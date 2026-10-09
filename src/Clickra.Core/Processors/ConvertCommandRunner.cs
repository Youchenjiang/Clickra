using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core.Processors;

/// <summary>Runs a convert command against FileProcessor. Both UIs dispatch through
/// this single implementation; UI-specific interactions (password / split-page
/// prompts) are supplied as delegates.</summary>
public static class ConvertCommandRunner
{
        /// <summary>Outcome of a tracked conversion run.</summary>
        public enum ConvertRunStatus { Succeeded, Canceled, Parked, Failed }

        /// <summary>Outcome of a tracked conversion run together with the failure message.</summary>
        public readonly record struct ConvertRunResult(ConvertRunStatus Status, string? Error, string TaskId = "");

        /// <summary>Groups the optional parameters for conversion execution.
        /// Keeps method signatures under the 7-parameter SonarCloud threshold.</summary>
        public sealed record ConversionOptions(
            Func<int, Task<string?>> PromptPassword,
            Func<int, string, Task<string?>> PromptSplitPages,
            int StartIndex = 0,
            string? ExistingTaskId = null,
            Dictionary<string, object>? CommandOptions = null,
            Action<int>? OnFileStarting = null,
            Action<string>? OnTaskStarted = null);

        /// <summary>
        /// 任務被「暫存」的信號：由 prompt delegate 在 UI 要求暫存（例如卡在密碼/分割
        /// 輸入時關窗）時拋出。RunTrackedAsync 會寫入 Parked 狀態（不寫歷史），
        /// 任務保留在 dashboard 供「繼續」或「取消」。
        /// </summary>
        public sealed class ParkedException : Exception
        {
            /// <summary>暫存當下正要處理的檔案索引（恢復時從這裡續跑）。</summary>
            public int NextFileIndex { get; }
            public ParkedException(string reason, int nextFileIndex) : base(reason) => NextFileIndex = nextFileIndex;
        }

        /// <summary>Executes a command while recording the active record in ClickraStorage.
        /// The progress delegate receives already-marshaled UI updates; the caller keeps
        /// handling the result-specific UI. Shared by both UIs so start/complete/cancel
        /// accounting stays in one place.</summary>
        public static async Task<ConvertRunResult> RunTrackedAsync(
            string command,
            List<string> files,
            List<string> outputs,
            Action<int, string> updateProgress,
            ConversionOptions options,
            CancellationToken token = default)
        {
            void Progress(int current, int total, string message)
            {
                int percent = total > 0 ? Math.Clamp((int)(current * 100.0 / total), 0, 100) : 0;
                updateProgress(percent, message);
            }

            var lifecycle = ConversionTaskLifecycle.Start(command, files, options.ExistingTaskId);
            options.OnTaskStarted?.Invoke(lifecycle.TaskId);
            try
            {
                await Task.Run(() => Run(command, files, outputs, Progress, options, token), token);
                lifecycle.CompleteSuccess(string.Join(";", outputs));
                return new ConvertRunResult(ConvertRunStatus.Succeeded, null, lifecycle.TaskId);
            }
            catch (ParkedException ex)
            {
                lifecycle.Park(ex.Message, ex.NextFileIndex);
                return new ConvertRunResult(ConvertRunStatus.Parked, ex.Message, lifecycle.TaskId);
            }
            catch (OperationCanceledException)
            {
                lifecycle.CompleteFailure("Canceled", string.Join(";", outputs));
                return new ConvertRunResult(ConvertRunStatus.Canceled, null, lifecycle.TaskId);
            }
            catch (Exception ex)
            {
                lifecycle.CompleteFailure(ex.Message, string.Join(";", outputs));
                return new ConvertRunResult(ConvertRunStatus.Failed, ex.Message, lifecycle.TaskId);
            }
        }

        /// <summary>Executes the given command. A null result from either prompt delegate
        /// cancels the operation. <paramref name="startIndex"/> lets a resumed (parked)
        /// batch skip files that already completed.</summary>
        public static void Run(
            string command,
            List<string> files,
            List<string> outputs,
            Action<int, int, string> progress,
            ConversionOptions options,
            CancellationToken token = default)
        {
            switch (command)
            {
                case "ppt2pdf":
                    FileProcessor.ConvertPptToPdf(files, progress, token);
                    break;
                case "word2pdf":
                    FileProcessor.ConvertWordToPdf(files, progress, token);
                    break;
                case "excel2pdf":
                    FileProcessor.ConvertExcelToPdf(files, progress, token);
                    break;
                case "md2pdf":
                {
                    Dictionary<string, object> markdownOptions = options.CommandOptions ?? MarkdownPdfOptions.Create();
                    RunPerFile(files, outputs, (f, o, p, t) => FileProcessor.ConvertMarkdownToPdf(f, o, markdownOptions, p, t), progress, options.StartIndex, options.OnFileStarting, token);
                    break;
                }
                case "md2word":
                {
                    Dictionary<string, object> markdownOptions = options.CommandOptions ?? MarkdownPdfOptions.Create();
                    RunPerFile(files, outputs, (f, o, p, t) => FileProcessor.ConvertMarkdownToWord(f, o, markdownOptions, p, t), progress, options.StartIndex, options.OnFileStarting, token);
                    break;
                }
                case "merge-pdf":
                    FileProcessor.MergePdfs(files, outputs[0], progress, token);
                    break;
                case "translate-pdf":
                    RunPerFile(files, outputs, (f, o, p, t) => FileProcessor.TranslatePdf(f, o, ClickraStorage.GetSetting(ClickraSettings.TranslateTargetLang), p, t), progress, options.StartIndex, options.OnFileStarting, token);
                    break;
                case "img-merge":
                    FileProcessor.ConvertImagesToPdf(files, outputs[0], progress, token);
                    break;
                case "img-stitch":
                    FileProcessor.StitchImages(files, outputs[0], progress, token);
                    break;
                case "img-compress":
                {
                    Dictionary<string, object> compressionOptions = ConvertCommandRegistry.ImageCompressionOptions();
                    RunPerFile(files, outputs, (f, o, p, t) => FileProcessor.CompressImage(f, o, compressionOptions, p, t), progress, options.StartIndex, options.OnFileStarting, token);
                    break;
                }
                case "img-to-png":
                case "img-to-jpg":
                case "img-to-webp":
                case "img-to-gif":
                case "img-to-heic":
                    RunImageConvert(
                        files,
                        outputs,
                        command["img-to-".Length..],
                        progress,
                        options.StartIndex,
                        options.OnFileStarting,
                        token);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown convert command '{command}'.");
            }
        }

        /// <summary>Converts each image to the target format, reusing the per-file
        /// loop with the format baked into the FileProcessor call.</summary>
        private static void RunImageConvert(List<string> files, List<string> outputs, string format, Action<int, int, string> progress, int startIndex, Action<int>? onFileStarting, CancellationToken token)
        {
            ConvertCommandRegistry.EnsureUniqueOutputPaths(outputs);
            RunPerFile(files, outputs, (f, o, p, t) => FileProcessor.ConvertImageFormat(f, o, format, p, t), progress, startIndex, onFileStarting, token);
        }

        private static void RunPerFile(List<string> files, List<string> outputs, Action<string, string, Action<int, int, string>, CancellationToken> action, Action<int, int, string> progress, int startIndex, Action<int>? onFileStarting, CancellationToken token)
        {
            for (int i = startIndex; i < files.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                onFileStarting?.Invoke(i);
                int index = i;
                action(files[i], outputs[i], (c, t, m) => progress((index * 100) + c, files.Count * 100, m), token);
            }
        }

    }

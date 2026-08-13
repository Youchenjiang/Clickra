using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core.Processors;

/// <summary>Runs a convert command against FileProcessor. Both UIs dispatch through
/// this single implementation; UI-specific interactions (password / split-page
/// prompts) are supplied as delegates.</summary>
public static class ConvertCommandRunner
{
        /// <summary>Outcome of a tracked conversion run.</summary>
        public enum ConvertRunStatus { Succeeded, Canceled, Failed }

        /// <summary>Outcome of a tracked conversion run together with the failure message.</summary>
        public readonly record struct ConvertRunResult(ConvertRunStatus Status, string? Error);

        /// <summary>Executes a command while recording the active record in ClickraStorage.
        /// The progress delegate receives already-marshaled UI updates; the caller keeps
        /// handling the result-specific UI. Shared by both UIs so start/complete/cancel
        /// accounting stays in one place.</summary>
        public static async Task<ConvertRunResult> RunTrackedAsync(
            string command,
            List<string> files,
            List<string> outputs,
            Action<int, string> updateProgress,
            Func<Task<string?>> promptPassword,
            Func<string, Task<string?>> promptSplitPages,
            CancellationToken token)
        {
            // Display timestamp for the history log; local time is what the user expects.
            string startTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); // skipcq: CS-W1091
            string inputs = string.Join(";", files);
            var stopwatch = Stopwatch.StartNew();

            void Progress(int current, int total, string message)
            {
                int percent = total > 0 ? Math.Clamp((int)(current * 100.0 / total), 0, 100) : 0;
                updateProgress(percent, message);
            }

            try
            {
                ClickraStorage.StartActiveRecord(command, files.Count, inputs);
                ClickraStorage.SetActiveRecordInProgress();
                await Task.Run(() => Run(command, files, outputs, Progress, promptPassword, promptSplitPages, token), token);
                stopwatch.Stop();
                ClickraStorage.CompleteActiveRecord(command, startTime, true, "", elapsedMs: stopwatch.ElapsedMilliseconds, inputPaths: inputs, outputPath: string.Join(";", outputs));
                return new ConvertRunResult(ConvertRunStatus.Succeeded, null);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                ClickraStorage.CompleteActiveRecord(command, startTime, false, "Canceled", elapsedMs: stopwatch.ElapsedMilliseconds, inputPaths: inputs, outputPath: string.Join(";", outputs));
                return new ConvertRunResult(ConvertRunStatus.Canceled, null);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                ClickraStorage.CompleteActiveRecord(command, startTime, false, ex.Message, elapsedMs: stopwatch.ElapsedMilliseconds, inputPaths: inputs, outputPath: string.Join(";", outputs));
                return new ConvertRunResult(ConvertRunStatus.Failed, ex.Message);
            }
        }

        /// <summary>Executes the given command. A null result from either prompt delegate
        /// cancels the operation.</summary>
        public static void Run(
            string command,
            List<string> files,
            List<string> outputs,
            Action<int, int, string> progress,
            Func<Task<string?>> promptPassword,
            Func<string, Task<string?>> promptSplitPages,
            CancellationToken token)
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
                case "merge-pdf":
                    FileProcessor.MergePdfs(files, outputs[0], progress, token);
                    break;
                case "compress-pdf":
                    RunPerFile(files, outputs, (f, o, p, t) => FileProcessor.CompressPdf(f, o, ConvertCommandRegistry.CompressionOptions(), p, t), progress, token);
                    break;
                case "translate-pdf":
                    RunPerFile(files, outputs, (f, o, p, t) => FileProcessor.TranslatePdf(f, o, ClickraStorage.GetSetting("TranslateTargetLang"), p, t), progress, token);
                    break;
                case "decrypt-pdf":
                    string? password = promptPassword().GetAwaiter().GetResult();
                    if (password is null) throw new OperationCanceledException(token);
                    RunPerFile(files, outputs, (f, o, p, t) => FileProcessor.DecryptPdf(f, o, password, p, t), progress, token);
                    break;
                case "split-pdf":
                    RunSplit(files, outputs, promptSplitPages, progress, token);
                    break;
                case "img2pdf":
                    RunPerFile(files, outputs, (f, o, p, t) => FileProcessor.ConvertImagesToPdf(new List<string> { f }, o, p, t), progress, token);
                    break;
                case "img-merge":
                    FileProcessor.ConvertImagesToPdf(files, outputs[0], progress, token);
                    break;
                case "img-stitch":
                    FileProcessor.StitchImages(files, outputs[0], progress, token);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown convert command '{command}'.");
            }
        }

        private static void RunPerFile(List<string> files, List<string> outputs, Action<string, string, Action<int, int, string>, CancellationToken> action, Action<int, int, string> progress, CancellationToken token)
        {
            for (int i = 0; i < files.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                int index = i;
                action(files[i], outputs[i], (c, t, m) => progress((index * 100) + c, files.Count * 100, m), token);
            }
        }

        private static void RunSplit(List<string> files, List<string> outputs, Func<string, Task<string?>> promptSplitPages, Action<int, int, string> progress, CancellationToken token)
        {
            for (int i = 0; i < files.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                string? splitPages = promptSplitPages(files[i]).GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(splitPages)) throw new OperationCanceledException(token);
                int index = i;
                FileProcessor.SplitPdf(files[i], outputs[i], splitPages, (c, t, m) => progress((index * 100) + c, files.Count * 100, m), token);
            }
        }
    }

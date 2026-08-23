using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PdfSharp.Pdf.IO;

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
            Func<int, Task<string?>> promptPassword,
            Func<int, string, Task<string?>> promptSplitPages,
            CancellationToken token,
            int startIndex = 0,
            string? existingTaskId = null)
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

            // 每個任務有獨立的進度檔（tasks/task-{id}.tmp），多個並行任務不會互相覆蓋。
            // resume 時沿用原任務檔（existingTaskId），避免重複建立與歷史重複寫入。
            string taskId = existingTaskId ?? ClickraStorage.StartTask(command, files.Count, inputs);
            ClickraStorage.SetTaskInProgress(taskId);
            try
            {
                await Task.Run(() => Run(command, files, outputs, Progress, promptPassword, promptSplitPages, token, startIndex), token);
                stopwatch.Stop();
                ClickraStorage.CompleteTask(taskId, command, startTime, true, "", endTime: null, stopwatch.ElapsedMilliseconds, inputs, string.Join(";", outputs));
                return new ConvertRunResult(ConvertRunStatus.Succeeded, null, taskId);
            }
            catch (ParkedException ex)
            {
                stopwatch.Stop();
                ClickraStorage.ParkTask(taskId, ex.Message, ex.NextFileIndex);
                return new ConvertRunResult(ConvertRunStatus.Parked, ex.Message, taskId);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                ClickraStorage.CompleteTask(taskId, command, startTime, false, "Canceled", endTime: null, stopwatch.ElapsedMilliseconds, inputs, string.Join(";", outputs));
                return new ConvertRunResult(ConvertRunStatus.Canceled, null, taskId);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                ClickraStorage.CompleteTask(taskId, command, startTime, false, ex.Message, endTime: null, stopwatch.ElapsedMilliseconds, inputs, string.Join(";", outputs));
                return new ConvertRunResult(ConvertRunStatus.Failed, ex.Message, taskId);
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
            Func<int, Task<string?>> promptPassword,
            Func<int, string, Task<string?>> promptSplitPages,
            CancellationToken token,
            int startIndex = 0)
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
                    RunPerFile(files, outputs, (f, o, p, t) => FileProcessor.CompressPdf(f, o, ConvertCommandRegistry.CompressionOptions(), p, t), progress, token, startIndex);
                    break;
                case "translate-pdf":
                    RunPerFile(files, outputs, (f, o, p, t) => FileProcessor.TranslatePdf(f, o, ClickraStorage.GetSetting("TranslateTargetLang"), p, t), progress, token, startIndex);
                    break;
                case "decrypt-pdf":
                    RunDecrypt(files, outputs, promptPassword, progress, token, startIndex);
                    break;
                case "split-pdf":
                    RunSplit(files, outputs, promptSplitPages, progress, token, startIndex);
                    break;
                case "img2pdf":
                    RunPerFile(files, outputs, (f, o, p, t) => FileProcessor.ConvertImagesToPdf(new List<string> { f }, o, p, t), progress, token, startIndex);
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

        private static void RunPerFile(List<string> files, List<string> outputs, Action<string, string, Action<int, int, string>, CancellationToken> action, Action<int, int, string> progress, CancellationToken token, int startIndex)
        {
            for (int i = startIndex; i < files.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                int index = i;
                action(files[i], outputs[i], (c, t, m) => progress((index * 100) + c, files.Count * 100, m), token);
            }
        }

        /// <summary>Removes the password from each PDF, trying an empty password first
        /// and prompting only when the file is actually encrypted (mirrors the native
        /// CLI flow). A null result from the prompt cancels the operation.</summary>
        private static void RunDecrypt(List<string> files, List<string> outputs, Func<int, Task<string?>> promptPassword, Action<int, int, string> progress, CancellationToken token, int startIndex)
        {
            for (int i = startIndex; i < files.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                string password = "";
                bool success = false;
                while (!success)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        int index = i;
                        FileProcessor.DecryptPdf(files[i], outputs[i], password, (c, t, m) => progress((index * 100) + c, files.Count * 100, m), token);
                        success = true;
                    }
                    catch (Exception ex) when (IsPasswordError(ex))
                    {
                        password = promptPassword(i).GetAwaiter().GetResult() ?? throw new OperationCanceledException(token);
                    }
                }
            }
        }

        /// <summary>Whether the exception indicates a wrong or missing PDF password.</summary>
        private static bool IsPasswordError(Exception ex)
            => ex is PdfReaderException &&
               ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);

        private static void RunSplit(List<string> files, List<string> outputs, Func<int, string, Task<string?>> promptSplitPages, Action<int, int, string> progress, CancellationToken token, int startIndex)
        {
            for (int i = startIndex; i < files.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                string? splitPages = promptSplitPages(i, files[i]).GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(splitPages)) throw new OperationCanceledException(token);
                int index = i;
                FileProcessor.SplitPdf(files[i], outputs[i], splitPages, (c, t, m) => progress((index * 100) + c, files.Count * 100, m), token);
            }
        }
    }

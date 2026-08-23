using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using Clickra.Core;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public partial class ProgressWindow
    {
        /// <summary>Runs the command on the background thread, driving the progress callback,
        /// password prompts and the visual splitter, then closes the window and records the
        /// outcome in the persistent history.</summary>
        // skipcq: CS-R1140
        private void RunProcessing(IntPtr hwnd)
        {
            string startTimeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            List<string> currentFiles = new List<string>();
            string cmd = "";
            string taskId = "";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                lock (_stateLock)
                {
                    currentFiles = _files ?? new List<string>();
                    cmd = _command;
                }

                if (currentFiles.Count == 0)
                {
                    lock (_stateLock) { _completed = true; _message = "無檔案可處理。"; }
                    PostMessageW(hwnd, WM_USER_INVALIDATE, (IntPtr)1, IntPtr.Zero);
                    Thread.Sleep(1000);
                    PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
                    return;
                }

                Action<int, int, string> progressCallback = (curr, tot, msg) =>
                {
                    lock (_stateLock)
                    {
                        _current = curr;
                        if (tot > 0) _total = tot;
                        _message = msg;
                        if (_total > 0) _targetWidth = 448.0 * _current / _total;
                    }
                    UpdateTrayIconProgress();
                };

                // 立即建立 Pending 任務紀錄，讓 Dashboard 可即時看到；每個任務有
                // 獨立的進度檔（tasks/task-{id}.tmp），並行任務不會互相覆蓋。
                string inputsStr = string.Join(";", currentFiles);
                try
                {
                    taskId = ClickraStorage.StartTask(cmd, currentFiles.Count, inputsStr);
                    ClickraStorage.SetTaskInProgress(taskId);
                }
                catch { }

                string outputDir = ClickraStorage.GetOutputDir(currentFiles[0]);

                switch (cmd)
                {
                    case "ppt2pdf":
                        FileProcessor.ConvertPptToPdf(currentFiles, progressCallback, _cts.Token);
                        break;
                    case "word2pdf":
                        FileProcessor.ConvertWordToPdf(currentFiles, progressCallback, _cts.Token);
                        break;
                    case "excel2pdf":
                        FileProcessor.ConvertExcelToPdf(currentFiles, progressCallback, _cts.Token);
                        break;
                    case "merge-pdf":
                        FileProcessor.MergePdfs(currentFiles, Path.Combine(outputDir, "Merged_PDF.pdf"), progressCallback, _cts.Token);
                        break;
                    case "compress-pdf":
                        RunCompressPdf(currentFiles, outputDir, progressCallback);
                        break;
                    case "img2pdf":
                        RunImg2Pdf(currentFiles, outputDir, progressCallback);
                        break;
                    case "img-merge":
                        FileProcessor.ConvertImagesToPdf(currentFiles, Path.Combine(outputDir, "Merged_Images.pdf"), progressCallback, _cts.Token);
                        break;
                    case "img-stitch":
                        FileProcessor.StitchImages(currentFiles, Path.Combine(outputDir, "Stitched_Image.png"), progressCallback, _cts.Token);
                        break;
                    case "translate-pdf":
                        RunTranslatePdf(currentFiles, outputDir, progressCallback);
                        break;
                    case "split-pdf":
                        RunSplitPdf(hwnd, currentFiles, outputDir, progressCallback);
                        break;
                    case "decrypt-pdf":
                        RunDecryptPdf(hwnd, currentFiles, outputDir, progressCallback);
                        break;
                }

                sw.Stop();
                long elapsedMs = sw.ElapsedMilliseconds;
                string endTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string inputs = string.Join(";", currentFiles);
                string outputs = GetOutputPath(cmd, currentFiles, outputDir);

                lock (_stateLock)
                {
                    _completed = true;
                    if (cmd != "compress-pdf")
                        _message = "所有作業已順利完成！";
                }
                PostMessageW(hwnd, WM_USER_INVALIDATE, (IntPtr)1, IntPtr.Zero);

                // 完成：寫入持久化日誌並暫留 Success 狀態供 Dashboard 讀取
                try { ClickraStorage.CompleteTask(taskId, cmd, startTimeStr, true, "", endTime, elapsedMs, inputs, outputs); } catch { }

                ShowToastNotification(cmd, currentFiles.Count);

                Thread.Sleep(1500);
                try { ClickraStorage.DeleteTask(taskId); } catch { }
                PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            }
            catch (Exception ex)
            {
                sw.Stop();
                long elapsedMs = sw.ElapsedMilliseconds;
                string endTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string inputs = string.Join(";", currentFiles);
                string outputDir = currentFiles.Count > 0 ? ClickraStorage.GetOutputDir(currentFiles[0]) : "";
                string outputs = currentFiles.Count > 0 ? GetOutputPath(cmd, currentFiles, outputDir) : "";

                bool wasCanceled = _cts.IsCancellationRequested || ex is OperationCanceledException;
                string errorMsg = wasCanceled ? "User Aborted" : ex.Message;

                lock (_stateLock)
                {
                    _hasError = true;
                    _errorMessage = errorMsg;
                }
                PostMessageW(hwnd, WM_USER_INVALIDATE, (IntPtr)1, IntPtr.Zero);

                try { ClickraStorage.CompleteTask(taskId, cmd, startTimeStr, false, errorMsg, endTime, elapsedMs, inputs, outputs); } catch { }

                try { ClickraStorage.DeleteTask(taskId); } catch { }
                PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            }
        }

        /// <summary>Compresses each PDF with the saved quality settings, reporting per-file
        /// progress through the callback.</summary>
        private void RunCompressPdf(List<string> files, string outputDir, Action<int, int, string> progressCallback)
        {
            string compressionSummary = "";
            for (int i = 0; i < files.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                try { ClickraStorage.SetActiveRecordIndex(i); } catch { /* Non-critical UI state; ignore if storage unavailable */ }
                var f = files[i];
                string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_compressed.pdf");
                progressCallback((i * 100) + 10, files.Count * 100, $"正在壓縮 PDF: {Path.GetFileName(f)} ({i + 1}/{files.Count})...");

                var pdfOptions = BuildPdfCompressOptions();

                FileProcessor.CompressPdf(f, outName, pdfOptions, (curr, tot, msg) => {
                    int progressPct = tot > 0 ? (int)(curr * 80.0 / tot) + 10 : 10;
                    if (curr >= tot && !string.IsNullOrWhiteSpace(msg))
                        compressionSummary = msg;
                    progressCallback((i * 100) + progressPct, files.Count * 100, $"[PDF 壓縮] {msg} ({i + 1}/{files.Count})");
                }, _cts.Token);
            }
            _cts.Token.ThrowIfCancellationRequested();
            progressCallback(files.Count * 100, files.Count * 100,
                string.IsNullOrWhiteSpace(compressionSummary) ? "PDF 壓縮完成。" : compressionSummary);
        }

        /// <summary>Builds the PDF compression options dictionary from saved settings.</summary>
        private static Dictionary<string, object> BuildPdfCompressOptions()
        {
            string qualityStr = ClickraStorage.GetSetting("PdfCompressJpegQuality");
            if (string.IsNullOrEmpty(qualityStr)) qualityStr = "75";
            string dpiStr = ClickraStorage.GetSetting("PdfCompressTargetDpi");
            if (string.IsNullOrEmpty(dpiStr)) dpiStr = "150";
            if (!int.TryParse(dpiStr, out int dpi)) dpi = 150;
            string stripStr = ClickraStorage.GetSetting("PdfCompressStripFonts");
            if (string.IsNullOrEmpty(stripStr)) stripStr = "false";
            string minifyStr = ClickraStorage.GetSetting("PdfCompressMinifyContent");
            if (string.IsNullOrEmpty(minifyStr)) minifyStr = "true";
            if (!int.TryParse(qualityStr, out int quality)) quality = 75;

            return new Dictionary<string, object>
            {
                { "target_dpi", dpi },
                { "jpeg_quality", quality },
                { "strip_fonts", stripStr.Equals("true", StringComparison.OrdinalIgnoreCase) },
                { "minify_content", minifyStr.Equals("true", StringComparison.OrdinalIgnoreCase) }
            };
        }

        /// <summary>Converts each image to its own PDF, reporting per-file progress.</summary>
        private void RunImg2Pdf(List<string> files, string outputDir, Action<int, int, string> progressCallback)
        {
            for (int i = 0; i < files.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                try { ClickraStorage.SetActiveRecordIndex(i); } catch { /* Ignored: history recording must not abort processing. */ }
                var f = files[i];
                string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + ".pdf");
                progressCallback((i * 100) + 50, files.Count * 100, $"正在轉換圖片: {Path.GetFileName(f)} ({i + 1}/{files.Count})...");
                FileProcessor.ConvertImagesToPdf(new List<string> { f }, outName, null, _cts.Token);
            }
            _cts.Token.ThrowIfCancellationRequested();
            progressCallback(files.Count * 100, files.Count * 100, "轉換完成，正在儲存 PDF...");
        }

        /// <summary>Translates each PDF to the saved target language, reporting per-file
        /// progress through the callback.</summary>
        private void RunTranslatePdf(List<string> files, string outputDir, Action<int, int, string> progressCallback)
        {
            string targetLang = ClickraStorage.GetSetting("TranslateTargetLang");
            for (int i = 0; i < files.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                try { ClickraStorage.SetActiveRecordIndex(i); } catch { /* Ignored: history recording must not abort processing. */ }
                var f = files[i];
                string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_translated.pdf");
                progressCallback((i * 100) + 10, files.Count * 100, $"正在翻譯 PDF: {Path.GetFileName(f)} ({i + 1}/{files.Count})...");
                FileProcessor.TranslatePdf(f, outName, targetLang, (curr, tot, msg) => {
                    int progressPct = tot > 0 ? (int)(curr * 80.0 / tot) + 10 : 10;
                    progressCallback((i * 100) + progressPct, files.Count * 100, $"[PDF 翻譯] {msg} ({i + 1}/{files.Count})");
                }, _cts.Token);
            }
            _cts.Token.ThrowIfCancellationRequested();
            progressCallback(files.Count * 100, files.Count * 100, "翻譯完成，正在儲存 PDF...");
        }

        /// <summary>Splits each PDF, prompting the visual splitter when no --pages range
        /// was supplied on the command line.</summary>
        private void RunSplitPdf(IntPtr hwnd, List<string> files, string outputDir, Action<int, int, string> progressCallback)
        {
            string pagesOption = GetSplitPagesOptionFromCommandLine();
            for (int i = 0; i < files.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                try { ClickraStorage.SetActiveRecordIndex(i); } catch { /* Ignored: history recording must not abort processing. */ }
                var f = files[i];
                string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_split.pdf");

                string targetPages = ResolveSplitTargetPages(hwnd, f, pagesOption);

                progressCallback((i * 100) + 10, files.Count * 100, $"正在分割 PDF: {Path.GetFileName(f)} ({i + 1}/{files.Count})...");
                FileProcessor.SplitPdf(f, outName, targetPages, (curr, tot, msg) => {
                    int progressPct = tot > 0 ? (int)(curr * 80.0 / tot) + 10 : 10;
                    progressCallback((i * 100) + progressPct, files.Count * 100, $"[PDF 分割] {msg} ({i + 1}/{files.Count})");
                }, _cts.Token);
            }
            _cts.Token.ThrowIfCancellationRequested();
            progressCallback(files.Count * 100, files.Count * 100, "PDF 分割完成。");
        }

        /// <summary>Reads the --pages / -p page-range option from the command line.</summary>
        private static string GetSplitPagesOptionFromCommandLine()
        {
            string pagesOption = "prompt";
            var cliArgs = Environment.GetCommandLineArgs();
            for (int a = 0; a < cliArgs.Length - 1; a++)
            {
                if (cliArgs[a].Equals("--pages", StringComparison.OrdinalIgnoreCase) || cliArgs[a].Equals("-p", StringComparison.OrdinalIgnoreCase))
                {
                    pagesOption = cliArgs[a + 1];
                    break;
                }
            }
            return pagesOption;
        }

        /// <summary>Returns the page range for a split, launching the visual splitter (or
        /// password prompt) when no --pages range was supplied.</summary>
        private string ResolveSplitTargetPages(IntPtr hwnd, string filePath, string pagesOption)
        {
            string targetPages = pagesOption;
            if (!string.IsNullOrEmpty(targetPages) && !targetPages.Equals("prompt", StringComparison.OrdinalIgnoreCase))
            {
                return targetPages;
            }

            lock (_stateLock)
            {
                _isPromptingPassword = true;
                _isPromptingVisualSplitter = true;
                _passwordPromptFilename = filePath;
                _passwordPromptIsRetry = false;
                _inputPassword = null;
                _passwordCancelled = false;
                InitializeVisualSplitter(filePath);
            }

            PostMessageW(hwnd, WM_USER_SHOW_PASSWORD_INPUT, IntPtr.Zero, IntPtr.Zero);
            _passwordEvent.WaitOne();

            bool cancelled = false;
            lock (_stateLock)
            {
                cancelled = _passwordCancelled;
                targetPages = string.IsNullOrWhiteSpace(_inputPassword) ? BuildVisualSplitSpec() : _inputPassword.Trim();
                _isPromptingPassword = false;
                _isPromptingVisualSplitter = false;
            }

            if (cancelled)
            {
                throw new OperationCanceledException("使用者已取消頁碼範圍輸入。");
            }
            return targetPages;
        }

        /// <summary>Removes the password from each PDF, re-prompting until the correct
        /// password is supplied or the user cancels.</summary>
        private void RunDecryptPdf(IntPtr hwnd, List<string> files, string outputDir, Action<int, int, string> progressCallback)
        {
            for (int i = 0; i < files.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                DecryptSingleFile(hwnd, files[i], outputDir, i, files.Count, progressCallback);
            }
            _cts.Token.ThrowIfCancellationRequested();
            progressCallback(files.Count * 100, files.Count * 100, "密碼去除完成，正在儲存 PDF...");
        }

        /// <summary>Removes the password from one PDF, re-prompting until the correct
        /// password is supplied or the user cancels.</summary>
        private void DecryptSingleFile(IntPtr hwnd, string f, string outputDir, int index, int total, Action<int, int, string> progressCallback)
        {
            try { ClickraStorage.SetActiveRecordIndex(index); } catch { /* Ignored: history recording must not abort processing. */ }
            string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_decrypted.pdf");
            progressCallback((index * 100) + 10, total * 100, $"正在去除密碼: {Path.GetFileName(f)} ({index + 1}/{total})...");

            string currentPassword = "";
            bool success = false;
            bool isRetry = false;
            while (!success)
            {
                _cts.Token.ThrowIfCancellationRequested();
                try
                {
                    FileProcessor.DecryptPdf(f, outName, currentPassword, (curr, tot, msg) => {
                        int progressPct = tot > 0 ? (int)(curr * 80.0 / tot) + 10 : 10;
                        progressCallback((index * 100) + progressPct, total * 100, $"[去除密碼] {msg} ({index + 1}/{total})");
                    }, _cts.Token);
                    success = true;
                }
                catch (Exception ex)
                {
                    if (!IsPasswordError(ex))
                    {
                        throw;
                    }
                    currentPassword = ResolveDecryptPassword(hwnd, f, isRetry);
                    isRetry = true;
                }
            }
        }

        /// <summary>Whether the exception indicates a wrong or missing PDF password.</summary>
        private static bool IsPasswordError(Exception ex)
            => ex is PdfSharp.Pdf.IO.PdfReaderException &&
               ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);

        /// <summary>Shows the password prompt and waits for the user's input, throwing
        /// OperationCanceledException when the prompt is cancelled.</summary>
        private string ResolveDecryptPassword(IntPtr hwnd, string filePath, bool isRetry)
        {
            lock (_stateLock)
            {
                _isPromptingPassword = true;
                _passwordPromptFilename = filePath;
                _passwordPromptIsRetry = isRetry;
                _inputPassword = null;
                _passwordCancelled = false;
            }

            PostMessageW(hwnd, WM_USER_SHOW_PASSWORD_INPUT, IntPtr.Zero, IntPtr.Zero);
            _passwordEvent.WaitOne();

            // The prompt window writes these fields on the UI thread while this
            // thread waits on _passwordEvent, so read them through Volatile.Read.
            bool cancelled = Volatile.Read(ref _passwordCancelled);
            string? input = Volatile.Read(ref _inputPassword);
            lock (_stateLock)
            {
                _isPromptingPassword = false;
            }

            if (cancelled)
            {
                throw new OperationCanceledException(Localization.T("error_user_aborted", ClickraStorage.GetSetting("Language")));
            }
            return input ?? "";
        }

        /// <summary>Returns the expected output path(s) for a completed command, used for history logging.</summary>
        private static string GetOutputPath(string cmd, List<string> inputFiles, string outputDir)
        {
            switch (cmd)
            {
                case "merge-pdf":
                    return Path.Combine(outputDir, "Merged_PDF.pdf");
                case "img-merge":
                    return Path.Combine(outputDir, "Merged_Images.pdf");
                case "img-stitch":
                    return Path.Combine(outputDir, "Stitched_Image.png");
                case "ppt2pdf":
                case "word2pdf":
                case "excel2pdf":
                case "img2pdf":
                    return string.Join(";", inputFiles.Select(f => Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + ".pdf")));
                case "translate-pdf":
                    return string.Join(";", inputFiles.Select(f => Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_translated.pdf")));
                case "decrypt-pdf":
                    return string.Join(";", inputFiles.Select(f => Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_decrypted.pdf")));
                case "compress-pdf":
                    return string.Join(";", inputFiles.Select(f => Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_compressed.pdf")));
                default:
                    return outputDir;
            }
        }

        /// <summary>Shows a Windows toast notification on success, unless notifications are disabled.</summary>
        private void ShowToastNotification(string command, int count)
        {
            if (ClickraStorage.GetSetting("Notification") == "false")
                return;

            try
            {
                string title = "Clickra 轉換成功";
                string body = $"已順利完成 {command} 作業 (共 {count} 個檔案)。";
                
                string psScript = $@"
$ErrorActionPreference = 'Stop'
try {{
    [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
    $template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
    $textNodes = $template.GetElementsByTagName('text')
    $textNodes.Item(0).AppendChild($template.CreateTextNode('{title.Replace("'", "''").Replace("`", "``").Replace("\"", "`\"")}')) | Out-Null
    $textNodes.Item(1).AppendChild($template.CreateTextNode('{body.Replace("'", "''").Replace("`", "``").Replace("\"", "`\"")}')) | Out-Null
    $toast = [Windows.UI.Notifications.ToastNotification]::new($template)
    $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Clickra')
    $notifier.Show($toast)
}} catch {{
    # 忽略 Toast 失敗
}}";

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(startInfo);
                p?.WaitForExit();
            }
            catch { }
        }
    }
}

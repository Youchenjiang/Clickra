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
        private void RunProcessing(IntPtr hwnd)
        {
            string startTimeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            List<string> currentFiles = new List<string>();
            string cmd = "";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                lock (_stateLock)
                {
                    currentFiles = _files;
                    cmd = _command;
                }

                if (currentFiles == null || currentFiles.Count == 0)
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

                // 立即建立 Pending 紀錄，讓 Dashboard 可即時看到
                string inputsStr = string.Join(";", currentFiles);
                try { ClickraStorage.StartActiveRecord(cmd, currentFiles.Count, inputsStr); } catch { }

                string outputDir = ClickraStorage.GetOutputDir(currentFiles[0]);

                // 開始實際處理，切換為 InProgress
                try { ClickraStorage.SetActiveRecordInProgress(); } catch { }

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
                        string compressionSummary = "";
                        for (int i = 0; i < currentFiles.Count; i++)
                        {
                            _cts.Token.ThrowIfCancellationRequested();
                            try { ClickraStorage.SetActiveRecordIndex(i); } catch { /* Non-critical UI state; ignore if storage unavailable */ }
                            var f = currentFiles[i];
                            string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_compressed.pdf");
                            progressCallback((i * 100) + 10, currentFiles.Count * 100, $"正在壓縮 PDF: {Path.GetFileName(f)} ({i + 1}/{currentFiles.Count})...");
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

                            var pdfOptions = new Dictionary<string, object>
                            {
                                { "target_dpi", dpi },
                                { "jpeg_quality", quality },
                                { "strip_fonts", stripStr.Equals("true", StringComparison.OrdinalIgnoreCase) },
                                { "minify_content", minifyStr.Equals("true", StringComparison.OrdinalIgnoreCase) }
                            };

                            FileProcessor.CompressPdf(f, outName, pdfOptions, (curr, tot, msg) => {
                                int progressPct = tot > 0 ? (int)(curr * 80.0 / tot) + 10 : 10;
                                if (curr >= tot && !string.IsNullOrWhiteSpace(msg))
                                    compressionSummary = msg;
                                progressCallback((i * 100) + progressPct, currentFiles.Count * 100, $"[PDF 壓縮] {msg} ({i + 1}/{currentFiles.Count})");
                            }, _cts.Token);
                        }
                        _cts.Token.ThrowIfCancellationRequested();
                        progressCallback(currentFiles.Count * 100, currentFiles.Count * 100,
                            string.IsNullOrWhiteSpace(compressionSummary) ? "PDF 壓縮完成。" : compressionSummary);
                        break;
                    case "img2pdf":
                        for (int i = 0; i < currentFiles.Count; i++)
                        {
                            _cts.Token.ThrowIfCancellationRequested();
                            try { ClickraStorage.SetActiveRecordIndex(i); } catch { }
                            var f = currentFiles[i];
                            string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + ".pdf");
                            progressCallback((i * 100) + 50, currentFiles.Count * 100, $"正在轉換圖片: {Path.GetFileName(f)} ({i + 1}/{currentFiles.Count})...");
                            FileProcessor.ConvertImagesToPdf(new List<string> { f }, outName, null, _cts.Token);
                        }
                        _cts.Token.ThrowIfCancellationRequested();
                        progressCallback(currentFiles.Count * 100, currentFiles.Count * 100, "轉換完成，正在儲存 PDF...");
                        break;
                    case "img-merge":
                        FileProcessor.ConvertImagesToPdf(currentFiles, Path.Combine(outputDir, "Merged_Images.pdf"), progressCallback, _cts.Token);
                        break;
                    case "img-stitch":
                        FileProcessor.StitchImages(currentFiles, Path.Combine(outputDir, "Stitched_Image.png"), progressCallback, _cts.Token);
                        break;
                    case "translate-pdf":
                        {
                            string targetLang = ClickraStorage.GetSetting("TranslateTargetLang");
                            for (int i = 0; i < currentFiles.Count; i++)
                            {
                                _cts.Token.ThrowIfCancellationRequested();
                                try { ClickraStorage.SetActiveRecordIndex(i); } catch { }
                                var f = currentFiles[i];
                                string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_translated.pdf");
                                progressCallback((i * 100) + 10, currentFiles.Count * 100, $"正在翻譯 PDF: {Path.GetFileName(f)} ({i + 1}/{currentFiles.Count})...");
                                FileProcessor.TranslatePdf(f, outName, targetLang, (curr, tot, msg) => {
                                    int progressPct = tot > 0 ? (int)(curr * 80.0 / tot) + 10 : 10;
                                    progressCallback((i * 100) + progressPct, currentFiles.Count * 100, $"[PDF 翻譯] {msg} ({i + 1}/{currentFiles.Count})");
                                }, _cts.Token);
                            }
                            _cts.Token.ThrowIfCancellationRequested();
                            progressCallback(currentFiles.Count * 100, currentFiles.Count * 100, "翻譯完成，正在儲存 PDF...");
                        }
                        break;
                    case "split-pdf":
                        for (int i = 0; i < currentFiles.Count; i++)
                        {
                            _cts.Token.ThrowIfCancellationRequested();
                            try { ClickraStorage.SetActiveRecordIndex(i); } catch { }
                            var f = currentFiles[i];
                            string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_split.pdf");
                            progressCallback((i * 100) + 10, currentFiles.Count * 100, $"正在分割 PDF: {Path.GetFileName(f)} ({i + 1}/{currentFiles.Count})...");
                            FileProcessor.SplitPdf(f, outName, "all", (curr, tot, msg) => {
                                int progressPct = tot > 0 ? (int)(curr * 80.0 / tot) + 10 : 10;
                                progressCallback((i * 100) + progressPct, currentFiles.Count * 100, $"[PDF 分割] {msg} ({i + 1}/{currentFiles.Count})");
                            }, _cts.Token);
                        }
                        _cts.Token.ThrowIfCancellationRequested();
                        progressCallback(currentFiles.Count * 100, currentFiles.Count * 100, "PDF 分割完成。");
                        break;
                    case "decrypt-pdf":
                        for (int i = 0; i < currentFiles.Count; i++)
                        {
                            _cts.Token.ThrowIfCancellationRequested();
                            try { ClickraStorage.SetActiveRecordIndex(i); } catch { }
                            var f = currentFiles[i];
                            string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_decrypted.pdf");
                            progressCallback((i * 100) + 10, currentFiles.Count * 100, $"正在去除密碼: {Path.GetFileName(f)} ({i + 1}/{currentFiles.Count})...");

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
                                        progressCallback((i * 100) + progressPct, currentFiles.Count * 100, $"[去除密碼] {msg} ({i + 1}/{currentFiles.Count})");
                                    }, _cts.Token);
                                    success = true;
                                }
                                catch (Exception ex)
                                {
                                    bool isPasswordError = ex is PdfSharp.Pdf.IO.PdfReaderException &&
                                                           ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);

                                    if (isPasswordError)
                                    {
                                        lock (_stateLock)
                                        {
                                            _isPromptingPassword = true;
                                            _passwordPromptFilename = f;
                                            _passwordPromptIsRetry = isRetry;
                                            _inputPassword = null;
                                            _passwordCancelled = false;
                                        }

                                        PostMessageW(hwnd, WM_USER_SHOW_PASSWORD_INPUT, IntPtr.Zero, IntPtr.Zero);

                                        _passwordEvent.WaitOne();

                                        bool cancelled;
                                        string? input;
                                        lock (_stateLock)
                                        {
                                            cancelled = _passwordCancelled;
                                            input = _inputPassword;
                                            _isPromptingPassword = false;
                                        }

                                        if (cancelled)
                                        {
                                            throw new OperationCanceledException(Localization.T("error_user_aborted", ClickraStorage.GetSetting("Language")));
                                        }

                                        currentPassword = input ?? "";
                                        isRetry = true;
                                    }
                                    else
                                    {
                                        throw;
                                    }
                                }
                            }
                        }
                        _cts.Token.ThrowIfCancellationRequested();
                        progressCallback(currentFiles.Count * 100, currentFiles.Count * 100, "密碼去除完成，正在儲存 PDF...");
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
                try { ClickraStorage.CompleteActiveRecord(cmd, startTimeStr, true, "", endTime, elapsedMs, inputs, outputs); } catch { }

                ShowToastNotification(cmd, currentFiles.Count);

                Thread.Sleep(1500);
                try { ClickraStorage.ClearActiveRecord(); } catch { }
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

                // 失敗：立即寫入持久化日誌並暫留 Failed 狀態供 Dashboard 讀取
                try { ClickraStorage.CompleteActiveRecord(cmd, startTimeStr, false, errorMsg, endTime, elapsedMs, inputs, outputs); } catch { }

                if (!wasCanceled)
                {
                    string lang = ClickraStorage.GetSetting("Language");
                    MessageBox(
                        hwnd,
                        string.Format(Localization.T("error_processing_failed", lang), ex.Message),
                        $"Clickra — {Localization.T("status_error", lang)}",
                        0x10); // MB_ICONERROR
                }
                try { ClickraStorage.ClearActiveRecord(); } catch { }
                PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            }
        }

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

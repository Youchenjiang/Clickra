using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Drawing2D;
using Clickra.Core;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    /// <summary>
    /// 提供 CLI 執行階段專專用之 Win32 進度視窗。
    /// </summary>
    public partial class ProgressWindow
    {
        delegate IntPtr WndProcDelegate(IntPtr h, uint msg, IntPtr w, IntPtr l);
        static readonly WndProcDelegate _wndProcDelegate = WndProc;

        private CancellationTokenSource _cts = new CancellationTokenSource();
        private NOTIFYICONDATAW _nid;
        private bool _trayIconAdded = false;
        private bool _isTrayBtnHovered = false;
        private IntPtr _hIcon = IntPtr.Zero;

        private const uint WM_TRAYICON = 0x0400 + 1;
        private const uint WM_USER_INVALIDATE = 0x0400 + 2;
        private const uint WM_USER_SHOW_PASSWORD_INPUT = 0x0400 + 3;
        private const uint WM_USER_HIDE_PASSWORD_INPUT = 0x0400 + 4;

        private const uint NIM_ADD = 0;
        private const uint NIM_MODIFY = 1;
        private const uint NIM_DELETE = 2;
        private const uint NIF_MESSAGE = 1;
        private const uint NIF_ICON = 2;
        private const uint NIF_TIP = 4;
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;

        private const uint WS_CLIPCHILDREN = 0x02000000;
        private const uint WS_CHILD = 0x40000000;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint WS_BORDER = 0x00800000;
        private const uint WS_TABSTOP = 0x00010000;

        private readonly AutoResetEvent _passwordEvent = new AutoResetEvent(false);
        private string? _inputPassword = null;
        private bool _passwordCancelled = false;
        private volatile bool _isPromptingPassword = false;
        private string _passwordPromptFilename = "";
        private bool _passwordPromptIsRetry = false;
        private IntPtr _hwndEdit = IntPtr.Zero;
        private IntPtr _hwndBtnOk = IntPtr.Zero;
        private IntPtr _hwndBtnCancel = IntPtr.Zero;
        private IntPtr _editBgBrush = IntPtr.Zero;
        private IntPtr _darkBrush = IntPtr.Zero;
        private IntPtr _hFont = IntPtr.Zero;

        private readonly object _stateLock = new object();

        private string _command = "";
        private List<string> _files = new List<string>();
        private int _current = 0;
        private int _total = 0;
        private string _message = "";
        private bool _completed = false;
        private bool _hasError = false;
        private string _errorMessage = "";
        private IntPtr _hwnd = IntPtr.Zero;

        private double _currentDispWidth = 0;
        private double _targetWidth = 0;
        private float _shimmerOffset = -120;
        private float _dpiScale = 1.0f;
        private float _scrollOffset = 0f;
        private bool _isDraggingScroll = false;
        private float _dragStartMouseX = 0f;
        private float _dragStartOffset = 0f;

        // GDI+ 雙雙緩衝與色彩快取
        private Bitmap? _bufferBmp;
        private Graphics? _bufferGraphics;
        private Color _cachedColorizationColor = Color.FromArgb(255, 0, 120, 212);
        private bool _hasCachedColorizationColor = false;

        // GDI+ 快取字型與筆刷
        private Font? _titleFont;
        private Font? _subFont;
        private Font? _headerFont;
        private Font? _msgFont;
        private Font? _tipFont;
        private Font? _pctFont;
        private Pen? _linePen;
        private Pen? _borderPen;
        private SolidBrush? _bgBrush;

        private void RecreateScaledFonts()
        {
            try { _titleFont?.Dispose(); _titleFont = null; } catch { }
            try { _subFont?.Dispose(); _subFont = null; } catch { }
            try { _headerFont?.Dispose(); _headerFont = null; } catch { }
            try { _msgFont?.Dispose(); _msgFont = null; } catch { }
            try { _tipFont?.Dispose(); _tipFont = null; } catch { }
            try { _pctFont?.Dispose(); _pctFont = null; } catch { }

            string lang = ClickraStorage.GetSetting("Language");
            lang = Localization.NormalizeLanguageCode(lang);
            string fontName = "Segoe UI";
            if (lang.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) || lang.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase))
                fontName = "Microsoft JhengHei UI";
            else if (lang.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase) || lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                fontName = "Microsoft YaHei UI";
            else if (lang.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                fontName = "Yu Gothic UI";
            else if (lang.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
                fontName = "Malgun Gothic";

            float s = _dpiScale;
            _titleFont = new Font("Segoe UI Variable Display", 32f * s, FontStyle.Bold, GraphicsUnit.Pixel);
            _subFont = new Font(fontName, 14.67f * s, GraphicsUnit.Pixel);
            _headerFont = new Font(fontName, 21.33f * s, FontStyle.Bold, GraphicsUnit.Pixel);
            _msgFont = new Font(fontName, 14.67f * s, GraphicsUnit.Pixel);
            _tipFont = new Font(fontName, 12f * s, GraphicsUnit.Pixel);
            _pctFont = new Font(fontName, 13.33f * s, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        public static void Show(string command, List<string> files)
        {
            var window = new ProgressWindow();
            window.ShowInstance(command, files);
        }

        private void ShowInstance(string command, List<string> files)
        {
            if (files == null || files.Count == 0)
            {
                MessageBox(IntPtr.Zero, "未傳入任何檔案進行處理。", "Clickra — 警告", 0x30); // MB_ICONWARNING
                return;
            }

            lock (_stateLock)
            {
                _command = command;
                _files = files;
                _current = 0;
                _total = files.Count * 100;
                _message = "正在準備處理...";
                _completed = false;
                _hasError = false;
                _errorMessage = "";
                _currentDispWidth = 0;
                _targetWidth = 0;
                _shimmerOffset = -120;
                _scrollOffset = 0f;
            }

            uint dpi = 96;
            try { dpi = GetDpiForSystem(); } catch {}
            _dpiScale = dpi / 96.0f;

            if (_darkBrush == IntPtr.Zero) _darkBrush = CreateSolidBrush(0x00202020);
            if (_editBgBrush == IntPtr.Zero) _editBgBrush = CreateSolidBrush(0x002D2D2D);

            RecreateScaledFonts();
            _linePen ??= new Pen(Color.FromArgb(60, 60, 60), 1f * _dpiScale);
            _borderPen ??= new Pen(Color.FromArgb(70, 70, 70), 1f * _dpiScale);
            _bgBrush ??= new SolidBrush(Color.FromArgb(45, 45, 45));

            int clientW = (int)(520 * _dpiScale);
            int clientH = (int)(280 * _dpiScale);

            if (_bufferBmp == null)
            {
                _bufferBmp = new Bitmap(clientW, clientH);
                _bufferGraphics = Graphics.FromImage(_bufferBmp);
                _bufferGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                _bufferGraphics.SmoothingMode = SmoothingMode.AntiAlias;
            }

            _hasCachedColorizationColor = false;

            string className = "ClickraProgressWnd";
            IntPtr hClass = Marshal.StringToHGlobalUni(className);

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                hInstance = GetModuleHandle(null),
                hCursor = LoadCursorW(IntPtr.Zero, 32512),
                hbrBackground = IntPtr.Zero,
                lpszClassName = hClass
            };

            RegisterClassEx(ref wc);

            var rect = new RECT { left = 0, top = 0, right = clientW, bottom = clientH };
            AdjustWindowRectEx(ref rect, WS_OVERLAPPED_FIXED, false, 0);
            int winW = rect.right - rect.left;
            int winH = rect.bottom - rect.top;

            GCHandle handle = GCHandle.Alloc(this);
            IntPtr lpParam = GCHandle.ToIntPtr(handle);

            _hwnd = CreateWindowEx(0, className, "Clickra",
                WS_OVERLAPPED_FIXED, CW_USEDEFAULT, CW_USEDEFAULT, winW, winH,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, lpParam);

            int dark = 1;
            DwmSetWindowAttribute(_hwnd, DWMWA_DARK_MODE, ref dark, sizeof(int));
            SetWindowText(_hwnd, "Clickra");

            string exePath = Environment.ProcessPath ?? "";
            if (!string.IsNullOrEmpty(exePath))
            {
                var hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
                if (hIcon != IntPtr.Zero)
                {
                    _hIcon = hIcon;
                    SendMessageW(_hwnd, 0x0080, (IntPtr)0, hIcon); // ICON_BIG
                    SendMessageW(_hwnd, 0x0080, (IntPtr)1, hIcon); // ICON_SMALL
                }
            }

            ShowWindow(_hwnd, 5);
            SetTimer(_hwnd, (IntPtr)1, 16, IntPtr.Zero); // 16ms 約 60fps

            Thread bgThread = new Thread(() => RunProcessing(_hwnd));
            bgThread.IsBackground = true;
            bgThread.Start();

            int status;
            while ((status = GetMessage(out var msg, IntPtr.Zero, 0, 0)) != 0)
            {
                if (status == -1) break;
                if (_isPromptingPassword && IsDialogMessageW(_hwnd, ref msg))
                {
                    continue;
                }
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            Marshal.FreeHGlobal(hClass);
        }

        private void SetupTrayIcon(IntPtr hwnd)
        {
            if (_trayIconAdded) return;

            _nid = new NOTIFYICONDATAW();
            _nid.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>();
            _nid.hWnd = hwnd;
            _nid.uID = 2;
            _nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            _nid.uCallbackMessage = WM_TRAYICON;
            _nid.hIcon = _hIcon;

            int pct = 0;
            lock (_stateLock)
            {
                if (_total > 0)
                {
                    pct = _current * 100 / _total;
                }
            }
            _nid.szTip = $"Clickra - 正在轉換... {pct}%";

            Shell_NotifyIcon(NIM_ADD, ref _nid);
            _trayIconAdded = true;
        }

        private void UpdateTrayIconProgress()
        {
            if (!_trayIconAdded) return;

            int pct = 0;
            lock (_stateLock)
            {
                if (_total > 0)
                {
                    pct = _current * 100 / _total;
                }
            }
            _nid.szTip = $"Clickra - 正在轉換... {pct}%";
            _nid.uFlags = NIF_TIP;
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        }

        private void RemoveTrayIcon()
        {
            if (_trayIconAdded)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _nid);
                _trayIconAdded = false;
            }
        }

        private void CleanupResources()
        {
            if (_hwndEdit != IntPtr.Zero) { DestroyWindow(_hwndEdit); _hwndEdit = IntPtr.Zero; }
            if (_hwndBtnOk != IntPtr.Zero) { DestroyWindow(_hwndBtnOk); _hwndBtnOk = IntPtr.Zero; }
            if (_hwndBtnCancel != IntPtr.Zero) { DestroyWindow(_hwndBtnCancel); _hwndBtnCancel = IntPtr.Zero; }

            try { _titleFont?.Dispose(); _titleFont = null; } catch { }
            try { _subFont?.Dispose(); _subFont = null; } catch { }
            try { _headerFont?.Dispose(); _headerFont = null; } catch { }
            try { _msgFont?.Dispose(); _msgFont = null; } catch { }
            try { _tipFont?.Dispose(); _tipFont = null; } catch { }
            try { _pctFont?.Dispose(); _pctFont = null; } catch { }
            try { _linePen?.Dispose(); _linePen = null; } catch { }
            try { _borderPen?.Dispose(); _borderPen = null; } catch { }
            try { _bgBrush?.Dispose(); _bgBrush = null; } catch { }
            try { _bufferGraphics?.Dispose(); _bufferGraphics = null; } catch { }
            try { _bufferBmp?.Dispose(); _bufferBmp = null; } catch { }
            try { _cts?.Dispose(); } catch { }

            if (_darkBrush != IntPtr.Zero) { DeleteObject(_darkBrush); _darkBrush = IntPtr.Zero; }
            if (_editBgBrush != IntPtr.Zero) { DeleteObject(_editBgBrush); _editBgBrush = IntPtr.Zero; }
            if (_hFont != IntPtr.Zero) { DeleteObject(_hFont); _hFont = IntPtr.Zero; }

            RemoveTrayIcon();
            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
        }

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
                    case "merge-pdf":
                        FileProcessor.MergePdfs(currentFiles, Path.Combine(outputDir, "Merged_PDF.pdf"), progressCallback, _cts.Token);
                        break;
                    case "img2pdf":
                        for (int i = 0; i < currentFiles.Count; i++)
                        {
                            _cts.Token.ThrowIfCancellationRequested();
                            try { ClickraStorage.SetActiveRecordIndex(i); } catch { }
                            var f = currentFiles[i];
                            string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + ".pdf");
                            progressCallback((i * 100) + 50, currentFiles.Count * 100, $"正在轉換圖片: {Path.GetFileName(f)} ({i + 1}/{currentFiles.Count})...");
                            FileProcessor.ImagesToPdf(new List<string> { f }, outName, null, _cts.Token);
                        }
                        _cts.Token.ThrowIfCancellationRequested();
                        progressCallback(currentFiles.Count * 100, currentFiles.Count * 100, "轉換完成，正在儲存 PDF...");
                        break;
                    case "img-merge":
                        FileProcessor.ImagesToPdf(currentFiles, Path.Combine(outputDir, "Merged_Images.pdf"), progressCallback, _cts.Token);
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
                    MessageBox(hwnd, $"處理過程中發生錯誤：\n{ex.Message}", "Clickra — 錯誤", 0x10); // MB_ICONERROR
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
                case "img2pdf":
                    return string.Join(";", inputFiles.Select(f => Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + ".pdf")));
                case "translate-pdf":
                    return string.Join(";", inputFiles.Select(f => Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_translated.pdf")));
                case "decrypt-pdf":
                    return string.Join(";", inputFiles.Select(f => Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + "_decrypted.pdf")));
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

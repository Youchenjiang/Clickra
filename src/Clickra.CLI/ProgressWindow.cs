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

namespace Clickra.UI
{
    /// <summary>
    /// 提供 CLI 執行階段專專用之 Win32 進度視窗。
    /// </summary>
    public class ProgressWindow
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public IntPtr lpszMenuName;
            public IntPtr lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public Point pt; }

        [StructLayout(LayoutKind.Sequential)]
        struct PAINTSTRUCT
        {
            public IntPtr hdc; public bool fErase;
            public int rcLeft, rcTop, rcRight, rcBottom;
            public bool fRestore, fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int left, top, right, bottom; }

        [DllImport("user32.dll", EntryPoint = "AdjustWindowRectEx", CharSet = CharSet.Unicode)]
        static extern bool AdjustWindowRectEx(ref RECT lpRect, uint dwStyle, bool bMenu, uint dwExStyle);

        [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)] 
        static extern ushort RegisterClassEx(ref WNDCLASSEX c);
        
        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)] 
        static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr p, IntPtr m, IntPtr inst, IntPtr par);
        
        [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)] 
        static extern bool SetWindowText(IntPtr h, string text);

        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
        [DllImport("user32.dll")] static extern bool GetMessage(out MSG m, IntPtr h, uint f, uint l);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
        [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT p);
        [DllImport("user32.dll")] static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT p);
        [DllImport("user32.dll")] static extern void PostQuitMessage(int c);
        [DllImport("user32.dll")] static extern IntPtr LoadCursorW(IntPtr h, int n);
        [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
        [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
        [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);
        [DllImport("user32.dll")] static extern bool KillTimer(IntPtr hWnd, IntPtr nIDEvent);

        [DllImport("shell32.dll", EntryPoint = "ExtractIconW", CharSet = CharSet.Unicode)] 
        static extern IntPtr ExtractIcon(IntPtr h, string path, int idx);
        
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("dwmapi.dll", PreserveSig = false)] static extern void DwmGetColorizationColor(out uint pcrColorization, out bool pfOpaqueBlend);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", CharSet = CharSet.Unicode)]
        static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", CharSet = CharSet.Unicode)]
        static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        const uint WS_OVERLAPPED_FIXED = 0x00CF0000 & ~0x00040000u & ~0x00020000u;
        const int DWMWA_DARK_MODE = 20;
        const int CW_USEDEFAULT = unchecked((int)0x80000000);

        delegate IntPtr WndProcDelegate(IntPtr h, uint msg, IntPtr w, IntPtr l);
        static readonly WndProcDelegate _wndProcDelegate = WndProc;

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
            }

            // 初始化 GDI+ 快取物件
            _titleFont ??= new Font("Segoe UI Variable Display", 24, FontStyle.Bold);
            _subFont ??= new Font("Segoe UI Variable Display", 11);
            _headerFont ??= new Font("Segoe UI Variable Display", 16, FontStyle.Bold);
            _msgFont ??= new Font("Segoe UI Variable Display", 11);
            _tipFont ??= new Font("Segoe UI Variable Display", 9);
            _pctFont ??= new Font("Segoe UI Variable Display", 10, FontStyle.Bold);
            _linePen ??= new Pen(Color.FromArgb(60, 60, 60));
            _borderPen ??= new Pen(Color.FromArgb(70, 70, 70));
            _bgBrush ??= new SolidBrush(Color.FromArgb(45, 45, 45));

            if (_bufferBmp == null)
            {
                _bufferBmp = new Bitmap(520, 280);
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
                hInstance = Marshal.GetHINSTANCE(typeof(ProgressWindow).Module),
                hCursor = LoadCursorW(IntPtr.Zero, 32512),
                hbrBackground = IntPtr.Zero,
                lpszClassName = hClass
            };

            RegisterClassEx(ref wc);

            var rect = new RECT { left = 0, top = 0, right = 520, bottom = 280 };
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
                    SendMessageW(_hwnd, 0x0080, (IntPtr)0, hIcon); // ICON_BIG
                    SendMessageW(_hwnd, 0x0080, (IntPtr)1, hIcon); // ICON_SMALL
                }
            }

            ShowWindow(_hwnd, 5);
            SetTimer(_hwnd, (IntPtr)1, 16, IntPtr.Zero); // 16ms 約 60fps

            Thread bgThread = new Thread(() => RunProcessing(_hwnd));
            bgThread.IsBackground = true;
            bgThread.Start();

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                DispatchMessage(ref msg);
            }

            Marshal.FreeHGlobal(hClass);
        }

        private void CleanupResources()
        {
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
        }

        static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            ProgressWindow? window = null;
            if (msg == 0x0081) // WM_NCCREATE
            {
                IntPtr lpCreateParams = Marshal.ReadIntPtr(l);
                SetWindowLongPtr(hwnd, -21, lpCreateParams); // GWLP_USERDATA = -21
                if (lpCreateParams != IntPtr.Zero)
                {
                    GCHandle gcHandle = GCHandle.FromIntPtr(lpCreateParams);
                    window = gcHandle.Target as ProgressWindow;
                    if (window != null) window._hwnd = hwnd;
                }
            }
            else
            {
                IntPtr userData = GetWindowLongPtr(hwnd, -21);
                if (userData != IntPtr.Zero)
                {
                    GCHandle gcHandle = GCHandle.FromIntPtr(userData);
                    if (gcHandle.IsAllocated)
                    {
                        window = gcHandle.Target as ProgressWindow;
                    }
                }
            }

            IntPtr result = IntPtr.Zero;
            if (window != null)
            {
                result = window.InstanceWndProc(hwnd, msg, w, l);
            }
            else
            {
                result = DefWindowProcW(hwnd, msg, w, l);
            }

            if (msg == 0x0082) // WM_NCDESTROY
            {
                IntPtr userData = GetWindowLongPtr(hwnd, -21);
                if (userData != IntPtr.Zero)
                {
                    GCHandle gcHandle = GCHandle.FromIntPtr(userData);
                    if (gcHandle.IsAllocated)
                    {
                        gcHandle.Free();
                    }
                    SetWindowLongPtr(hwnd, -21, IntPtr.Zero);
                }
            }

            return result;
        }

        private IntPtr InstanceWndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            switch (msg)
            {
                case 0x0014: return (IntPtr)1; // WM_ERASEBKGND
                case 0x0113: // WM_TIMER
                    lock (_stateLock)
                    {
                        if (!_completed && !_hasError)
                        {
                            if (_currentDispWidth < _targetWidth)
                            {
                                double diff = _targetWidth - _currentDispWidth;
                                double step = diff * 0.15;
                                if (step < 1.0) step = 1.0;
                                
                                _currentDispWidth += step;
                                if (_currentDispWidth >= _targetWidth) _currentDispWidth = _targetWidth;
                            }
                            
                            _shimmerOffset += 5.0f;
                            if (_shimmerOffset > 448) _shimmerOffset = -120;
                            
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                    }
                    return IntPtr.Zero;
                case 0x000F: // WM_PAINT
                    var ps = new PAINTSTRUCT();
                    var hdc = BeginPaint(hwnd, out ps);
                    Paint(hdc);
                    EndPaint(hwnd, ref ps);
                    return IntPtr.Zero;
                case 0x0002: // WM_DESTROY
                    KillTimer(hwnd, (IntPtr)1);
                    CleanupResources();
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, w, l);
        }

        private void RunProcessing(IntPtr hwnd)
        {
            List<string> currentFiles = new List<string>();
            string cmd = "";
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
                    InvalidateRect(hwnd, IntPtr.Zero, true);
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
                };

                // 立即建立 Pending 紀錄，讓 Dashboard 可即時看到
                try { ClickraStorage.StartActiveRecord(cmd, currentFiles.Count); } catch { }

                string outputDir = ClickraStorage.GetOutputDir(currentFiles[0]);

                // 開始實際處理，切換為 InProgress
                try { ClickraStorage.SetActiveRecordInProgress(); } catch { }

                switch (cmd)
                {
                    case "ppt2pdf":
                        FileProcessor.ConvertPptToPdf(currentFiles, progressCallback);
                        break;
                    case "word2pdf":
                        FileProcessor.ConvertWordToPdf(currentFiles, progressCallback);
                        break;
                    case "merge-pdf":
                        FileProcessor.MergePdfs(currentFiles, Path.Combine(outputDir, "Merged_PDF.pdf"), progressCallback);
                        break;
                    case "img2pdf":
                        for (int i = 0; i < currentFiles.Count; i++)
                        {
                            var f = currentFiles[i];
                            string outName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(f) + ".pdf");
                            progressCallback((i * 100) + 50, currentFiles.Count * 100, $"正在轉換圖片: {Path.GetFileName(f)} ({i + 1}/{currentFiles.Count})...");
                            FileProcessor.ImagesToPdf(new List<string> { f }, outName, null);
                        }
                        progressCallback(currentFiles.Count * 100, currentFiles.Count * 100, "轉換完成，正在儲存 PDF...");
                        break;
                    case "img-merge":
                        FileProcessor.ImagesToPdf(currentFiles, Path.Combine(outputDir, "Merged_Images.pdf"), progressCallback);
                        break;
                    case "img-stitch":
                        FileProcessor.StitchImages(currentFiles, Path.Combine(outputDir, "Stitched_Image.png"), progressCallback);
                        break;
                }

                lock (_stateLock)
                {
                    _completed = true;
                    _message = "所有作業已順利完成！";
                }
                InvalidateRect(hwnd, IntPtr.Zero, true);

                // 完成：寫入持久化日誌並暫留 Success 狀態供 Dashboard 讀取
                try { ClickraStorage.CompleteActiveRecord(true, ""); } catch { }

                ShowToastNotification(cmd, currentFiles.Count);

                Thread.Sleep(1500);
                try { ClickraStorage.ClearActiveRecord(); } catch { }
                PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            }
            catch (Exception ex)
            {
                lock (_stateLock)
                {
                    _hasError = true;
                    _errorMessage = ex.Message;
                }
                InvalidateRect(hwnd, IntPtr.Zero, true);

                // 失敗：立即寫入持久化日誌並暫留 Failed 狀態供 Dashboard 讀取
                try { ClickraStorage.CompleteActiveRecord(false, ex.Message); } catch { }

                MessageBox(hwnd, $"處理過程中發生錯誤：\n{ex.Message}", "Clickra — 錯誤", 0x10); // MB_ICONERROR
                try { ClickraStorage.ClearActiveRecord(); } catch { }
                PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
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

        private Color GetSystemColorizationColor()
        {
            if (_hasCachedColorizationColor) return _cachedColorizationColor;
            try
            {
                DwmGetColorizationColor(out uint color, out bool _);
                _cachedColorizationColor = Color.FromArgb(255, Color.FromArgb((int)color));
                _hasCachedColorizationColor = true;
                return _cachedColorizationColor;
            }
            catch
            {
                _cachedColorizationColor = Color.FromArgb(255, 0, 120, 212); // 微軟藍
                _hasCachedColorizationColor = true;
                return _cachedColorizationColor;
            }
        }

        private Color Lighten(Color c, float amount)
        {
            int r = (int)(c.R + (255 - c.R) * amount);
            int g = (int)(c.G + (255 - c.G) * amount);
            int b = (int)(c.B + (255 - c.B) * amount);
            return Color.FromArgb(255, Math.Min(255, r), Math.Min(255, g), Math.Min(255, b));
        }

        private GraphicsPath GetRoundedRectPath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void Paint(IntPtr hdc)
        {
            if (_bufferBmp == null || _bufferGraphics == null) return;
            var g = _bufferGraphics;
            g.Clear(Color.FromArgb(32, 32, 32));

            bool hasErr, comp; string msg, errMsg, pctStr;
            double dispW; float shimOff; int tot, cur;

            lock (_stateLock)
            {
                hasErr = _hasError; comp = _completed;
                msg = _message; errMsg = _errorMessage;
                dispW = _currentDispWidth; shimOff = _shimmerOffset;
                tot = _total; cur = _current;
            }

            if (_titleFont != null)
                g.DrawString("Clickra", _titleFont, Brushes.White, 36, 28);

            if (_subFont != null)
            {
                string subText = hasErr ? "作業失敗" : (comp ? "作業完成" : "正在執行作業...");
                Color subColor = hasErr ? Color.FromArgb(255, 90, 70) : (comp ? Color.FromArgb(100, 220, 100) : Color.FromArgb(160, 160, 160));
                using var subBrush = new SolidBrush(subColor);
                g.DrawString(subText, _subFont, subBrush, 36, 72);
            }

            if (_linePen != null)
                g.DrawLine(_linePen, 36, 110, 484, 110);

            if (hasErr)
            {
                if (_headerFont != null)
                {
                    using var errBrush = new SolidBrush(Color.FromArgb(255, 90, 70));
                    g.DrawString("❌ 處理失敗", _headerFont, errBrush, 36, 130);
                }
                if (_msgFont != null)
                {
                    using var errMsgBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                    g.DrawString(errMsg, _msgFont, errMsgBrush, new RectangleF(36, 170, 448, 60));
                }
            }
            else if (comp)
            {
                if (_headerFont != null)
                {
                    using var succBrush = new SolidBrush(Color.FromArgb(100, 220, 100));
                    g.DrawString("✔ 轉換成功！", _headerFont, succBrush, 36, 130);
                }
                if (_msgFont != null)
                {
                    using var msgBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
                    g.DrawString(msg, _msgFont, msgBrush, 36, 170);
                }
                if (_tipFont != null)
                {
                    using var tipBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
                    g.DrawString("視窗將於數秒後自動關閉...", _tipFont, tipBrush, 36, 220);
                }
            }
            else
            {
                if (_msgFont != null)
                    g.DrawString(msg, _msgFont, Brushes.White, 36, 130);

                int barX = 36, barY = 170, barW = 448, barH = 16;
                using var bgPath = GetRoundedRectPath(new RectangleF(barX, barY, barW, barH), 6);
                if (_bgBrush != null) g.FillPath(_bgBrush, bgPath);
                if (_borderPen != null) g.DrawPath(_borderPen, bgPath);

                if (dispW > 3)
                {
                    var fillRect = new RectangleF(barX, barY, (float)dispW, barH);
                    using var fillPath = GetRoundedRectPath(fillRect, 6);
                    
                    Color accent = GetSystemColorizationColor();
                    Color accentLight = Lighten(accent, 0.3f);
                    using var gradBrush = new LinearGradientBrush(fillRect, accent, accentLight, LinearGradientMode.Horizontal);
                    g.FillPath(gradBrush, fillPath);

                    var oldClip = g.Clip;
                    g.SetClip(fillPath);

                    var shimmerRect = new RectangleF(shimOff, barY, 120, barH);
                    using var shimmerBrush = new LinearGradientBrush(shimmerRect, Color.FromArgb(0, 255, 255, 255), Color.FromArgb(100, 255, 255, 255), LinearGradientMode.Horizontal);
                    var blend = new ColorBlend(3);
                    blend.Colors = new Color[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(100, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) };
                    blend.Positions = new float[] { 0.0f, 0.5f, 1.0f };
                    shimmerBrush.InterpolationColors = blend;

                    g.FillRectangle(shimmerBrush, shimmerRect);
                    g.Clip = oldClip;
                }

                pctStr = tot > 0 ? $"{(cur * 100 / tot)}%" : "";
                if (_pctFont != null)
                {
                    var size = g.MeasureString(pctStr, _pctFont);
                    using var pctBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
                    g.DrawString(pctStr, _pctFont, pctBrush, 484 - size.Width, 145);
                }

                if (_tipFont != null)
                {
                    using var tipBrush = new SolidBrush(Color.FromArgb(100, 100, 100));
                    g.DrawString("請稍候，正在背景高速處理中...", _tipFont, tipBrush, 36, 220);
                }
            }

            using var targetG = Graphics.FromHdc(hdc);
            targetG.DrawImage(_bufferBmp, 0, 0);
        }
    }
}

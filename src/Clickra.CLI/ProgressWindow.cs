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

        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")] static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        const uint WS_OVERLAPPED_FIXED = 0x00CF0000 & ~0x00040000u & ~0x00010000u;
        const int DWMWA_DARK_MODE = 20;
        const int CW_USEDEFAULT = unchecked((int)0x80000000);

        delegate IntPtr WndProcDelegate(IntPtr h, uint msg, IntPtr w, IntPtr l);
        static readonly WndProcDelegate _wndProcDelegate = WndProc;

        private CancellationTokenSource _cts = new CancellationTokenSource();
        private NOTIFYICONDATAW _nid;
        private bool _trayIconAdded = false;
        private bool _isTrayBtnHovered = false;
        private IntPtr _hIcon = IntPtr.Zero;

        private const uint WM_TRAYICON = 0x0400 + 1;
        private const uint WM_USER_INVALIDATE = 0x0400 + 2;
        private const uint NIM_ADD = 0;
        private const uint NIM_MODIFY = 1;
        private const uint NIM_DELETE = 2;
        private const uint NIF_MESSAGE = 1;
        private const uint NIF_ICON = 2;
        private const uint NIF_TIP = 4;
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;

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

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
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

            RemoveTrayIcon();
            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
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
                case 0x020A: // WM_MOUSEWHEEL
                    {
                        int delta = (short)((w.ToInt64() >> 16) & 0xFFFF);
                        int scrollDir = delta > 0 ? -1 : 1;
                        lock (_stateLock)
                        {
                            if (!_completed && !_hasError)
                            {
                                _scrollOffset += scrollDir * 30;
                                if (_scrollOffset < 0) _scrollOffset = 0;
                            }
                        }
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;
                case WM_USER_INVALIDATE:
                    InvalidateRect(hwnd, IntPtr.Zero, w != IntPtr.Zero);
                    return IntPtr.Zero;
                case 0x0200: // WM_MOUSEMOVE
                    {
                        int rawX = (short)(l.ToInt64() & 0xFFFF);
                        int rawY = (short)((l.ToInt64() >> 16) & 0xFFFF);
                        int mouseX = (int)(rawX / _dpiScale);
                        int mouseY = (int)(rawY / _dpiScale);

                        lock (_stateLock)
                        {
                            if (!_completed && !_hasError)
                            {
                                if (_isDraggingScroll)
                                {
                                    string statusMsg = _message;
                                    float logicalPctW = 0;
                                    string drawPctStr = _total > 0 ? $"{(_current * 100 / _total)}%" : "";
                                    if (_pctFont != null && _total > 0)
                                    {
                                        using var tempBmp = new Bitmap(1, 1);
                                        using var tempG = Graphics.FromImage(tempBmp);
                                        logicalPctW = tempG.MeasureString(drawPctStr, _pctFont).Width / _dpiScale;
                                    }
                                    float logicalMaxMsgW = 448f;
                                    if (logicalPctW > 0)
                                    {
                                        logicalMaxMsgW = 448f - logicalPctW - 10f;
                                    }

                                    float fullMsgW = 0f;
                                    using (var tempBmp = new Bitmap(1, 1))
                                    using (var tempG = Graphics.FromImage(tempBmp))
                                    {
                                        if (_msgFont != null)
                                        {
                                            fullMsgW = tempG.MeasureString(statusMsg, _msgFont).Width / _dpiScale;
                                        }
                                    }

                                    float maxLogicalScroll = Math.Max(0f, fullMsgW - logicalMaxMsgW);
                                    if (maxLogicalScroll > 0)
                                    {
                                        float thumbW = Math.Max(15f, (logicalMaxMsgW / fullMsgW) * logicalMaxMsgW);
                                        float travelRange = logicalMaxMsgW - thumbW;
                                        if (travelRange > 0)
                                        {
                                            float deltaX = mouseX - _dragStartMouseX;
                                            float deltaOffset = (deltaX / travelRange) * maxLogicalScroll;
                                            _scrollOffset = Math.Max(0f, Math.Min(_dragStartOffset + deltaOffset, maxLogicalScroll));
                                            InvalidateRect(hwnd, IntPtr.Zero, false);
                                        }
                                    }
                                }
                                else
                                {
                                    bool hovered = (mouseX >= 456 && mouseX <= 484 && mouseY >= 36 && mouseY <= 64);
                                    if (hovered != _isTrayBtnHovered)
                                    {
                                        _isTrayBtnHovered = hovered;
                                        InvalidateRect(hwnd, IntPtr.Zero, false);
                                    }
                                }
                            }
                        }
                    }
                    return IntPtr.Zero;
                case 0x0201: // WM_LBUTTONDOWN
                    {
                        int rawX = (short)(l.ToInt64() & 0xFFFF);
                        int rawY = (short)((l.ToInt64() >> 16) & 0xFFFF);
                        int mouseX = (int)(rawX / _dpiScale);
                        int mouseY = (int)(rawY / _dpiScale);

                        lock (_stateLock)
                        {
                            if (!_completed && !_hasError)
                            {
                                if (mouseX >= 456 && mouseX <= 484 && mouseY >= 36 && mouseY <= 64)
                                {
                                    SetupTrayIcon(hwnd);
                                    ShowWindow(hwnd, 0); // SW_HIDE
                                    _isTrayBtnHovered = false;
                                    return IntPtr.Zero;
                                }

                                float logicalPctW = 0;
                                string drawPctStr = _total > 0 ? $"{(_current * 100 / _total)}%" : "";
                                if (_pctFont != null && _total > 0)
                                {
                                    using var tempBmp = new Bitmap(1, 1);
                                    using var tempG = Graphics.FromImage(tempBmp);
                                    logicalPctW = tempG.MeasureString(drawPctStr, _pctFont).Width / _dpiScale;
                                }
                                float logicalMaxMsgW = 448f;
                                if (logicalPctW > 0)
                                {
                                    logicalMaxMsgW = 448f - logicalPctW - 10f;
                                }

                                if (mouseX >= 36 && mouseX <= 36 + logicalMaxMsgW && mouseY >= 148 && mouseY <= 158)
                                {
                                    string statusMsg = _message;
                                    float fullMsgW = 0f;
                                    using (var tempBmp = new Bitmap(1, 1))
                                    using (var tempG = Graphics.FromImage(tempBmp))
                                    {
                                        if (_msgFont != null)
                                        {
                                            fullMsgW = tempG.MeasureString(statusMsg, _msgFont).Width / _dpiScale;
                                        }
                                    }

                                    float maxLogicalScroll = Math.Max(0f, fullMsgW - logicalMaxMsgW);
                                    if (maxLogicalScroll > 0)
                                    {
                                        float clickX = mouseX - 36f;
                                        float thumbW = Math.Max(15f, (logicalMaxMsgW / fullMsgW) * logicalMaxMsgW);
                                        float thumbX = (_scrollOffset / fullMsgW) * logicalMaxMsgW;
                                        if (thumbX + thumbW > logicalMaxMsgW) thumbX = logicalMaxMsgW - thumbW;

                                        float travelRange = logicalMaxMsgW - thumbW;
                                        float relativePos = travelRange > 0 ? (clickX - thumbW / 2f) / travelRange : 0f;
                                        float newOffset = Math.Max(0f, Math.Min(relativePos * maxLogicalScroll, maxLogicalScroll));

                                        _scrollOffset = newOffset;
                                        _isDraggingScroll = true;
                                        _dragStartMouseX = mouseX;
                                        _dragStartOffset = newOffset;
                                        SetCapture(hwnd);
                                        InvalidateRect(hwnd, IntPtr.Zero, false);
                                    }
                                }
                            }
                        }
                    }
                    return IntPtr.Zero;
                case 0x0202: // WM_LBUTTONUP
                    {
                        lock (_stateLock)
                        {
                            if (_isDraggingScroll)
                            {
                                _isDraggingScroll = false;
                                ReleaseCapture();
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                        }
                    }
                    return IntPtr.Zero;
                case WM_TRAYICON:
                    if (l.ToInt64() == 0x0203) // WM_LBUTTONDBLCLK
                    {
                        ShowWindow(hwnd, 5); // SW_SHOW
                        ShowWindow(hwnd, 9); // SW_RESTORE
                        SetForegroundWindow(hwnd);
                        RemoveTrayIcon();
                    }
                    return IntPtr.Zero;
                case 0x0010: // WM_CLOSE
                    {
                        bool needsConfirmation = false;
                        lock (_stateLock)
                        {
                            if (!_completed && !_hasError)
                            {
                                needsConfirmation = true;
                            }
                        }

                        if (needsConfirmation)
                        {
                            string text = Localization.T("progress_cancel_confirm", ClickraStorage.GetSetting("Language"));
                            string caption = "Clickra";
                            int btn = MessageBox(hwnd, text, caption, 0x24 | 0x30); // MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2
                            if (btn != 6) // 6 is IDYES
                            {
                                return IntPtr.Zero; // Ignore close
                            }

                            // User confirmed cancellation
                            try { _cts.Cancel(); } catch { }
                        }

                        DestroyWindow(hwnd);
                    }
                    return IntPtr.Zero;
                case 0x02E0: // WM_DPICHANGED
                    {
                        uint newDpi = (uint)(w.ToInt64() & 0xFFFF);
                        _dpiScale = newDpi / 96.0f;
                        RecreateScaledFonts();
                        
                        int clientW = (int)(520 * _dpiScale);
                        int clientH = (int)(280 * _dpiScale);
                        
                        if (_bufferBmp != null)
                        {
                            _bufferGraphics?.Dispose();
                            _bufferBmp?.Dispose();
                            _bufferBmp = new Bitmap(clientW, clientH);
                            _bufferGraphics = Graphics.FromImage(_bufferBmp);
                            _bufferGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                            _bufferGraphics.SmoothingMode = SmoothingMode.AntiAlias;
                        }

                        var rect = Marshal.PtrToStructure<RECT>(l);
                        SetWindowPos(hwnd, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, 0x0010 | 0x0004);
                    }
                    return IntPtr.Zero;
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
                };

                // 立即建立 Pending 紀錄，讓 Dashboard 可即時看到
                try { ClickraStorage.StartActiveRecord(cmd, currentFiles.Count); } catch { }

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
                InvalidateRect(hwnd, IntPtr.Zero, true);

                // 完成：寫入持久化日誌並暫留 Success 狀態供 Dashboard 讀取
                try { ClickraStorage.CompleteActiveRecord(true, "", endTime, elapsedMs, inputs, outputs); } catch { }

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

                bool wasCanceled = _cts.IsCancellationRequested;
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

            float s = _dpiScale;

            if (_titleFont != null)
                g.DrawString("Clickra", _titleFont, Brushes.White, 36 * s, 28 * s);

            if (_subFont != null)
            {
                string subText = hasErr ? "作業失敗" : (comp ? "作業完成" : "正在執行作業...");
                Color subColor = hasErr ? Color.FromArgb(255, 90, 70) : (comp ? Color.FromArgb(100, 220, 100) : Color.FromArgb(160, 160, 160));
                using var subBrush = new SolidBrush(subColor);
                g.DrawString(subText, _subFont, subBrush, 36 * s, 72 * s);
            }

            if (_linePen != null)
                g.DrawLine(_linePen, 36 * s, 110 * s, 484 * s, 110 * s);

            if (hasErr)
            {
                if (_headerFont != null)
                {
                    using var errBrush = new SolidBrush(Color.FromArgb(255, 90, 70));
                    g.DrawString("❌ 處理失敗", _headerFont, errBrush, 36 * s, 130 * s);
                }
                if (_msgFont != null)
                {
                    using var errMsgBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                    string displayErrMsg = errMsg;
                    if (displayErrMsg.Equals("User Aborted", StringComparison.OrdinalIgnoreCase))
                    {
                        displayErrMsg = Localization.T("error_user_aborted", ClickraStorage.GetSetting("Language"));
                    }
                    g.DrawString(displayErrMsg, _msgFont, errMsgBrush, new RectangleF(36 * s, 170 * s, 448 * s, 60 * s));
                }
            }
            else if (comp)
            {
                if (_headerFont != null)
                {
                    using var succBrush = new SolidBrush(Color.FromArgb(100, 220, 100));
                    g.DrawString("✔ 轉換成功！", _headerFont, succBrush, 36 * s, 130 * s);
                }
                if (_msgFont != null)
                {
                    using var msgBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
                    g.DrawString(msg, _msgFont, msgBrush, 36 * s, 170 * s);
                }
                if (_tipFont != null)
                {
                    using var tipBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
                    g.DrawString("視窗將於數秒後自動關閉...", _tipFont, tipBrush, 36 * s, 220 * s);
                }
            }
            else
            {
                if (_msgFont != null)
                {
                    string drawPctStr = tot > 0 ? $"{(cur * 100 / tot)}%" : "";
                    float logicalPctW = 0;
                    if (_pctFont != null && tot > 0)
                    {
                        logicalPctW = g.MeasureString(drawPctStr, _pctFont).Width / s;
                    }
                    float logicalMaxMsgW = 448f;
                    if (logicalPctW > 0)
                    {
                        logicalMaxMsgW = 448f - logicalPctW - 10f;
                    }

                    float fullMsgW = g.MeasureString(msg, _msgFont).Width / s;
                    float maxLogicalScroll = Math.Max(0f, fullMsgW - logicalMaxMsgW);

                    if (maxLogicalScroll > 0)
                    {
                        float currentScroll = 0f;
                        lock (_stateLock)
                        {
                            if (_scrollOffset > maxLogicalScroll) _scrollOffset = maxLogicalScroll;
                            currentScroll = _scrollOffset;
                        }

                        var oldClip = g.Clip;
                        g.SetClip(new RectangleF(36 * s, 120 * s, logicalMaxMsgW * s, 30 * s));
                        g.DrawString(msg, _msgFont, Brushes.White, 36 * s - currentScroll * s, 130 * s);
                        g.Clip = oldClip;

                        // Draw scrollbar if scrollable
                        float scrollbarY = 152;
                        float thumbW = Math.Max(15f, (logicalMaxMsgW / fullMsgW) * logicalMaxMsgW);
                        float thumbX = 36f + (currentScroll / fullMsgW) * logicalMaxMsgW;
                        if (thumbX + thumbW > 36f + logicalMaxMsgW) thumbX = 36f + logicalMaxMsgW - thumbW;

                        using (var trackBrush = new SolidBrush(Color.FromArgb(15, 255, 255, 255)))
                        {
                            g.FillRectangle(trackBrush, 36 * s, scrollbarY * s, logicalMaxMsgW * s, 2 * s);
                        }
                        using (var thumbBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                        {
                            g.FillRectangle(thumbBrush, thumbX * s, scrollbarY * s, thumbW * s, 2 * s);
                        }
                    }
                    else
                    {
                        lock (_stateLock)
                        {
                            _scrollOffset = 0f;
                 _isDraggingScroll = false;
                 _dragStartMouseX = 0f;
                 _dragStartOffset = 0f;
                        }
                        g.DrawString(msg, _msgFont, Brushes.White, 36 * s, 130 * s);
                    }
                }

                float barX = 36 * s, barY = 170 * s, barW = 448 * s, barH = 16 * s;
                using var bgPath = GetRoundedRectPath(new RectangleF(barX, barY, barW, barH), 6 * s);
                if (_bgBrush != null) g.FillPath(_bgBrush, bgPath);
                if (_borderPen != null) g.DrawPath(_borderPen, bgPath);

                if (dispW > 3)
                {
                    var fillRect = new RectangleF(barX, barY, (float)(dispW * s), barH);
                    using var fillPath = GetRoundedRectPath(fillRect, 6 * s);
                    
                    Color accent = GetSystemColorizationColor();
                    Color accentLight = Lighten(accent, 0.3f);
                    using var gradBrush = new LinearGradientBrush(fillRect, accent, accentLight, LinearGradientMode.Horizontal);
                    g.FillPath(gradBrush, fillPath);

                    var oldClip = g.Clip;
                    g.SetClip(fillPath);

                    var shimmerRect = new RectangleF(shimOff * s, barY, 120 * s, barH);
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

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
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint f, uint l);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll", EntryPoint = "IsDialogMessageW")] static extern bool IsDialogMessageW(IntPtr hDlg, ref MSG lpMsg);
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
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll", EntryPoint = "DestroyWindow")] static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr SetCapture(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATAW
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
        static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATAW lpData);

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

        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hWnd);
        [DllImport("user32.dll", EntryPoint = "CallWindowProcW", CharSet = CharSet.Unicode)]
        static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetProp(IntPtr hWnd, string lpString);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr hWnd, string lpString, IntPtr hData);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr hWnd, string lpString);
        [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint crColor);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll", EntryPoint = "SetTextColor")] static extern uint SetTextColor(IntPtr hdc, uint crColor);
        [DllImport("gdi32.dll", EntryPoint = "SetBkColor")] static extern uint SetBkColor(IntPtr hdc, uint crColor);
        [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);
        [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontW(int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight, uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet, uint iOutPrecision, uint iClipPrecision, uint iQuality, uint iPitchAndFamily, string pszFaceName);

        const uint WS_OVERLAPPED_FIXED = (0x00CF0000 | 0x02000000) & ~0x00040000u & ~0x00010000u;
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

        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
        private static unsafe IntPtr EditSubclassProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            IntPtr oldProc = GetProp(hwnd, "ClickraOldWndProc");
            if (msg == 0x0100) // WM_KEYDOWN
            {
                int key = w.ToInt32();
                if (key == 0x0D) // VK_RETURN
                {
                    IntPtr parent = GetParent(hwnd);
                    PostMessageW(parent, 0x0111, (IntPtr)1001, IntPtr.Zero); // WM_COMMAND, ID = 1001 (OK)
                    return IntPtr.Zero;
                }
                if (key == 0x1B) // VK_ESCAPE
                {
                    IntPtr parent = GetParent(hwnd);
                    PostMessageW(parent, 0x0111, (IntPtr)1002, IntPtr.Zero); // WM_COMMAND, ID = 1002 (Cancel)
                    return IntPtr.Zero;
                }
            }
            if (msg == 0x0002) // WM_DESTROY
            {
                RemoveProp(hwnd, "ClickraOldWndProc");
            }
            return oldProc != IntPtr.Zero ? CallWindowProc(oldProc, hwnd, msg, w, l) : DefWindowProcW(hwnd, msg, w, l);
        }

        private unsafe IntPtr InstanceWndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            switch (msg)
            {
                case WM_USER_SHOW_PASSWORD_INPUT:
                    {
                        if (_hwndEdit != IntPtr.Zero) return IntPtr.Zero;

                        float scale = _dpiScale;
                        string lang = ClickraStorage.GetSetting("Language");
                        string normLang = Localization.NormalizeLanguageCode(lang);
                        string fontName = "Segoe UI";
                        if (normLang.StartsWith("zh-TW")) fontName = "Microsoft JhengHei UI";
                        else if (normLang.StartsWith("zh-CN")) fontName = "Microsoft YaHei UI";
                        else if (normLang.StartsWith("ja")) fontName = "Yu Gothic UI";
                        else if (normLang.StartsWith("ko")) fontName = "Malgun Gothic";

                        if (_hFont == IntPtr.Zero)
                        {
                            _hFont = CreateFontW((int)(14.5 * scale), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, fontName);
                        }

                        IntPtr hInstance = GetModuleHandle(null);
                        _hwndEdit = CreateWindowEx(0, "EDIT", "", WS_CHILD | WS_VISIBLE | WS_BORDER | WS_TABSTOP | 0x0020 | 0x0080, (int)(36 * scale), (int)(165 * scale), (int)(448 * scale), (int)(28 * scale), hwnd, (IntPtr)101, hInstance, IntPtr.Zero);
                        _hwndBtnOk = CreateWindowEx(0, "BUTTON", Localization.T("dialog_ok", lang), WS_CHILD | WS_VISIBLE | WS_TABSTOP | 0x00000001, (int)(280 * scale), (int)(210 * scale), (int)(90 * scale), (int)(30 * scale), hwnd, (IntPtr)1001, hInstance, IntPtr.Zero);
                        _hwndBtnCancel = CreateWindowEx(0, "BUTTON", Localization.T("dialog_cancel", lang), WS_CHILD | WS_VISIBLE | WS_TABSTOP, (int)(394 * scale), (int)(210 * scale), (int)(90 * scale), (int)(30 * scale), hwnd, (IntPtr)1002, hInstance, IntPtr.Zero);

                        SendMessageW(_hwndEdit, 0x0030, _hFont, (IntPtr)1); // WM_SETFONT = 0x0030
                        SendMessageW(_hwndBtnOk, 0x0030, _hFont, (IntPtr)1);
                        SendMessageW(_hwndBtnCancel, 0x0030, _hFont, (IntPtr)1);

                        // Subclass EDIT control for Enter/Esc VKs
                        IntPtr originalEditProc = GetWindowLongPtr(_hwndEdit, -4); // GWL_WNDPROC = -4
                        SetProp(_hwndEdit, "ClickraOldWndProc", originalEditProc);
                        SetWindowLongPtr(_hwndEdit, -4, (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&EditSubclassProc);

                        SetFocus(_hwndEdit);
                        InvalidateRect(hwnd, IntPtr.Zero, true);
                        InvalidateRect(_hwndEdit, IntPtr.Zero, true);
                        InvalidateRect(_hwndBtnOk, IntPtr.Zero, true);
                        InvalidateRect(_hwndBtnCancel, IntPtr.Zero, true);
                    }
                    return IntPtr.Zero;

                case WM_USER_HIDE_PASSWORD_INPUT:
                    {
                        if (_hwndEdit != IntPtr.Zero)
                        {
                            IntPtr oldProc = GetProp(_hwndEdit, "ClickraOldWndProc");
                            if (oldProc != IntPtr.Zero)
                            {
                                SetWindowLongPtr(_hwndEdit, -4, oldProc);
                                RemoveProp(_hwndEdit, "ClickraOldWndProc");
                            }
                            DestroyWindow(_hwndEdit);
                            _hwndEdit = IntPtr.Zero;
                        }
                        if (_hwndBtnOk != IntPtr.Zero)
                        {
                            DestroyWindow(_hwndBtnOk);
                            _hwndBtnOk = IntPtr.Zero;
                        }
                        if (_hwndBtnCancel != IntPtr.Zero)
                        {
                            DestroyWindow(_hwndBtnCancel);
                            _hwndBtnCancel = IntPtr.Zero;
                        }
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;

                case 0x0133: // WM_CTLCOLOREDIT
                    {
                        IntPtr editHdc = w;
                        SetTextColor(editHdc, 0x00FFFFFF); // White
                        SetBkColor(editHdc, 0x002D2D2D); // Edit bg (45, 45, 45)
                        return _editBgBrush;
                    }

                case 0x0111: // WM_COMMAND
                    {
                        int id = (int)w.ToInt64() & 0xFFFF;
                        if (id == 1001) // OK button
                        {
                            string? pwd = null;
                            if (_hwndEdit != IntPtr.Zero)
                            {
                                var sb = new System.Text.StringBuilder(260);
                                GetWindowTextW(_hwndEdit, sb, 260);
                                pwd = sb.ToString();
                            }
                            lock (_stateLock)
                            {
                                _inputPassword = pwd;
                                _passwordCancelled = false;
                            }
                            PostMessageW(hwnd, WM_USER_HIDE_PASSWORD_INPUT, IntPtr.Zero, IntPtr.Zero);
                            _passwordEvent.Set();
                        }
                        else if (id == 1002 || id == 2) // Cancel button
                        {
                            lock (_stateLock)
                            {
                                _inputPassword = null;
                                _passwordCancelled = true;
                            }
                            PostMessageW(hwnd, WM_USER_HIDE_PASSWORD_INPUT, IntPtr.Zero, IntPtr.Zero);
                            _passwordEvent.Set();
                        }
                    }
                    return IntPtr.Zero;
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
                            lock (_stateLock)
                            {
                                _passwordCancelled = true;
                            }
                            _passwordEvent.Set(); // Wake up background thread if blocked on password prompt
                            return IntPtr.Zero; // Wait for background thread to handle cancellation and close the window
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
                        if (!_completed && !_hasError && !_isPromptingPassword)
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
                                    bool isPasswordError = ex is PdfSharpCore.Pdf.IO.PdfReaderException &&
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

            bool hasErr, comp, isPrompting; string msg, errMsg, pctStr, promptFile; bool isRetry;
            double dispW; float shimOff; int tot, cur;

            lock (_stateLock)
            {
                hasErr = _hasError; comp = _completed;
                msg = _message; errMsg = _errorMessage;
                dispW = _currentDispWidth; shimOff = _shimmerOffset;
                tot = _total; cur = _current;
                isPrompting = _isPromptingPassword;
                promptFile = _passwordPromptFilename;
                isRetry = _passwordPromptIsRetry;
            }

            float s = _dpiScale;

            if (_titleFont != null)
                g.DrawString("Clickra", _titleFont, Brushes.White, 36 * s, 28 * s);

            if (_subFont != null)
            {
                string lang = ClickraStorage.GetSetting("Language");
                string subText = hasErr ? "作業失敗" : (comp ? "作業完成" : (isPrompting ? Localization.T("pdf_password_title", lang) : "正在執行作業..."));
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
            else if (isPrompting)
            {
                if (_msgFont != null)
                {
                    string lang = ClickraStorage.GetSetting("Language");
                    string promptFormat = isRetry 
                        ? Localization.T("pdf_password_retry", lang) 
                        : Localization.T("pdf_password_prompt", lang);
                    string promptText = string.Format(promptFormat, Path.GetFileName(promptFile));

                    using var promptBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
                    g.DrawString(promptText, _msgFont, promptBrush, new RectangleF(36 * s, 130 * s, 448 * s, 32 * s));
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
                    g.DrawString(pctStr, _pctFont, pctBrush, 484 * s - size.Width, 145 * s);
                }

                if (_tipFont != null)
                {
                    using var tipBrush = new SolidBrush(Color.FromArgb(100, 100, 100));
                    g.DrawString("請稍候，正在背景高速處理中...", _tipFont, tipBrush, 36 * s, 220 * s);
                }

                // Draw "Run in Background" (minimize to tray) icon button in top right
                {
                    var btnRect = new RectangleF(456 * s, 36 * s, 28 * s, 28 * s);
                    using var btnPath = GetRoundedRectPath(btnRect, 4 * s);

                    Color btnBg = _isTrayBtnHovered ? Color.FromArgb(60, 60, 60) : Color.Transparent;
                    Color btnPenColor = _isTrayBtnHovered ? GetSystemColorizationColor() : Color.FromArgb(160, 160, 160);

                    using var btnBrush = new SolidBrush(btnBg);
                    g.FillPath(btnBrush, btnPath);

                    if (_isTrayBtnHovered)
                    {
                        using var borderPen = new Pen(btnPenColor, 1f * s);
                        g.DrawPath(borderPen, btnPath);
                    }

                    // Draw diagonal arrow pointing down-right ↘
                    using var arrowPen = new Pen(btnPenColor, 2f * s);
                    float startX = btnRect.X + 8 * s;
                    float startY = btnRect.Y + 8 * s;
                    float endX = btnRect.X + 20 * s;
                    float endY = btnRect.Y + 20 * s;
                    g.DrawLine(arrowPen, startX, startY, endX, endY);
                    g.DrawLine(arrowPen, endX, endY, endX - 7 * s, endY);
                    g.DrawLine(arrowPen, endX, endY, endX, endY - 7 * s);

                    // Draw custom tooltip next to the button when hovered
                    if (_isTrayBtnHovered && _tipFont != null)
                    {
                        string lang = ClickraStorage.GetSetting("Language");
                        string tooltipText = Localization.T("progress_background", lang);
                        var tSize = g.MeasureString(tooltipText, _tipFont);
                        float tx = btnRect.X - tSize.Width - 10 * s;
                        float ty = btnRect.Y + (btnRect.Height - tSize.Height) / 2;

                        using var tBrush = new SolidBrush(Color.FromArgb(240, 30, 30, 30));
                        using var tPen = new Pen(Color.FromArgb(80, 80, 80), 1f * s);
                        using var textBrush = new SolidBrush(Color.FromArgb(220, 220, 220));

                        var tRect = new RectangleF(tx - 6 * s, ty - 4 * s, tSize.Width + 12 * s, tSize.Height + 8 * s);
                        using var tPath = GetRoundedRectPath(tRect, 4 * s);
                        g.FillPath(tBrush, tPath);
                        g.DrawPath(tPen, tPath);
                        g.DrawString(tooltipText, _tipFont, textBrush, tx, ty);
                    }
                }
            }

            using var targetG = Graphics.FromHdc(hdc);
            if (isPrompting)
            {
                targetG.ExcludeClip(new Rectangle((int)(36 * s - 1), (int)(165 * s - 1), (int)(448 * s + 2), (int)(28 * s + 2)));
                targetG.ExcludeClip(new Rectangle((int)(280 * s - 1), (int)(210 * s - 1), (int)(90 * s + 2), (int)(30 * s + 2)));
                targetG.ExcludeClip(new Rectangle((int)(394 * s - 1), (int)(210 * s - 1), (int)(90 * s + 2), (int)(30 * s + 2)));
            }
            if (_bufferBmp != null)
            {
                targetG.DrawImage(_bufferBmp, 0, 0, _bufferBmp.Width, _bufferBmp.Height);
            }
        }

        private static string TruncateProgressMessage(Graphics g, string msg, Font font, float maxLogicalWidth, float scale)
        {
            if (string.IsNullOrEmpty(msg)) return "";
            if (font == null) return msg;

            int colonIdx = msg.IndexOf(": ");
            if (colonIdx == -1)
            {
                return TruncateText(g, msg, font, maxLogicalWidth, scale);
            }

            string prefix = msg.Substring(0, colonIdx + 2);
            string rest = msg.Substring(colonIdx + 2);

            string filename = rest;
            string suffix = "";

            if (rest.EndsWith("..."))
            {
                int pIdx = rest.LastIndexOf(" (");
                if (pIdx != -1 && pIdx < rest.Length - 3)
                {
                    filename = rest.Substring(0, pIdx);
                    suffix = rest.Substring(pIdx);
                }
                else
                {
                    filename = rest.Substring(0, rest.Length - 3);
                    suffix = "...";
                }
            }
            else
            {
                int pIdx = rest.LastIndexOf(" (");
                if (pIdx != -1)
                {
                    filename = rest.Substring(0, pIdx);
                    suffix = rest.Substring(pIdx);
                }
            }

            float prefixW = g.MeasureString(prefix, font).Width / scale;
            float suffixW = g.MeasureString(suffix, font).Width / scale;
            float availableW = maxLogicalWidth - prefixW - suffixW;

            if (availableW <= 20)
            {
                return TruncateText(g, msg, font, maxLogicalWidth, scale);
            }

            string truncatedFile = TruncateFileName(g, filename, font, availableW, scale);
            return prefix + truncatedFile + suffix;
        }

        private static string TruncateText(Graphics g, string text, Font font, float maxLogicalWidth, float scale)
        {
            if (string.IsNullOrEmpty(text)) return "";
            float measuredWidth = g.MeasureString(text, font).Width / scale;
            if (measuredWidth <= maxLogicalWidth) return text;

            string suffix = "...";
            float suffixWidth = g.MeasureString(suffix, font).Width / scale;
            if (maxLogicalWidth <= suffixWidth) return "...";

            int low = 0;
            int high = text.Length - 1;
            int bestLength = 0;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                string candidate = text.Substring(0, mid) + suffix;
                float w = g.MeasureString(candidate, font).Width / scale;

                if (w <= maxLogicalWidth)
                {
                    bestLength = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return text.Substring(0, bestLength) + suffix;
        }

        private static string TruncateFileName(Graphics g, string filename, Font font, float maxWidth, float scale)
        {
            if (string.IsNullOrEmpty(filename)) return "";
            if (g.MeasureString(filename, font).Width / scale <= maxWidth) return filename;

            int low = 2;
            int high = filename.Length - 1;
            string best = "...";

            int extLen = 0;
            int dotIdx = filename.LastIndexOf('.');
            if (dotIdx >= 0)
            {
                extLen = filename.Length - dotIdx;
            }

            int targetRight = extLen + 8;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                
                int rightLen, leftLen;
                if (mid > targetRight)
                {
                    rightLen = targetRight;
                    leftLen = mid - rightLen;
                }
                else
                {
                    rightLen = Math.Min(extLen, mid - 1);
                    if (rightLen < 0) rightLen = 0;
                    leftLen = mid - rightLen;
                }

                string separator = "...";
                string rightPart = filename.Substring(filename.Length - rightLen);
                if (rightPart.StartsWith("."))
                {
                    separator = "..";
                }
                string candidate = filename.Substring(0, leftLen) + separator + rightPart;

                if (g.MeasureString(candidate, font).Width / scale <= maxWidth)
                {
                    best = candidate;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            if (best == "...")
            {
                int left = Math.Max(1, filename.Length - extLen);
                string suffix = extLen > 0 ? filename.Substring(filename.Length - extLen) : "";
                best = filename.Substring(0, Math.Min(2, left)) + (suffix.StartsWith(".") ? ".." : "...") + suffix;
            }

            return best;
        }
    }
}

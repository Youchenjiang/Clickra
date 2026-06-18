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

        private const uint WM_USER_SHOW_PASSWORD_INPUT = 0x0400 + 3;
        private const uint WM_USER_HIDE_PASSWORD_INPUT = 0x0400 + 4;

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

        // GDI+ 雙緩衝
        private Bitmap? _bufferBmp;
        private Graphics? _bufferGraphics;

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



    }
}

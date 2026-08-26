using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Clickra_Fluent;

/// <summary>
/// 系統匣圖示（NotifyIcon）實作，補上 Fluent 缺少的「縮至系統匣繼續轉換」行為
/// （對照 NativeAOT 進度視窗的 <c>ProgressWindow.Tray.cs</c>，見
/// <c>docs/development/fluent_aot_parity.md</c> G1）。
///
/// WinUI 3 沒有內建系統匣 API，這裡以 P/Invoke <c>Shell_NotifyIcon</c> 實作；
/// 回呼（WM_TRAYICON）由一個 message-only 視窗接收並轉發 <see cref="DoubleClick"/>，
/// 避免改寫 WinUI 視窗的 WndProc。message-only 視窗建立在 UI 執行緒上，
/// 因此事件會在 UI 執行緒觸發，可直接操作 XAML。
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint WM_TRAYICON = 0x0400 + 1;
    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const string MessageWindowClass = "ClickraFluentTrayWindow";

    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private static readonly Dictionary<IntPtr, TrayIcon> s_instances = new();
    // WndProc delegate 必須常駐，否則會被 GC 回收導致回呼崩潰。
    private static readonly WndProcDelegate s_wndProc = TrayWndProc;

    private readonly object _lock = new();
    private readonly IntPtr _hWnd;
    private IntPtr _hIcon;
    private NOTIFYICONDATAW _nid;
    private bool _added;

    /// <summary>匣圖示被雙擊（在 UI 執行緒觸發）。</summary>
    public event Action? DoubleClick;

    /// <summary>匣圖示被右鍵點擊（在 UI 執行緒觸發）。</summary>
    public event Action? RightClick;

    /// <summary>建立匣圖示並顯示（NIM_ADD）。</summary>
    public TrayIcon(string tooltip)
    {
        _hWnd = CreateMessageWindow();
        if (_hWnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create tray message window");
        s_instances[_hWnd] = this;

        _hIcon = LoadAppIcon();

        _nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = tooltip
        };
        Shell_NotifyIcon(NIM_ADD, ref _nid);
        _added = true;
    }

    /// <summary>更新 tooltip（例如顯示轉換進度 %）。可從背景執行緒呼叫。</summary>
    public void SetTooltip(string tooltip)
    {
        lock (_lock)
        {
            if (!_added) return;
            _nid.szTip = tooltip;
            _nid.uFlags = NIF_TIP;
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        }
    }

    /// <summary>移除匣圖示並釋放資源。</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_added)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _nid);
                _added = false;
            }
        }
        s_instances.Remove(_hWnd);
        if (_hWnd != IntPtr.Zero) DestroyWindow(_hWnd);
        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }

    private static IntPtr CreateMessageWindow()
    {
        IntPtr hInstance = GetModuleHandleW(null);
        var wc = new WNDCLASSW
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
            hInstance = hInstance,
            lpszClassName = MessageWindowClass
        };
        if (RegisterClassW(ref wc) == 0 && GetLastError() != 1410 /* ERROR_CLASS_ALREADY_EXISTS */)
            return IntPtr.Zero;
        return CreateWindowExW(0, MessageWindowClass, "ClickraTray", 0, 0, 0, 0, 0,
            HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);
    }

    private static IntPtr TrayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON && s_instances.TryGetValue(hWnd, out var icon))
        {
            if (lParam.ToInt64() == WM_LBUTTONDBLCLK)
                icon.DoubleClick?.Invoke();
            else if (lParam.ToInt64() == WM_RBUTTONUP)
                icon.RightClick?.Invoke();
            return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static IntPtr LoadAppIcon()
    {
        // 與 MainWindow 相同的 app.ico（由 csproj 複製到輸出目錄）。
        string icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(icoPath))
        {
            IntPtr icon = LoadImageW(IntPtr.Zero, icoPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
            if (icon != IntPtr.Zero) return icon;
        }
        // 備援：直接從執行檔抽第一個圖示（NativeAOT 的做法）。
        try
        {
            string exe = Environment.ProcessPath ?? "";
            if (exe.Length > 0) return ExtractIconW(IntPtr.Zero, exe, 0);
        }
        catch { /* Ignored: icon 載入失敗不影響匣圖示註冊。 */ }
        return IntPtr.Zero;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    // 與 Clickra.CLI/Native/Win32.cs 的 NOTIFYICONDATAW 相同配置，確保 ABI 一致。
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hinst, string lpszName, uint type, int cx, int cy, uint fuLoad);

    [DllImport("shell32.dll", EntryPoint = "ExtractIconW", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIconW(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("kernel32.dll")]
    private static extern uint GetLastError();
}

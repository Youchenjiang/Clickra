using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Text;

namespace Clickra.UI.Native
{
    public static class Win32
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX { public uint cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground; public IntPtr lpszMenuName; public IntPtr lpszClassName; public IntPtr hIconSm; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public Point pt; }

        [StructLayout(LayoutKind.Sequential)]
        public struct PAINTSTRUCT { public IntPtr hdc; public bool fErase; public int rcLeft, rcTop, rcRight, rcBottom; public bool fRestore, fIncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        /// <summary>Native POINT structure used by DPI-aware window size messages.</summary>
        public struct NATIVEPOINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        /// <summary>Native MINMAXINFO payload used to constrain Dashboard window sizing.</summary>
        public struct MINMAXINFO
        {
            /// <summary>Reserved native point supplied by Windows.</summary>
            public NATIVEPOINT ptReserved;
            /// <summary>Maximum tracking size supplied by Windows.</summary>
            public NATIVEPOINT ptMaxSize;
            /// <summary>Maximum window position supplied by Windows.</summary>
            public NATIVEPOINT ptMaxPosition;
            /// <summary>Minimum tracking size returned to Windows.</summary>
            public NATIVEPOINT ptMinTrackSize;
            /// <summary>Maximum tracking size returned to Windows.</summary>
            public NATIVEPOINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATAW { public uint cbSize; public IntPtr hWnd; public uint uID; public uint uFlags; public uint uCallbackMessage; public IntPtr hIcon; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip; public uint dwState; public uint dwStateMask; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo; public uint uTimeoutOrVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle; public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct OPENFILENAME { public int lStructSize; public IntPtr hwndOwner; public IntPtr hInstance; public string lpstrFilter; public string lpstrCustomFilter; public int nMaxCustFilter; public int nFilterIndex; public IntPtr lpstrFile; public int nMaxFile; public string lpstrFileTitle; public int nMaxFileTitle; public string lpstrInitialDir; public string lpstrTitle; public int Flags; public short nFileOffset; public short nFileExtension; public string lpstrDefExt; public IntPtr lCustData; public IntPtr lpfnHook; public string lpTemplateName; public IntPtr pvReserved; public int dwReserved; public int FlagsEx; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct BROWSEINFO { public IntPtr hwndOwner; public IntPtr pidlRoot; public IntPtr pszDisplayName; public IntPtr lpszTitle; public uint ulFlags; public IntPtr lpfn; public IntPtr lParam; public int iImage; }

        [DllImport("user32.dll", EntryPoint = "AdjustWindowRectEx", CharSet = CharSet.Unicode)] public static extern bool AdjustWindowRectEx(ref RECT lpRect, uint dwStyle, bool bMenu, uint dwExStyle);
        [DllImport("user32.dll", EntryPoint = "AdjustWindowRectExForDpi", CharSet = CharSet.Unicode)]
        private static extern bool AdjustWindowRectExForDpiNative(ref RECT lpRect, uint dwStyle, bool bMenu, uint dwExStyle, uint dpi); // skipcq: CS-R1138 — Win32 ABI requires RECT first.
        /// <summary>Adjusts a client rectangle for the specified window styles at an explicit DPI.</summary>
        public static bool AdjustWindowRectExForDpi(uint dwStyle, bool bMenu, uint dwExStyle, uint dpi, ref RECT lpRect) =>
            AdjustWindowRectExForDpiNative(ref lpRect, dwStyle, bMenu, dwExStyle, dpi);
        [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassEx(ref WNDCLASSEX c);
        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr p, IntPtr m, IntPtr inst, IntPtr par);
        [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
        [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)] public static extern bool SetWindowText(IntPtr h, string text);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
        [DllImport("user32.dll")] public static extern int GetMessage(out MSG m, IntPtr h, uint f, uint l);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG m);
        [DllImport("user32.dll")] public static extern IntPtr DefWindowProcW(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT p);
        [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT p);
        [DllImport("user32.dll")] public static extern void PostQuitMessage(int c);
        [DllImport("user32.dll")] public static extern IntPtr LoadCursorW(IntPtr h, int n);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
        [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr hCursor);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll", EntryPoint = "DestroyWindow")] public static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);
        [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr hWnd, IntPtr nIDEvent);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll", EntryPoint = "EnableWindow")] private static extern bool EnableWindowNative(IntPtr hWnd, bool bEnable);
        [DllImport("user32.dll", EntryPoint = "IsWindow")] private static extern bool IsWindowNative(IntPtr hWnd);
        [DllImport("user32.dll", EntryPoint = "IsWindowEnabled")] private static extern bool IsWindowEnabledNative(IntPtr hWnd);
        /// <summary>Enables or disables a native window through the managed interop boundary.</summary>
        public static bool EnableWindow(IntPtr hWnd, bool bEnable) => EnableWindowNative(hWnd, bEnable);
        /// <summary>Returns whether the supplied native window handle is valid.</summary>
        public static bool IsWindow(IntPtr hWnd) => IsWindowNative(hWnd);
        /// <summary>Returns whether the supplied native window is enabled for input.</summary>
        public static bool IsWindowEnabled(IntPtr hWnd) => IsWindowEnabledNative(hWnd);
        [DllImport("user32.dll")] public static extern IntPtr SetCapture(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll", EntryPoint = "IsDialogMessageW")] public static extern bool IsDialogMessageW(IntPtr hDlg, ref MSG lpMsg);
        [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", CharSet = CharSet.Unicode)] public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", CharSet = CharSet.Unicode)] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hWnd);
        [DllImport("user32.dll", EntryPoint = "CallWindowProcW", CharSet = CharSet.Unicode)] public static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetProp(IntPtr hWnd, string lpString);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetProp(IntPtr hWnd, string lpString, IntPtr hData);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr RemoveProp(IntPtr hWnd, string lpString);
        [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);
        [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point lpPoint);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hWnd, ref Point lpPoint);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("gdi32.dll")] public static extern IntPtr CreateSolidBrush(uint crColor);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll", EntryPoint = "SetTextColor")] public static extern uint SetTextColor(IntPtr hdc, uint crColor);
        [DllImport("gdi32.dll", EntryPoint = "SetBkColor")] public static extern uint SetBkColor(IntPtr hdc, uint crColor);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateFontW(int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight, uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet, uint iOutPrecision, uint iClipPrecision, uint iQuality, uint iPitchAndFamily, string pszFaceName);

        [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATAW lpData);
        [DllImport("shell32.dll", EntryPoint = "ExtractIconW", CharSet = CharSet.Unicode)] public static extern IntPtr ExtractIcon(IntPtr h, string path, int idx);
        [DllImport("shell32.dll")] public static extern void DragAcceptFiles(IntPtr hwnd, bool accept);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern uint DragQueryFileW(IntPtr hDrop, uint iFile, IntPtr lpszFile, uint cch);
        [DllImport("shell32.dll")] public static extern void DragFinish(IntPtr hDrop);
        [DllImport("shell32.dll", EntryPoint = "SHBrowseForFolderW", CharSet = CharSet.Unicode)] public static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);
        [DllImport("shell32.dll", EntryPoint = "SHGetPathFromIDListW", CharSet = CharSet.Unicode)] public static extern bool SHGetPathFromIDList(IntPtr pidl, IntPtr pszPath);

        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("dwmapi.dll", PreserveSig = false)] public static extern void DwmGetColorizationColor(out uint pcrColorization, out bool pfOpaqueBlend);

        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? lpModuleName);
        [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode)] public static extern bool GetOpenFileName(ref OPENFILENAME ofn);
        [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr pv);

        /// <summary>Standard overlapped top-level window style.</summary>
        public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
        /// <summary>Fixed-size overlapped style used by modal progress windows.</summary>
        public const uint WS_OVERLAPPED_FIXED = (0x00CF0000 | 0x02000000) & ~0x00040000u & ~0x00010000u;
        /// <summary>DWM attribute identifier for immersive dark mode.</summary>
        public const int DWMWA_DARK_MODE = 20;
        /// <summary>Requests the system default window position or dimension.</summary>
        public const int CW_USEDEFAULT = unchecked((int)0x80000000);
        /// <summary>Application message emitted by the notification-area icon.</summary>
        public const uint WM_TRAYICON = 0x0400 + 1;
        /// <summary>Application message requesting a progress-window repaint.</summary>
        public const uint WM_USER_INVALIDATE = 0x0400 + 2;
        /// <summary>Application message requesting password-input controls to be shown.</summary>
        public const uint WM_USER_SHOW_PASSWORD_INPUT = 0x0400 + 3;
        /// <summary>Application message requesting password-input controls to be hidden.</summary>
        public const uint WM_USER_HIDE_PASSWORD_INPUT = 0x0400 + 4;
        /// <summary>Application message used to marshal Dashboard actions onto its window thread.</summary>
        public const uint WM_USER_DASHBOARD_ACTION = 0x0400 + 5;
        /// <summary>Notification-area command that adds an icon.</summary>
        public const uint NIM_ADD = 0;
        /// <summary>Notification-area command that updates an icon.</summary>
        public const uint NIM_MODIFY = 1;
        /// <summary>Notification-area command that removes an icon.</summary>
        public const uint NIM_DELETE = 2;
        /// <summary>Notification-area flag enabling callback messages.</summary>
        public const uint NIF_MESSAGE = 1;
        /// <summary>Notification-area flag supplying an icon handle.</summary>
        public const uint NIF_ICON = 2;
        /// <summary>Notification-area flag supplying tooltip text.</summary>
        public const uint NIF_TIP = 4;
        /// <summary>System-command identifier for minimizing a window.</summary>
        public const uint SC_MINIMIZE = 0xF020;
        /// <summary>Windows message carrying system-command requests.</summary>
        public const uint WM_SYSCOMMAND = 0x0112;
        /// <summary>ShowWindow command that hides a window.</summary>
        public const int SW_HIDE = 0;
        /// <summary>ShowWindow command that shows a window in its current state.</summary>
        public const int SW_SHOW = 5;
        /// <summary>ShowWindow command that restores a minimized window.</summary>
        public const int SW_RESTORE = 9;
        /// <summary>Window style that excludes child windows from parent painting.</summary>
        public const uint WS_CLIPCHILDREN = 0x02000000;
        /// <summary>Window style that creates a child window.</summary>
        public const uint WS_CHILD = 0x40000000;
        /// <summary>Window style that makes a window initially visible.</summary>
        public const uint WS_VISIBLE = 0x10000000;
        /// <summary>Window style that draws a thin border.</summary>
        public const uint WS_BORDER = 0x00800000;
        /// <summary>Window style that includes a control in keyboard tab navigation.</summary>
        public const uint WS_TABSTOP = 0x00010000;
        /// <summary>System cursor identifier for the pointing-hand cursor.</summary>
        public const int IDC_HAND = 32649;

        [DllImport("user32.dll", EntryPoint = "CreatePopupMenu", SetLastError = true)]
        private static extern IntPtr CreatePopupMenuNative();

        [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool AppendMenuNative(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll", EntryPoint = "TrackPopupMenuEx", SetLastError = true)]
        private static extern uint TrackPopupMenuExNative(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

        [DllImport("user32.dll", EntryPoint = "DestroyMenu", SetLastError = true)]
        private static extern bool DestroyMenuNative(IntPtr hMenu);

        /// <summary>Result of the native progress window's tray action menu.</summary>
        public enum TrayPopupCommand : uint
        {
            None = 0,
            Restore = 1,
            Cancel = 2,
            Unavailable = uint.MaxValue
        }

        /// <summary>Shows the native Restore/Cancel popup and returns the selected tray command.</summary>
        public static TrayPopupCommand ShowTrayActionMenu(
            IntPtr hwnd,
            int x,
            int y,
            string restoreText,
            string cancelText)
        {
            IntPtr menu = CreatePopupMenuNative();
            if (menu == IntPtr.Zero) return TrayPopupCommand.Unavailable;

            try
            {
                AppendMenuNative(menu, MF_STRING, (IntPtr)TrayPopupCommand.Restore, restoreText);
                AppendMenuNative(menu, MF_SEPARATOR, IntPtr.Zero, null);
                AppendMenuNative(menu, MF_STRING, (IntPtr)TrayPopupCommand.Cancel, cancelText);

                uint command = TrackPopupMenuExNative(
                    menu,
                    TPM_RETURNCMD | TPM_NONOTIFY | TPM_BOTTOMALIGN,
                    x,
                    y,
                    hwnd,
                    IntPtr.Zero);
                return command is (uint)TrayPopupCommand.Restore or (uint)TrayPopupCommand.Cancel
                    ? (TrayPopupCommand)command
                    : TrayPopupCommand.None;
            }
            finally
            {
                DestroyMenuNative(menu);
            }
        }

        /// <summary>Managed signature for the native window procedure callback.</summary>
        public delegate IntPtr WndProcDelegate(IntPtr h, uint msg, IntPtr w, IntPtr l);
        /// <summary>Timer identifier reserved for periodic progress-window refreshes.</summary>
        public static readonly IntPtr TIMER_ID_REFRESH = (IntPtr)1001;
        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;
        private const uint TPM_RETURNCMD = 0x00000100;
        private const uint TPM_NONOTIFY = 0x00000080;
        private const uint TPM_BOTTOMALIGN = 0x00000020;
    }
}

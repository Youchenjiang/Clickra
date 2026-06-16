using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using Clickra.Core;

namespace Clickra.UI
{
    public static class PasswordPrompt
    {
        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(uint crColor);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", CharSet = CharSet.Unicode)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool AdjustWindowRectEx(ref RECT lpRect, uint dwStyle, bool bMenu, uint dwExStyle);

        [DllImport("gdi32.dll", EntryPoint = "SetTextColor")]
        private static extern uint SetTextColor(IntPtr hdc, uint crColor);

        [DllImport("gdi32.dll", EntryPoint = "SetBkColor")]
        private static extern uint SetBkColor(IntPtr hdc, uint crColor);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int nExitCode);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFontW(int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight, uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet, uint iOutPrecision, uint iClipPrecision, uint iQuality, uint iPitchAndFamily, string pszFaceName);

        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetProp(IntPtr hWnd, string lpString);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetProp(IntPtr hWnd, string lpString, IntPtr hData);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr RemoveProp(IntPtr hWnd, string lpString);

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
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int left, top, right, bottom; }

        private static bool _classRegistered = false;
        private static IntPtr _darkBrush = IntPtr.Zero;
        private static IntPtr _editBgBrush = IntPtr.Zero;

        private static string? _resultPassword = null;
        private static bool _cancelled = true;
        private static IntPtr _hFont = IntPtr.Zero;

        private const uint WS_CHILD = 0x40000000;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint WS_BORDER = 0x00800000;
        private const uint WS_TABSTOP = 0x00010000;

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static unsafe IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            switch (msg)
            {
                case 0x0111: // WM_COMMAND
                    {
                        int id = (int)w.ToInt64() & 0xFFFF;
                        if (id == 1) // OK button
                        {
                            IntPtr hwndEdit = GetDlgItem(hwnd, 101);
                            var sb = new StringBuilder(260);
                            GetWindowTextW(hwndEdit, sb, 260);
                            _resultPassword = sb.ToString();
                            _cancelled = false;
                            DestroyWindow(hwnd);
                        }
                        else if (id == 2) // Cancel button
                        {
                            _resultPassword = null;
                            _cancelled = true;
                            DestroyWindow(hwnd);
                        }
                    }
                    return IntPtr.Zero;

                case 0x0138: // WM_CTLCOLORSTATIC
                case 0x0136: // WM_CTLCOLORDLG
                    {
                        IntPtr hdc = w;
                        SetTextColor(hdc, 0x00FFFFFF); // White
                        SetBkColor(hdc, 0x00202020); // Dark (32, 32, 32)
                        return _darkBrush;
                    }

                case 0x0133: // WM_CTLCOLOREDIT
                    {
                        IntPtr hdc = w;
                        SetTextColor(hdc, 0x00FFFFFF); // White
                        SetBkColor(hdc, 0x002D2D2D); // Edit bg (45, 45, 45)
                        return _editBgBrush;
                    }

                case 0x0010: // WM_CLOSE
                    _resultPassword = null;
                    _cancelled = true;
                    DestroyWindow(hwnd);
                    return IntPtr.Zero;

                case 0x0002: // WM_DESTROY
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, w, l);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static unsafe IntPtr EditSubclassProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            IntPtr oldProc = GetProp(hwnd, "ClickraOldWndProc");
            if (msg == 0x0100) // WM_KEYDOWN
            {
                int key = w.ToInt32();
                if (key == 0x0D) // VK_RETURN
                {
                    IntPtr parent = GetParent(hwnd);
                    PostMessageW(parent, 0x0111, (IntPtr)1, IntPtr.Zero); // WM_COMMAND, ID = 1
                    return IntPtr.Zero;
                }
                if (key == 0x1B) // VK_ESCAPE
                {
                    IntPtr parent = GetParent(hwnd);
                    PostMessageW(parent, 0x0111, (IntPtr)2, IntPtr.Zero); // WM_COMMAND, ID = 2
                    return IntPtr.Zero;
                }
            }
            if (msg == 0x0002) // WM_DESTROY
            {
                RemoveProp(hwnd, "ClickraOldWndProc");
            }
            return oldProc != IntPtr.Zero ? CallWindowProcW(oldProc, hwnd, msg, w, l) : DefWindowProcW(hwnd, msg, w, l);
        }

        public static unsafe string? Prompt(IntPtr hwndParent, string filename, bool isRetry)
        {
            uint dpi = 96;
            try { dpi = GetDpiForSystem(); } catch { }
            float scale = dpi / 96.0f;

            if (_darkBrush == IntPtr.Zero) _darkBrush = CreateSolidBrush(0x00202020);
            if (_editBgBrush == IntPtr.Zero) _editBgBrush = CreateSolidBrush(0x002D2D2D);

            string lang = ClickraStorage.GetSetting("Language");
            string title = Localization.T("pdf_password_title", lang);
            string prompt = isRetry ? Localization.T("pdf_password_retry", lang) : Localization.T("pdf_password_prompt", lang);
            string cleanPrompt = string.Format(prompt, Path.GetFileName(filename));

            string className = "ClickraPasswordDlg";
            IntPtr hInstance = GetModuleHandle(null);

            if (!_classRegistered)
            {
                var wc = new WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                    lpfnWndProc = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProc,
                    hInstance = hInstance,
                    hCursor = LoadCursorW(IntPtr.Zero, 32512), // IDC_ARROW = 32512
                    hbrBackground = _darkBrush,
                    lpszClassName = Marshal.StringToHGlobalUni(className)
                };
                RegisterClassEx(ref wc);
                _classRegistered = true;
            }

            int clientW = (int)(400 * scale);
            int clientH = (int)(160 * scale);

            var rect = new RECT { left = 0, top = 0, right = clientW, bottom = clientH };
            AdjustWindowRectEx(ref rect, 0x00C00000 | 0x00080000, false, 0); // WS_CAPTION | WS_SYSMENU
            int winW = rect.right - rect.left;
            int winH = rect.bottom - rect.top;

            int x = (GetSystemMetrics(0) - winW) / 2;
            int y = (GetSystemMetrics(1) - winH) / 2;

            if (hwndParent != IntPtr.Zero)
            {
                RECT parentRect;
                if (GetWindowRect(hwndParent, out parentRect))
                {
                    int parentW = parentRect.right - parentRect.left;
                    int parentH = parentRect.bottom - parentRect.top;
                    x = parentRect.left + (parentW - winW) / 2;
                    y = parentRect.top + (parentH - winH) / 2;
                }
            }

            _resultPassword = null;
            _cancelled = true;

            IntPtr hwndDlg = CreateWindowEx(0, className, title, 0x00C00000 | 0x00080000, x, y, winW, winH, hwndParent, IntPtr.Zero, hInstance, IntPtr.Zero);

            int dark = 1;
            DwmSetWindowAttribute(hwndDlg, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE = 20

            // Font setting
            string fontName = "Segoe UI";
            string normLang = Localization.NormalizeLanguageCode(lang);
            if (normLang.StartsWith("zh-TW")) fontName = "Microsoft JhengHei UI";
            else if (normLang.StartsWith("zh-CN")) fontName = "Microsoft YaHei UI";
            else if (normLang.StartsWith("ja")) fontName = "Yu Gothic UI";
            else if (normLang.StartsWith("ko")) fontName = "Malgun Gothic";

            if (_hFont == IntPtr.Zero)
            {
                _hFont = CreateFontW((int)(14.5 * scale), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, fontName);
            }

            // Create controls
            IntPtr hwndStatic = CreateWindowEx(0, "STATIC", cleanPrompt, WS_CHILD | WS_VISIBLE, (int)(20 * scale), (int)(20 * scale), (int)(360 * scale), (int)(40 * scale), hwndDlg, IntPtr.Zero, hInstance, IntPtr.Zero);
            IntPtr hwndEdit = CreateWindowEx(0, "EDIT", "", WS_CHILD | WS_VISIBLE | WS_BORDER | WS_TABSTOP | 0x0020 | 0x0080, (int)(20 * scale), (int)(65 * scale), (int)(360 * scale), (int)(26 * scale), hwndDlg, (IntPtr)101, hInstance, IntPtr.Zero);
            IntPtr hwndBtnOk = CreateWindowEx(0, "BUTTON", Localization.T("dialog_ok", lang), WS_CHILD | WS_VISIBLE | WS_TABSTOP | 0x00000001, (int)(180 * scale), (int)(110 * scale), (int)(90 * scale), (int)(30 * scale), hwndDlg, (IntPtr)1, hInstance, IntPtr.Zero);
            IntPtr hwndBtnCancel = CreateWindowEx(0, "BUTTON", Localization.T("dialog_cancel", lang), WS_CHILD | WS_VISIBLE | WS_TABSTOP, (int)(290 * scale), (int)(110 * scale), (int)(90 * scale), (int)(30 * scale), hwndDlg, (IntPtr)2, hInstance, IntPtr.Zero);

            SendMessage(hwndStatic, 0x0030, _hFont, (IntPtr)1);
            SendMessage(hwndEdit, 0x0030, _hFont, (IntPtr)1);
            SendMessage(hwndBtnOk, 0x0030, _hFont, (IntPtr)1);
            SendMessage(hwndBtnCancel, 0x0030, _hFont, (IntPtr)1);

            // Subclass EDIT control for Enter/Esc VKs
            IntPtr originalEditProc = GetWindowLongPtr(hwndEdit, -4);
            SetProp(hwndEdit, "ClickraOldWndProc", originalEditProc);
            SetWindowLongPtr(hwndEdit, -4, (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&EditSubclassProc);

            if (hwndParent != IntPtr.Zero)
            {
                EnableWindow(hwndParent, false);
            }

            ShowWindow(hwndDlg, 5); // SW_SHOW = 5
            UpdateWindow(hwndDlg);
            SetFocus(hwndEdit);

            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (hwndParent != IntPtr.Zero)
            {
                EnableWindow(hwndParent, true);
                SetWindowPos(hwndParent, IntPtr.Zero, 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0040); // SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW
            }

            if (_darkBrush != IntPtr.Zero) { DeleteObject(_darkBrush); _darkBrush = IntPtr.Zero; }
            if (_editBgBrush != IntPtr.Zero) { DeleteObject(_editBgBrush); _editBgBrush = IntPtr.Zero; }
            if (_hFont != IntPtr.Zero) { DeleteObject(_hFont); _hFont = IntPtr.Zero; }

            return _cancelled ? null : _resultPassword;
        }

        [DllImport("user32.dll", EntryPoint = "LoadCursorW", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadCursorW(IntPtr hInstance, int lpCursorName);
    }
}

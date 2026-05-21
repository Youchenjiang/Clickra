using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using Microsoft.Win32;
using Clickra.Core;

namespace Clickra.UI
{
    public static class DashboardWindow
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
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
        [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT p);
        [DllImport("user32.dll")] static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT p);
        [DllImport("user32.dll")] static extern void PostQuitMessage(int c);
        [DllImport("user32.dll")] static extern IntPtr LoadCursorW(IntPtr h, int n);
        [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
        [DllImport("user32.dll")] static extern IntPtr SetCursor(IntPtr hCursor);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
        
        [DllImport("shell32.dll", EntryPoint = "ExtractIconW", CharSet = CharSet.Unicode)] 
        static extern IntPtr ExtractIcon(IntPtr h, string path, int idx);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("dwmapi.dll", PreserveSig = false)] static extern void DwmGetColorizationColor(out uint pcrColorization, out bool pfOpaqueBlend);

        // Timer for real-time history refresh
        [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);
        [DllImport("user32.dll")] static extern bool KillTimer(IntPtr hWnd, IntPtr nIDEvent);
        static readonly IntPtr TIMER_ID_REFRESH = (IntPtr)1001;

        [DllImport("shell32.dll")]
        static extern void DragAcceptFiles(IntPtr hwnd, bool accept);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern uint DragQueryFileW(IntPtr hDrop, uint iFile, IntPtr lpszFile, uint cch);

        [DllImport("shell32.dll")]
        static extern void DragFinish(IntPtr hDrop);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OPENFILENAME
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode)]
        static extern bool GetOpenFileName(ref OPENFILENAME ofn);

        const uint WS_OVERLAPPED_FIXED = 0x00CF0000 & ~0x00040000u & ~0x00020000u;
        const int DWMWA_DARK_MODE = 20;
        const int CW_USEDEFAULT = unchecked((int)0x80000000);

        delegate IntPtr WndProcDelegate(IntPtr h, uint msg, IntPtr w, IntPtr l);
        static WndProcDelegate _wndProc = WndProc;

        // UI State Variables
        static int _activeTab = 0; // 0: Overview, 1: Convert, 2: History, 3: Settings
        static int _hoveredElement = -1; // IDs of hovered elements
        
        // Convert tab state
        static int _convertCommandIndex = 1; // Default: 1 (word2pdf)
        static List<string> _selectedFiles = new List<string>();
        private static readonly string[] ConvertCommands = { "ppt2pdf", "word2pdf", "merge-pdf", "img2pdf", "img-merge", "img-stitch" };
        
        // Language Dropdown state
        static bool _langDropdownOpen = false;
        static string _langSearchQuery = "";
        static int _langHoveredIndex = 0;
        private static readonly List<(string Code, string NativeName, string EnglishName)> SupportedLanguages = new()
        {
            ("zh-TW", "繁體中文", "Traditional Chinese"),
            ("zh-CN", "简体中文", "Simplified Chinese"),
            ("en-US", "English", "English"),
            ("ja-JP", "日本語", "Japanese"),
            ("ko-KR", "한국어", "Korean")
        };
        
        // History & Statistics Cache
        static List<ClickraStorage.HistoryEntry> _historyEntries = new List<ClickraStorage.HistoryEntry>();
        static int _statTotal = 0;
        static int _statSuccess = 0;
        static int _statFailed = 0;

        // Double Buffering & Colors
        static Bitmap? _bufferBmp;
        static Graphics? _bufferGraphics;
        static Color _cachedColorizationColor = Color.FromArgb(255, 0, 120, 212);
        static bool _hasCachedColorizationColor = false;

        // Fonts
        static Font? _titleFont;
        static Font? _subFont;
        static Font? _tabFont;
        static Font? _contentTitleFont;
        static Font? _sectionFont;
        static Font? _bodyFont;
        static Font? _tagFont;
        static Font? _iconFont;

        public static void Show()
        {
            RefreshHistoryData();

            string className = "ClickraWnd";
            IntPtr hClass = Marshal.StringToHGlobalUni(className);

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = Marshal.GetHINSTANCE(typeof(DashboardWindow).Module),
                hCursor = LoadCursorW(IntPtr.Zero, 32512),
                hbrBackground = IntPtr.Zero,
                lpszClassName = hClass
            };

            RegisterClassEx(ref wc);

            var rect = new RECT { left = 0, top = 0, right = 760, bottom = 460 };
            AdjustWindowRectEx(ref rect, WS_OVERLAPPED_FIXED, false, 0);
            int winW = rect.right - rect.left;
            int winH = rect.bottom - rect.top;

            var hwnd = CreateWindowEx(0, className, "Clickra",
                WS_OVERLAPPED_FIXED, CW_USEDEFAULT, CW_USEDEFAULT, winW, winH,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

            // Enable Drag and Drop
            DragAcceptFiles(hwnd, true);

            // Dark title bar
            int dark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_DARK_MODE, ref dark, sizeof(int));

            // Set Title again to be safe
            SetWindowText(hwnd, "Clickra");

            // Load icon from exe
            string exePath = Environment.ProcessPath ?? "";
            if (!string.IsNullOrEmpty(exePath))
            {
                var hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
                if (hIcon != IntPtr.Zero)
                {
                    SendMessageW(hwnd, 0x0080, (IntPtr)0, hIcon); // ICON_BIG
                    SendMessageW(hwnd, 0x0080, (IntPtr)1, hIcon); // ICON_SMALL
                }
            }

            // GDI+ Resources
            _titleFont ??= new Font("Segoe UI Variable Display", 22, FontStyle.Bold);
            _subFont ??= new Font("Segoe UI Variable Display", 9);
            _tabFont ??= new Font("Segoe UI Variable Display", 11);
            _contentTitleFont ??= new Font("Segoe UI Variable Display", 20, FontStyle.Bold);
            _sectionFont ??= new Font("Segoe UI Variable Display", 12, FontStyle.Bold);
            _bodyFont ??= new Font("Segoe UI Variable Display", 10);
            _tagFont ??= new Font("Segoe UI Variable Display", 8.5f, FontStyle.Bold);
            _iconFont ??= new Font("Segoe MDL2 Assets", 11);

            if (_bufferBmp == null)
            {
                _bufferBmp = new Bitmap(760, 460);
                _bufferGraphics = Graphics.FromImage(_bufferBmp);
                _bufferGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                _bufferGraphics.SmoothingMode = SmoothingMode.AntiAlias;
            }

            ShowWindow(hwnd, 5);

            // 每 800ms 刷新一次歷史資料（供即時轉換狀態顯示）
            SetTimer(hwnd, TIMER_ID_REFRESH, 800, IntPtr.Zero);

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            
            Marshal.FreeHGlobal(hClass);
        }

        static void RefreshHistoryData()
        {
            try
            {
                _historyEntries = ClickraStorage.GetHistory(50);
                _statTotal = _historyEntries.Count;
                _statSuccess = _historyEntries.Count(h => h.IsSuccess);
                _statFailed = _historyEntries.Count(h => !h.IsSuccess);
            }
            catch
            {
                _historyEntries = new List<ClickraStorage.HistoryEntry>();
                _statTotal = 0;
                _statSuccess = 0;
                _statFailed = 0;
            }
        }

        static void CleanupResources()
        {
            try { _titleFont?.Dispose(); _titleFont = null; } catch { }
            try { _subFont?.Dispose(); _subFont = null; } catch { }
            try { _tabFont?.Dispose(); _tabFont = null; } catch { }
            try { _contentTitleFont?.Dispose(); _contentTitleFont = null; } catch { }
            try { _sectionFont?.Dispose(); _sectionFont = null; } catch { }
            try { _bodyFont?.Dispose(); _bodyFont = null; } catch { }
            try { _tagFont?.Dispose(); _tagFont = null; } catch { }
            try { _iconFont?.Dispose(); _iconFont = null; } catch { }
            try { _bufferGraphics?.Dispose(); _bufferGraphics = null; } catch { }
            try { _bufferBmp?.Dispose(); _bufferBmp = null; } catch { }
        }

        static int HitTest(int x, int y)
        {
            // Sidebar tabs (always active)
            if (x >= 0 && x < 200 && y >= 120 && y < 160) return 0;
            if (x >= 0 && x < 200 && y >= 168 && y < 208) return 1;
            if (x >= 0 && x < 200 && y >= 216 && y < 256) return 2;
            if (x >= 0 && x < 200 && y >= 264 && y < 304) return 3;

            if (_activeTab == 1) // Convert
            {
                if (x >= 236 && x < 386 && y >= 95 && y < 135) return 11;
                if (x >= 398 && x < 548 && y >= 95 && y < 135) return 12;
                if (x >= 560 && x < 710 && y >= 95 && y < 135) return 13;
                if (x >= 236 && x < 386 && y >= 145 && y < 185) return 14;
                if (x >= 398 && x < 548 && y >= 145 && y < 185) return 15;
                if (x >= 560 && x < 710 && y >= 145 && y < 185) return 16;

                if (_selectedFiles.Count > 0 && x >= 650 && x < 698 && y >= 217 && y < 239) return 19; // Clear button
                if (x >= 236 && x < 710 && y >= 205 && y < 325) return 17; // Drag & Drop zone
                if (_selectedFiles.Count > 0 && x >= 236 && x < 710 && y >= 340 && y < 376) return 18; // Start button
            }
            else if (_activeTab == 2) // History
            {
                // Clear history button
                if (x >= 630 && x < 720 && y >= 38 && y < 66) return 4;
            }
            else if (_activeTab == 3) // Settings
            {
                // Quiet Mode toggle
                if (x >= 660 && x < 704 && y >= 105 && y < 127) return 5;
                // Notification toggle
                if (x >= 660 && x < 704 && y >= 175 && y < 197) return 6;
                // OutputDir: Source
                if (x >= 236 && x < 346 && y >= 290 && y < 320) return 7;
                // OutputDir: Desktop
                if (x >= 356 && x < 431 && y >= 290 && y < 320) return 8;
                // OutputDir: Downloads
                if (x >= 441 && x < 516 && y >= 290 && y < 320) return 9;
                // Language dropdown button
                if (x >= 236 && x < 476 && y >= 390 && y < 420) return 10;
            }

            return -1;
        }

        static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            switch (msg)
            {
                case 0x0014: return (IntPtr)1; // WM_ERASEBKGND
                case 0x000F: // WM_PAINT
                    var ps = new PAINTSTRUCT();
                    var hdc = BeginPaint(hwnd, out ps);
                    Paint(hdc);
                    EndPaint(hwnd, ref ps);
                    return IntPtr.Zero;
                case 0x0200: // WM_MOUSEMOVE
                    {
                        int mouseX = (short)(l.ToInt64() & 0xFFFF);
                        int mouseY = (short)((l.ToInt64() >> 16) & 0xFFFF);
                        
                        if (_langDropdownOpen)
                        {
                            if (mouseX >= 236 && mouseX <= 476 && mouseY >= 248 && mouseY < 390)
                            {
                                int idx = (mouseY - 248) / 26;
                                var filtered = GetFilteredLanguages();
                                if (idx >= 0 && idx < filtered.Count && idx != _langHoveredIndex)
                                {
                                    _langHoveredIndex = idx;
                                    InvalidateRect(hwnd, IntPtr.Zero, false);
                                }
                            }
                        }

                        int prevHovered = _hoveredElement;
                        _hoveredElement = HitTest(mouseX, mouseY);
                        if (_hoveredElement != prevHovered)
                        {
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                    }
                    return IntPtr.Zero;
                case 0x0201: // WM_LBUTTONDOWN
                    {
                        int mouseX = (short)(l.ToInt64() & 0xFFFF);
                        int mouseY = (short)((l.ToInt64() >> 16) & 0xFFFF);

                        if (_langDropdownOpen)
                        {
                            if (mouseX >= 236 && mouseX <= 476)
                            {
                                if (mouseY >= 210 && mouseY < 248)
                                {
                                    // Clicked search box or container top, do nothing but keep open
                                    return IntPtr.Zero;
                                }
                                else if (mouseY >= 248 && mouseY < 390)
                                {
                                    int clickedIdx = (mouseY - 248) / 26;
                                    var filtered = GetFilteredLanguages();
                                    if (clickedIdx >= 0 && clickedIdx < filtered.Count)
                                    {
                                        SelectLanguage(filtered[clickedIdx].Code);
                                    }
                                    _langDropdownOpen = false;
                                    InvalidateRect(hwnd, IntPtr.Zero, false);
                                    return IntPtr.Zero;
                                }
                            }
                            
                            // Clicked outside dropdown
                            _langDropdownOpen = false;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                            return IntPtr.Zero;
                        }

                        int element = HitTest(mouseX, mouseY);
                        if (element >= 0 && element <= 3)
                        {
                            _activeTab = element;
                            if (_activeTab == 0 || _activeTab == 2)
                            {
                                RefreshHistoryData();
                            }
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 4) // Clear history
                        {
                            if (MessageBox(hwnd, GetText("history_clear_confirm"), "Clickra", 0x24) == 6) // MB_YESNO | MB_ICONQUESTION, 6 is IDYES
                            {
                                ClickraStorage.ClearHistory();
                                RefreshHistoryData();
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                        }
                        else if (element == 5) // Toggle Quiet Mode
                        {
                            bool current = ClickraStorage.GetSetting("QuietMode").Equals("true", StringComparison.OrdinalIgnoreCase);
                            ClickraStorage.SaveSetting("QuietMode", current ? "false" : "true");
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 6) // Toggle Notification
                        {
                            bool current = ClickraStorage.GetSetting("Notification").Equals("true", StringComparison.OrdinalIgnoreCase);
                            ClickraStorage.SaveSetting("Notification", current ? "false" : "true");
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 7) // OutputDir: source
                        {
                            ClickraStorage.SaveSetting("OutputDir", "source");
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 8) // OutputDir: desktop
                        {
                            ClickraStorage.SaveSetting("OutputDir", "desktop");
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 9) // OutputDir: downloads
                        {
                            ClickraStorage.SaveSetting("OutputDir", "downloads");
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 10) // Language dropdown button
                        {
                            _langDropdownOpen = !_langDropdownOpen;
                            if (_langDropdownOpen)
                            {
                                _langSearchQuery = "";
                                _langHoveredIndex = 0;
                            }
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element >= 11 && element <= 16) // Change convert tool
                        {
                            ChangeConvertCommand(element - 11);
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 17) // Drag & Drop Zone clicked (Browse files)
                        {
                            string cmd = ConvertCommands[_convertCommandIndex];
                            string filter = GetFilterForCommand(cmd);
                            string title = GetText("convert_drag_drop_hint");
                            var chosen = OpenFiles(hwnd, filter, title);
                            if (chosen.Count > 0)
                            {
                                _selectedFiles = chosen;
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                        }
                        else if (element == 18) // Start conversion button
                        {
                            RunConversion(hwnd);
                        }
                        else if (element == 19) // Clear files button
                        {
                            _selectedFiles.Clear();
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                    }
                    return IntPtr.Zero;
                case 0x0233: // WM_DROPFILES
                    {
                        IntPtr hDrop = w;
                        uint fileCount = DragQueryFileW(hDrop, 0xFFFFFFFF, IntPtr.Zero, 0);
                        var droppedFiles = new List<string>();
                        for (uint i = 0; i < fileCount; i++)
                        {
                            uint length = DragQueryFileW(hDrop, i, IntPtr.Zero, 0);
                            if (length > 0)
                            {
                                IntPtr buffer = Marshal.AllocHGlobal((int)(length + 1) * 2);
                                if (DragQueryFileW(hDrop, i, buffer, length + 1) > 0)
                                {
                                    string? file = Marshal.PtrToStringUni(buffer);
                                    if (!string.IsNullOrEmpty(file))
                                    {
                                        droppedFiles.Add(file);
                                    }
                                }
                                Marshal.FreeHGlobal(buffer);
                            }
                        }
                        DragFinish(hDrop);

                        if (droppedFiles.Count > 0)
                        {
                            HandleDroppedFiles(droppedFiles);
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                    }
                    return IntPtr.Zero;
                case 0x0102: // WM_CHAR
                    if (_langDropdownOpen)
                    {
                        char c = (char)w.ToInt32();
                        if (c == '\b') // Backspace
                        {
                            if (_langSearchQuery.Length > 0)
                            {
                                _langSearchQuery = _langSearchQuery.Substring(0, _langSearchQuery.Length - 1);
                                _langHoveredIndex = 0;
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                        }
                        else if (c == 27) // Escape
                        {
                            _langDropdownOpen = false;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (c == '\r') // Enter
                        {
                            var filtered = GetFilteredLanguages();
                            if (filtered.Count > 0 && _langHoveredIndex >= 0 && _langHoveredIndex < filtered.Count)
                            {
                                SelectLanguage(filtered[_langHoveredIndex].Code);
                            }
                            _langDropdownOpen = false;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (!char.IsControl(c))
                        {
                            _langSearchQuery += c;
                            _langHoveredIndex = 0;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        return IntPtr.Zero;
                    }
                    break;
                case 0x0100: // WM_KEYDOWN
                    if (_langDropdownOpen)
                    {
                        int key = w.ToInt32();
                        if (key == 0x1B) // VK_ESCAPE
                        {
                            _langDropdownOpen = false;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                            return IntPtr.Zero;
                        }
                        else if (key == 0x26) // VK_UP
                        {
                            var filtered = GetFilteredLanguages();
                            if (filtered.Count > 0)
                            {
                                _langHoveredIndex = (_langHoveredIndex - 1 + filtered.Count) % filtered.Count;
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                            return IntPtr.Zero;
                        }
                        else if (key == 0x28) // VK_DOWN
                        {
                            var filtered = GetFilteredLanguages();
                            if (filtered.Count > 0)
                            {
                                _langHoveredIndex = (_langHoveredIndex + 1) % filtered.Count;
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                            return IntPtr.Zero;
                        }
                    }
                    break;
                case 0x0020: // WM_SETCURSOR
                    if (_hoveredElement != -1 || _langDropdownOpen)
                    {
                        SetCursor(LoadCursorW(IntPtr.Zero, 32649)); // IDC_HAND = 32649
                        return (IntPtr)1; // Handled
                    }
                    break;
                case 0x0113: // WM_TIMER
                    if (w == TIMER_ID_REFRESH)
                    {
                        // 靜默刷新歷史（不重置捲動位置）
                        RefreshHistoryData();
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;
                case 0x0002: // WM_DESTROY
                    KillTimer(hwnd, TIMER_ID_REFRESH);
                    CleanupResources();
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, w, l);
        }

        static void Paint(IntPtr hdc)
        {
            if (_bufferBmp == null || _bufferGraphics == null) return;
            var g = _bufferGraphics;
            g.Clear(Color.FromArgb(32, 32, 32));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 1. Draw Sidebar
            using (var sidebarBrush = new SolidBrush(Color.FromArgb(24, 24, 24)))
            {
                g.FillRectangle(sidebarBrush, 0, 0, 200, 460);
            }
            using (var dividerPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(dividerPen, 200, 0, 200, 460);
            }

            // Draw Brand Title
            if (_titleFont != null)
                g.DrawString("Clickra", _titleFont, Brushes.White, 24, 30);
            if (_subFont != null)
            {
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string verStr = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "Unknown";
                using var verBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
                g.DrawString($"v{verStr} · Shell Suite", _subFont, verBrush, 26, 70);
            }

            // Brand Divider
            using (var brandDivPen = new Pen(Color.FromArgb(45, 45, 45)))
            {
                g.DrawLine(brandDivPen, 20, 95, 180, 95);
            }

            // Draw Tabs
            DrawTabButton(g, "\uE80F", GetText("tab_status"), 0, 120);
            DrawTabButton(g, "\uEC7E", GetText("tab_convert"), 1, 168);
            DrawTabButton(g, "\uE81C", GetText("tab_history"), 2, 216);
            DrawTabButton(g, "\uE713", GetText("tab_settings"), 3, 264);

            // 2. Draw Content Area
            if (_activeTab == 0)
            {
                DrawOverviewTab(g);
            }
            else if (_activeTab == 1)
            {
                DrawConvertTab(g);
            }
            else if (_activeTab == 2)
            {
                DrawHistoryTab(g);
            }
            else if (_activeTab == 3)
            {
                DrawSettingsTab(g);
            }

            // Draw double buffer to screen
            using var targetG = Graphics.FromHdc(hdc);
            targetG.DrawImage(_bufferBmp, 0, 0);
        }

        static void DrawTabButton(Graphics g, string icon, string label, int tabIndex, int y)
        {
            bool isActive = _activeTab == tabIndex;
            bool isHovered = _hoveredElement == tabIndex;

            if (isActive)
            {
                // Accent left border
                using var accentBrush = new SolidBrush(GetSystemColorizationColor());
                g.FillRectangle(accentBrush, 0, y + 4, 4, 32);

                // Subtle background for active tab
                using var activeBg = new SolidBrush(Color.FromArgb(36, 36, 36));
                g.FillRectangle(activeBg, 4, y, 196, 40);
            }
            else if (isHovered)
            {
                // Hover background
                using var hoverBg = new SolidBrush(Color.FromArgb(30, 30, 30));
                g.FillRectangle(hoverBg, 4, y, 196, 40);
            }

            Color textColor = isActive ? Color.White : (isHovered ? Color.FromArgb(220, 220, 220) : Color.FromArgb(150, 150, 150));
            using var textBrush = new SolidBrush(textColor);

            // Draw Icon (Segoe MDL2 Assets)
            if (_iconFont != null)
            {
                g.DrawString(icon, _iconFont, textBrush, 24, y + 12);
            }

            // Draw Label (Segoe UI)
            if (_tabFont != null)
            {
                g.DrawString(label, _tabFont, textBrush, 52, y + 10);
            }
        }

        static void DrawOverviewTab(Graphics g)
        {
            // Title
            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_status"), _contentTitleFont, Brushes.White, 236, 30);

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, 236, 75, 720, 75);
            }

            // Engine status
            if (_sectionFont != null)
                g.DrawString(GetText("overview_engine_status"), _sectionFont, Brushes.White, 236, 95);

            DrawEngineRow(g, GetText("engine_pdf"), true, 236, 125);
            DrawEngineRow(g, GetText("engine_ppt"), IsOfficeInstalled("PowerPoint"), 236, 165);
            DrawEngineRow(g, GetText("engine_word"), IsOfficeInstalled("Word"), 236, 205);

            // Statistics
            if (_sectionFont != null)
                g.DrawString(GetText("overview_stats"), _sectionFont, Brushes.White, 236, 260);

            // Draw Cards
            DrawStatCard(g, GetText("overview_stat_total"), _statTotal.ToString(), Color.FromArgb(200, 200, 200), 236, 290, 140);
            DrawStatCard(g, GetText("overview_stat_success"), _statSuccess.ToString(), Color.FromArgb(100, 220, 100), 396, 290, 140);
            DrawStatCard(g, GetText("overview_stat_failed"), _statFailed.ToString(), Color.FromArgb(255, 90, 70), 556, 290, 140);
            
            // Footer tips
            if (_subFont != null)
            {
                using var tipBrush = new SolidBrush(Color.FromArgb(100, 100, 100));
                g.DrawString(GetText("overview_tip"), _subFont, tipBrush, 236, 400);
            }
        }

        static void DrawHistoryTab(Graphics g)
        {
            // Title
            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_history"), _contentTitleFont, Brushes.White, 236, 30);

            // Clear button
            bool isClearHovered = _hoveredElement == 3;
            Color btnBg = isClearHovered ? Color.FromArgb(70, 70, 70) : Color.FromArgb(50, 50, 50);
            Color btnBorder = isClearHovered ? Color.FromArgb(90, 90, 90) : Color.FromArgb(70, 70, 70);
            using (var btnBgBrush = new SolidBrush(btnBg))
            using (var btnBorderPen = new Pen(btnBorder))
            using (var path = GetRoundedRectPath(new RectangleF(630, 38, 90, 28), 4))
            {
                g.FillPath(btnBgBrush, path);
                g.DrawPath(btnBorderPen, path);
            }
            if (_subFont != null)
            {
                Color btnText = isClearHovered ? Color.White : Color.FromArgb(200, 200, 200);
                using var btnTextBrush = new SolidBrush(btnText);
                var size = g.MeasureString(GetText("history_clear"), _subFont);
                g.DrawString(GetText("history_clear"), _subFont, btnTextBrush, 630 + (90 - size.Width) / 2, 38 + (28 - size.Height) / 2);
            }

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, 236, 75, 720, 75);
            }

            // 取得目前進行中作業（若有）
            var activeEntry = ClickraStorage.GetActiveEntry();

            int rowStartY = 90;
            int rowSpacing = 52;
            int drawnRows = 0;

            // ——— 顧示進行中作業（置頂）———
            if (activeEntry.HasValue)
            {
                var ae = activeEntry.Value;
                int rowY = rowStartY;
                int rowW = 484, rowH = 44;

                // 進行中作業背景素微醒目（深藍色調）
                Color activeBgColor = ae.Status switch
                {
                    ConversionStatus.Pending    => Color.FromArgb(38, 38, 48),
                    ConversionStatus.InProgress => Color.FromArgb(30, 42, 55),
                    ConversionStatus.Success    => Color.FromArgb(30, 44, 34),
                    ConversionStatus.Failed     => Color.FromArgb(50, 32, 32),
                    _                           => Color.FromArgb(36, 36, 36)
                };
                Color activeBorderColor = ae.Status switch
                {
                    ConversionStatus.Pending    => Color.FromArgb(70, 70, 100),
                    ConversionStatus.InProgress => Color.FromArgb(0, 120, 212),
                    ConversionStatus.Success    => Color.FromArgb(50, 160, 80),
                    ConversionStatus.Failed     => Color.FromArgb(200, 60, 60),
                    _                           => Color.FromArgb(60, 60, 60)
                };

                using var path = GetRoundedRectPath(new RectangleF(236, rowY, rowW, rowH), 6);
                using var rowBg = new SolidBrush(activeBgColor);
                g.FillPath(rowBg, path);
                using var borderPen = new Pen(activeBorderColor);
                g.DrawPath(borderPen, path);

                // 時間
                if (_bodyFont != null)
                {
                    using var timeBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    g.DrawString(ae.Time, _bodyFont, timeBrush, 248, rowY + 13);
                }

                // 指令標籤
                DrawCommandTag(g, ae.Command, 380, rowY + 11);

                // 檔案數
                if (_bodyFont != null)
                {
                    using var countBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                    g.DrawString($"{ae.FileCount} {GetText("label_files")}", _bodyFont, countBrush, 470, rowY + 13);
                }

                // 狀態標籤
                string statusText;
                Color statusColor;
                switch (ae.Status)
                {
                    case ConversionStatus.Pending:
                        statusText = GetText("status_pending");
                        statusColor = Color.FromArgb(180, 180, 100);
                        break;
                    case ConversionStatus.InProgress:
                        statusText = GetText("status_converting");
                        statusColor = Color.FromArgb(80, 160, 240);
                        break;
                    case ConversionStatus.Success:
                        statusText = GetText("status_success");
                        statusColor = Color.FromArgb(100, 220, 100);
                        break;
                    case ConversionStatus.Failed:
                        statusText = GetText("status_failed");
                        statusColor = Color.FromArgb(255, 90, 70);
                        break;
                    default:
                        statusText = "";
                        statusColor = Color.Gray;
                        break;
                }
                if (_tagFont != null)
                {
                    using var statusBrush = new SolidBrush(statusColor);
                    g.DrawString(statusText, _tagFont, statusBrush, 550, rowY + 13);
                }

                // 錯誤訊息（失敗時）
                if (ae.Status == ConversionStatus.Failed && !string.IsNullOrEmpty(ae.ErrorMessage))
                {
                    if (_subFont != null)
                    {
                        using var errBrush = new SolidBrush(Color.FromArgb(230, 90, 70));
                        string errText = ae.ErrorMessage.Length > 14 ? ae.ErrorMessage.Substring(0, 14) + "..." : ae.ErrorMessage;
                        g.DrawString(errText, _subFont, errBrush, 590, rowY + 14);
                    }
                }

                drawnRows++;
            }

            // ——— 顧示持久化歷史紀錄———
            if (_historyEntries == null || _historyEntries.Count == 0)
            {
                if (drawnRows == 0 && _tabFont != null)
                {
                    using var textBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
                    g.DrawString(GetText("history_empty"), _tabFont, textBrush, 236, rowStartY + rowSpacing * drawnRows + 10);
                }
                return;
            }

            // 可展示的動態紀錄最多類國（進行中占一行）
            int maxHistoryRows = 6 - drawnRows;
            int limit = Math.Min(maxHistoryRows, _historyEntries.Count);
            for (int i = 0; i < limit; i++)
            {
                var entry = _historyEntries[i];
                int rowY = rowStartY + (drawnRows + i) * rowSpacing;
                int rowW = 484, rowH = 44;

                using var path = GetRoundedRectPath(new RectangleF(236, rowY, rowW, rowH), 6);
                using var rowBg = new SolidBrush(Color.FromArgb(36, 36, 36));
                g.FillPath(rowBg, path);

                using var borderPen = new Pen(Color.FromArgb(48, 48, 48));
                g.DrawPath(borderPen, path);

                // 時間
                if (_bodyFont != null)
                {
                    using var timeBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    g.DrawString(entry.Time, _bodyFont, timeBrush, 248, rowY + 13);
                }

                // 指令標籤
                DrawCommandTag(g, entry.Command, 380, rowY + 11);

                // 檔案數
                if (_bodyFont != null)
                {
                    using var countBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                    g.DrawString($"{entry.FileCount} {GetText("label_files")}", _bodyFont, countBrush, 470, rowY + 13);
                }

                // 狀態標籤
                Color statusColor = entry.IsSuccess ? Color.FromArgb(100, 220, 100) : Color.FromArgb(255, 90, 70);
                string statusText = entry.IsSuccess ? GetText("status_success") : GetText("status_failed");
                if (_tagFont != null)
                {
                    using var statusBrush = new SolidBrush(statusColor);
                    g.DrawString(statusText, _tagFont, statusBrush, 550, rowY + 13);
                }

                // 錯誤訊息（失敗時）
                if (!entry.IsSuccess && !string.IsNullOrEmpty(entry.ErrorMessage))
                {
                    if (_subFont != null)
                    {
                        using var errBrush = new SolidBrush(Color.FromArgb(230, 90, 70));
                        string errText = entry.ErrorMessage.Length > 14 ? entry.ErrorMessage.Substring(0, 14) + "..." : entry.ErrorMessage;
                        g.DrawString(errText, _subFont, errBrush, 590, rowY + 14);
                    }
                }
            }
        }

        static void DrawCommandTag(Graphics g, string command, int x, int y)
        {
            Color tagBg;
            string text = command;
            switch (command.ToLowerInvariant())
            {
                case "word2pdf":
                    tagBg = Color.FromArgb(0, 120, 212);
                    text = GetText("cmd_word_to_pdf");
                    break;
                case "ppt2pdf":
                    tagBg = Color.FromArgb(180, 50, 30);
                    text = GetText("cmd_ppt_to_pdf");
                    break;
                case "merge-pdf":
                    tagBg = Color.FromArgb(16, 124, 65);
                    text = GetText("cmd_merge_pdf");
                    break;
                case "img2pdf":
                    tagBg = Color.FromArgb(100, 60, 180);
                    text = GetText("cmd_img_to_pdf");
                    break;
                case "img-merge":
                    tagBg = Color.FromArgb(0, 130, 135);
                    text = GetText("cmd_merge_img");
                    break;
                case "img-stitch":
                    tagBg = Color.FromArgb(216, 59, 1);
                    text = GetText("cmd_stitch_img");
                    break;
                default:
                    tagBg = Color.FromArgb(100, 100, 100);
                    break;
            }

            int w = 82;
            int h = 22;
            using var path = GetRoundedRectPath(new RectangleF(x, y, w, h), 4);
            using var brush = new SolidBrush(tagBg);
            g.FillPath(brush, path);

            if (_tagFont != null)
            {
                var size = g.MeasureString(text, _tagFont);
                g.DrawString(text, _tagFont, Brushes.White, x + (w - size.Width) / 2, y + (h - size.Height) / 2);
            }
        }

        static void DrawSettingsTab(Graphics g)
        {
            // Title
            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_settings"), _contentTitleFont, Brushes.White, 236, 30);

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, 236, 75, 720, 75);
            }

            // Quiet mode setting
            bool quietMode = ClickraStorage.GetSetting("QuietMode").Equals("true", StringComparison.OrdinalIgnoreCase);
            bool isQuietHovered = _hoveredElement == 4;
            if (_tabFont != null)
                g.DrawString(GetText("setting_silent_title"), _tabFont, Brushes.White, 236, 100);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(GetText("setting_silent_desc"), _subFont, subBrush, 236, 122);
            }
            DrawToggleSwitch(g, quietMode, isQuietHovered, 660, 105, 44, 22);

            // Notification setting
            bool notification = ClickraStorage.GetSetting("Notification").Equals("true", StringComparison.OrdinalIgnoreCase);
            bool isNotifHovered = _hoveredElement == 5;
            if (_tabFont != null)
                g.DrawString(GetText("setting_notify_title"), _tabFont, Brushes.White, 236, 170);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(GetText("setting_notify_desc"), _subFont, subBrush, 236, 192);
            }
            DrawToggleSwitch(g, notification, isNotifHovered, 660, 175, 44, 22);

            // Output path setting
            if (_tabFont != null)
                g.DrawString(GetText("setting_output_title"), _tabFont, Brushes.White, 236, 240);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(GetText("setting_output_desc"), _subFont, subBrush, 236, 262);
            }

            string outputDirMode = ClickraStorage.GetSetting("OutputDir");
            DrawOutputDirButton(g, GetText("setting_output_same_as_source"), outputDirMode.Equals("source", StringComparison.OrdinalIgnoreCase), 7, 236, 290, 110);
            DrawOutputDirButton(g, GetText("setting_output_desktop"), outputDirMode.Equals("desktop", StringComparison.OrdinalIgnoreCase), 8, 356, 290, 75);
            DrawOutputDirButton(g, GetText("setting_output_downloads"), outputDirMode.Equals("downloads", StringComparison.OrdinalIgnoreCase), 9, 441, 290, 75);

            // Language setting UI block
            if (_tabFont != null)
                g.DrawString(GetText("setting_lang_title"), _tabFont, Brushes.White, 236, 340);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(GetText("setting_lang_desc"), _subFont, subBrush, 236, 362);
            }

            // Draw Dropdown Selector
            DrawLanguageDropdown(g);
        }

        static void DrawToggleSwitch(Graphics g, bool state, bool hovered, int x, int y, int w, int h)
        {
            // Track
            Color trackColor = state ? GetSystemColorizationColor() : Color.FromArgb(60, 60, 60);
            if (hovered)
            {
                trackColor = state ? Lighten(trackColor, 0.15f) : Color.FromArgb(80, 80, 80);
            }
            using var trackBrush = new SolidBrush(trackColor);
            using var path = GetRoundedRectPath(new RectangleF(x, y, w, h), h / 2f);
            g.FillPath(trackBrush, path);

            // Thumb
            int thumbMargin = 2;
            int thumbSize = h - thumbMargin * 2;
            int thumbX = state ? (x + w - thumbSize - thumbMargin) : (x + thumbMargin);
            using var thumbBrush = new SolidBrush(Color.White);
            g.FillEllipse(thumbBrush, thumbX, y + thumbMargin, thumbSize, thumbSize);
        }

        static void DrawOutputDirButton(Graphics g, string text, bool selected, int elementId, int x, int y, int w)
        {
            bool isHovered = _hoveredElement == elementId;
            Color btnBg;
            Color btnBorder;
            Color textColor;

            if (selected)
            {
                btnBg = GetSystemColorizationColor();
                if (isHovered) btnBg = Lighten(btnBg, 0.15f);
                btnBorder = btnBg;
                textColor = Color.White;
            }
            else
            {
                btnBg = isHovered ? Color.FromArgb(55, 55, 55) : Color.FromArgb(40, 40, 40);
                btnBorder = isHovered ? Color.FromArgb(80, 80, 80) : Color.FromArgb(60, 60, 60);
                textColor = isHovered ? Color.White : Color.FromArgb(200, 200, 200);
            }

            int h = 30;
            using var path = GetRoundedRectPath(new RectangleF(x, y, w, h), 4);
            using var bgBrush = new SolidBrush(btnBg);
            g.FillPath(bgBrush, path);

            using var borderPen = new Pen(btnBorder);
            g.DrawPath(borderPen, path);

            if (_subFont != null)
            {
                using var textBrush = new SolidBrush(textColor);
                var size = g.MeasureString(text, _subFont);
                g.DrawString(text, _subFont, textBrush, x + (w - size.Width) / 2, y + (h - size.Height) / 2);
            }
        }

        static void DrawEngineRow(Graphics g, string label, bool ok, int x, int y)
        {
            Color dotColor = ok ? Color.FromArgb(100, 220, 100) : Color.FromArgb(255, 90, 70);
            using var dotBrush = new SolidBrush(dotColor);
            g.FillEllipse(dotBrush, x, y + 4, 10, 10);

            using var textBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
            if (_tabFont != null)
            {
                string statusText = ok ? GetText("engine_ready") : GetText("engine_office_not_installed");
                g.DrawString($"{label}:  ", _tabFont, textBrush, x + 20, y);
                
                using var statusBrush = new SolidBrush(dotColor);
                var labelSize = g.MeasureString($"{label}:  ", _tabFont);
                g.DrawString(statusText, _tabFont, statusBrush, x + 20 + labelSize.Width, y);
            }
        }

        static void DrawStatCard(Graphics g, string title, string val, Color valColor, int x, int y, int w)
        {
            int h = 70;
            using var path = GetRoundedRectPath(new RectangleF(x, y, w, h), 6);
            using var cardBg = new SolidBrush(Color.FromArgb(40, 40, 40));
            g.FillPath(cardBg, path);

            using var borderPen = new Pen(Color.FromArgb(55, 55, 55));
            g.DrawPath(borderPen, path);

            if (_subFont != null)
            {
                using var titleBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(title, _subFont, titleBrush, x + 12, y + 10);
            }

            if (_sectionFont != null)
            {
                using var valBrush = new SolidBrush(valColor);
                g.DrawString(val, _sectionFont, valBrush, x + 12, y + 32);
            }
        }

        static GraphicsPath GetRoundedRectPath(RectangleF rect, float radius)
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

        static Color GetSystemColorizationColor()
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
                _cachedColorizationColor = Color.FromArgb(255, 0, 120, 212); // Microsoft Blue
                _hasCachedColorizationColor = true;
                return _cachedColorizationColor;
            }
        }

        static Color Lighten(Color c, float amount)
        {
            int r = (int)(c.R + (255 - c.R) * amount);
            int g = (int)(c.G + (255 - c.G) * amount);
            int b = (int)(c.B + (255 - c.B) * amount);
            return Color.FromArgb(255, Math.Min(255, r), Math.Min(255, g), Math.Min(255, b));
        }

        static bool IsOfficeInstalled(string app)
        {
            string progId = app == "PowerPoint" ? "PowerPoint.Application" : "Word.Application";
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Classes\{progId}");
                return key != null;
            }
            catch { return false; }
        }

        static void DrawLanguageDropdown(Graphics g)
        {
            string currentLangCode = ClickraStorage.GetSetting("Language");
            if (string.IsNullOrEmpty(currentLangCode))
            {
                currentLangCode = System.Globalization.CultureInfo.CurrentUICulture.Name;
            }
            
            var currentLang = SupportedLanguages.FirstOrDefault(l => l.Code.Equals(currentLangCode, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(currentLang.Code))
            {
                currentLang = SupportedLanguages[0]; // Default to Traditional Chinese
            }

            string displayText = $"{currentLang.NativeName} ({currentLang.EnglishName})";
            bool isHovered = _hoveredElement == 10;

            int x = 236, y = 390, w = 240, h = 30;

            Color btnBg = isHovered ? Color.FromArgb(55, 55, 55) : Color.FromArgb(40, 40, 40);
            Color btnBorder = _langDropdownOpen ? GetSystemColorizationColor() : (isHovered ? Color.FromArgb(80, 80, 80) : Color.FromArgb(60, 60, 60));
            Color textColor = Color.FromArgb(220, 220, 220);

            // Draw button base
            using (var path = GetRoundedRectPath(new RectangleF(x, y, w, h), 4))
            using (var bgBrush = new SolidBrush(btnBg))
            using (var borderPen = new Pen(btnBorder, _langDropdownOpen ? 1.5f : 1f))
            {
                g.FillPath(bgBrush, path);
                g.DrawPath(borderPen, path);
            }

            // Draw selected language text
            if (_subFont != null)
            {
                using var textBrush = new SolidBrush(textColor);
                g.DrawString(displayText, _subFont, textBrush, x + 10, y + 7);
            }

            // Draw Chevron Down icon
            if (_iconFont != null)
            {
                using var iconBrush = new SolidBrush(Color.FromArgb(160, 160, 160));
                g.DrawString("\uE70D", _iconFont, iconBrush, x + w - 24, y + 9);
            }

            // Draw overlay popup list if open
            if (_langDropdownOpen)
            {
                int popupH = 180;
                int popupY = y - popupH; // 210

                // Container path
                using (var path = GetRoundedRectPath(new RectangleF(x, popupY, w, popupH), 4))
                using (var bgBrush = new SolidBrush(Color.FromArgb(28, 28, 28)))
                using (var borderPen = new Pen(Color.FromArgb(60, 60, 60)))
                {
                    g.FillPath(bgBrush, path);
                    g.DrawPath(borderPen, path);
                }

                // Search input box: y = 216
                int searchX = x + 6, searchY = popupY + 6, searchW = w - 12, searchH = 26;
                using (var searchPath = GetRoundedRectPath(new RectangleF(searchX, searchY, searchW, searchH), 4))
                using (var searchBg = new SolidBrush(Color.FromArgb(45, 45, 45)))
                using (var searchBorder = new Pen(Color.FromArgb(75, 75, 75)))
                {
                    g.FillPath(searchBg, searchPath);
                    g.DrawPath(searchBorder, searchPath);
                }

                // Draw Search Icon
                if (_iconFont != null)
                {
                    using var searchIconBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    g.DrawString("\uE721", _iconFont, searchIconBrush, searchX + 8, searchY + 7);
                }

                // Draw Search Text or Placeholder
                if (_subFont != null)
                {
                    if (string.IsNullOrEmpty(_langSearchQuery))
                    {
                        using var placeholderBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
                        g.DrawString(GetText("search_lang_placeholder"), _subFont, placeholderBrush, searchX + 26, searchY + 6);
                    }
                    else
                    {
                        using var queryBrush = new SolidBrush(Color.White);
                        g.DrawString(_langSearchQuery, _subFont, queryBrush, searchX + 26, searchY + 6);
                    }

                    // Draw flashing cursor (caret)
                    if ((DateTime.Now.Millisecond / 500) % 2 == 0)
                    {
                        var size = g.MeasureString(_langSearchQuery, _subFont);
                        using var cursorBrush = new SolidBrush(Color.White);
                        g.FillRectangle(cursorBrush, searchX + 26 + size.Width, searchY + 6, 1.5f, 13);
                    }
                }

                // Draw filtered list
                var filtered = GetFilteredLanguages();
                int listStartY = searchY + searchH + 6; // 248

                for (int i = 0; i < Math.Min(5, filtered.Count); i++)
                {
                    var item = filtered[i];
                    int itemY = listStartY + i * 26;
                    int itemH = 24;

                    bool isItemHovered = _langHoveredIndex == i;
                    Color itemBg = isItemHovered ? GetSystemColorizationColor() : Color.Transparent;
                    Color itemTextCol = isItemHovered ? Color.White : Color.FromArgb(200, 200, 200);

                    if (isItemHovered)
                    {
                        using (var itemPath = GetRoundedRectPath(new RectangleF(x + 4, itemY, w - 8, itemH), 3))
                        using (var itemBgBrush = new SolidBrush(itemBg))
                        {
                            g.FillPath(itemBgBrush, itemPath);
                        }
                    }

                    if (_subFont != null)
                    {
                        using var itemTextBrush = new SolidBrush(itemTextCol);
                        g.DrawString($"{item.NativeName} ({item.EnglishName})", _subFont, itemTextBrush, x + 10, itemY + 5);
                    }
                }
            }
        }

        static List<(string Code, string NativeName, string EnglishName)> GetFilteredLanguages()
        {
            if (string.IsNullOrEmpty(_langSearchQuery))
            {
                return SupportedLanguages;
            }
            return SupportedLanguages.Where(l =>
                l.NativeName.Contains(_langSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                l.EnglishName.Contains(_langSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                l.Code.Contains(_langSearchQuery, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        static void SelectLanguage(string code)
        {
            ClickraStorage.SaveSetting("Language", code);
        }

        static string GetText(string key)
        {
            return Clickra.Core.Localization.T(key, ClickraStorage.GetSetting("Language"));
        }

        static List<string> OpenFiles(IntPtr hwndOwner, string filter, string title)
        {
            var files = new List<string>();
            var ofn = new OPENFILENAME();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.hwndOwner = hwndOwner;
            ofn.lpstrFilter = filter;
            
            int maxFile = 65536;
            IntPtr fileBuffer = Marshal.AllocHGlobal(maxFile * 2);
            byte[] zeros = new byte[maxFile * 2];
            Marshal.Copy(zeros, 0, fileBuffer, zeros.Length);
            
            ofn.lpstrFile = fileBuffer;
            ofn.nMaxFile = maxFile;
            ofn.lpstrTitle = title;
            ofn.Flags = 0x00080000 | 0x00000200 | 0x00001000 | 0x00000004;

            if (GetOpenFileName(ref ofn))
            {
                var paths = new List<string>();
                IntPtr currentPtr = fileBuffer;
                while (true)
                {
                    string? s = Marshal.PtrToStringUni(currentPtr);
                    if (string.IsNullOrEmpty(s)) break;
                    paths.Add(s);
                    currentPtr += (s.Length + 1) * 2;
                }

                if (paths.Count > 0)
                {
                    if (paths.Count == 1)
                    {
                        files.Add(paths[0]);
                    }
                    else
                    {
                        string dir = paths[0];
                        for (int i = 1; i < paths.Count; i++)
                        {
                            files.Add(Path.Combine(dir, paths[i]));
                        }
                    }
                }
            }
            Marshal.FreeHGlobal(fileBuffer);
            return files;
        }

        static bool ValidateConvertFiles(string cmd, List<string> files, out string errorMsg)
        {
            errorMsg = "";
            if (files.Count == 0) return true;

            string[] allowed = cmd switch
            {
                "ppt2pdf" => new[] { ".ppt", ".pptx" },
                "word2pdf" => new[] { ".doc", ".docx" },
                "merge-pdf" => new[] { ".pdf" },
                "img2pdf" or "img-merge" or "img-stitch" => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" },
                _ => Array.Empty<string>()
            };

            var invalid = files.Where(f => !allowed.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
            if (invalid.Count > 0)
            {
                errorMsg = GetText("convert_err_invalid_ext");
                return false;
            }

            int minFiles = cmd switch
            {
                "merge-pdf" or "img-merge" or "img-stitch" => 2,
                _ => 1
            };

            if (files.Count < minFiles)
            {
                errorMsg = string.Format(GetText("convert_err_min_files"), minFiles);
                return false;
            }

            return true;
        }

        static void HandleDroppedFiles(List<string> files)
        {
            var extensions = files.Select(f => Path.GetExtension(f).ToLowerInvariant()).Distinct().ToList();
            if (extensions.Count == 0) return;

            if (extensions.All(ext => ext == ".ppt" || ext == ".pptx"))
            {
                ChangeConvertCommand(0);
            }
            else if (extensions.All(ext => ext == ".doc" || ext == ".docx"))
            {
                ChangeConvertCommand(1);
            }
            else if (extensions.All(ext => ext == ".pdf"))
            {
                ChangeConvertCommand(2);
            }
            else if (extensions.All(ext => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" }.Contains(ext)))
            {
                ChangeConvertCommand(files.Count > 1 ? 4 : 3);
            }

            _selectedFiles = files;
        }

        static void ChangeConvertCommand(int index)
        {
            _convertCommandIndex = index;
            string cmd = ConvertCommands[index];
            if (_selectedFiles.Count > 0)
            {
                if (!ValidateConvertFiles(cmd, _selectedFiles, out _))
                {
                    _selectedFiles.Clear();
                }
            }
        }

        static void RunConversion(IntPtr hwnd)
        {
            string cmd = ConvertCommands[_convertCommandIndex];
            if (_selectedFiles.Count == 0) return;

            if (!ValidateConvertFiles(cmd, _selectedFiles, out string error))
            {
                MessageBox(hwnd, error, "Clickra", 0x30);
                return;
            }

            var filesCopy = new List<string>(_selectedFiles);
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    ProgressWindow.Show(cmd, filesCopy);
                }
                catch (Exception ex)
                {
                    MessageBox(IntPtr.Zero, $"Execution failed: {ex.Message}", "Clickra", 0x10);
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();

            _selectedFiles.Clear();

            _activeTab = 2; // Switch to History
            RefreshHistoryData();
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        static string GetFilterForCommand(string cmd)
        {
            return cmd switch
            {
                "ppt2pdf" => "PowerPoint Files (*.ppt; *.pptx)\0*.ppt;*.pptx\0All Files (*.*)\0*.*\0\0",
                "word2pdf" => "Word Files (*.doc; *.docx)\0*.doc;*.docx\0All Files (*.*)\0*.*\0\0",
                "merge-pdf" => "PDF Files (*.pdf)\0*.pdf\0All Files (*.*)\0*.*\0\0",
                _ => "Image Files (*.jpg; *.jpeg; *.png; *.bmp; *.gif; *.tiff; *.webp)\0*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp\0All Files (*.*)\0*.*\0\0"
            };
        }

        static void DrawConvertTab(Graphics g)
        {
            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_convert"), _contentTitleFont, Brushes.White, 236, 30);

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, 236, 75, 720, 75);
            }

            for (int i = 0; i < 6; i++)
            {
                int col = i % 3;
                int row = i / 3;
                int cardX = 236 + col * 162;
                int cardY = 95 + row * 50;
                int cardW = 150;
                int cardH = 40;

                bool isSelected = _convertCommandIndex == i;
                bool isHovered = _hoveredElement == (11 + i);

                Color cardBg;
                Color cardBorder;
                Color textColor;

                if (isSelected)
                {
                    cardBg = Color.FromArgb(45, 45, 55);
                    cardBorder = GetSystemColorizationColor();
                    textColor = Color.White;
                }
                else
                {
                    cardBg = isHovered ? Color.FromArgb(50, 50, 50) : Color.FromArgb(36, 36, 36);
                    cardBorder = isHovered ? Color.FromArgb(80, 80, 80) : Color.FromArgb(48, 48, 48);
                    textColor = isHovered ? Color.White : Color.FromArgb(200, 200, 200);
                }

                using var path = GetRoundedRectPath(new RectangleF(cardX, cardY, cardW, cardH), 5);
                using var bgBrush = new SolidBrush(cardBg);
                using var borderPen = new Pen(cardBorder, isSelected ? 1.5f : 1f);
                g.FillPath(bgBrush, path);
                g.DrawPath(borderPen, path);

                string cmdKey = i switch
                {
                    0 => "cmd_ppt_to_pdf",
                    1 => "cmd_word_to_pdf",
                    2 => "cmd_merge_pdf",
                    3 => "cmd_img_to_pdf",
                    4 => "cmd_merge_img",
                    5 => "cmd_stitch_img",
                    _ => ""
                };
                string cmdText = GetText(cmdKey);
                
                if (_tabFont != null)
                {
                    using var textBrush = new SolidBrush(textColor);
                    var size = g.MeasureString(cmdText, _tabFont);
                    g.DrawString(cmdText, _tabFont, textBrush, cardX + (cardW - size.Width) / 2, cardY + (cardH - size.Height) / 2);
                }
            }

            int zoneX = 236, zoneY = 205, zoneW = 474, zoneH = 120;
            bool isZoneHovered = _hoveredElement == 17;
            Color zoneBg = isZoneHovered ? Color.FromArgb(42, 42, 42) : Color.FromArgb(34, 34, 34);
            Color zoneBorder = isZoneHovered ? GetSystemColorizationColor() : Color.FromArgb(60, 60, 60);

            using (var path = GetRoundedRectPath(new RectangleF(zoneX, zoneY, zoneW, zoneH), 6))
            using (var bgBrush = new SolidBrush(zoneBg))
            using (var borderPen = new Pen(zoneBorder, 1.5f))
            {
                borderPen.DashStyle = DashStyle.Dash;
                g.FillPath(bgBrush, path);
                g.DrawPath(borderPen, path);
            }

            if (_selectedFiles.Count == 0)
            {
                if (_iconFont != null)
                {
                    using var iconBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    g.DrawString("\uE118", _iconFont, iconBrush, zoneX + (zoneW - 20) / 2, zoneY + 25);
                }

                if (_tabFont != null)
                {
                    string hint = GetText("convert_drag_drop_hint");
                    using var textBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
                    var size = g.MeasureString(hint, _tabFont);
                    g.DrawString(hint, _tabFont, textBrush, zoneX + (zoneW - size.Width) / 2, zoneY + 55);
                }

                if (_subFont != null)
                {
                    string subHint = GetText("convert_drag_drop_sub");
                    using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    var size = g.MeasureString(subHint, _subFont);
                    g.DrawString(subHint, _subFont, subBrush, zoneX + (zoneW - size.Width) / 2, zoneY + 80);
                }
            }
            else
            {
                if (_tabFont != null)
                {
                    string summary = string.Format(GetText("convert_selected_count"), _selectedFiles.Count);
                    using var textBrush = new SolidBrush(Color.FromArgb(100, 220, 100));
                    g.DrawString(summary, _tabFont, textBrush, zoneX + 20, zoneY + 20);
                }

                if (_subFont != null)
                {
                    using var listBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
                    string joinedNames = string.Join(", ", _selectedFiles.Select(Path.GetFileName));
                    if (joinedNames.Length > 85)
                    {
                        joinedNames = joinedNames.Substring(0, 82) + "...";
                    }
                    g.DrawString(joinedNames, _subFont, listBrush, zoneX + 20, zoneY + 50);

                    string outDirMode = ClickraStorage.GetSetting("OutputDir");
                    string outPathDesc = outDirMode.ToLowerInvariant() switch
                    {
                        "desktop" => GetText("setting_output_desktop"),
                        "downloads" => GetText("setting_output_downloads"),
                        _ => GetText("setting_output_same_as_source")
                    };
                    using var descBrush = new SolidBrush(Color.FromArgb(130, 130, 130));
                    g.DrawString($"{GetText("setting_output_title")}: {outPathDesc}", _subFont, descBrush, zoneX + 20, zoneY + 85);
                }

                bool isClearHovered = _hoveredElement == 19;
                Color clearBtnBg = isClearHovered ? Color.FromArgb(60, 60, 60) : Color.FromArgb(45, 45, 45);
                Color clearBtnBorder = isClearHovered ? Color.FromArgb(80, 80, 80) : Color.FromArgb(55, 55, 55);
                using (var path = GetRoundedRectPath(new RectangleF(650, zoneY + 12, 48, 22), 3))
                using (var bgBrush = new SolidBrush(clearBtnBg))
                using (var borderPen = new Pen(clearBtnBorder))
                {
                    g.FillPath(bgBrush, path);
                    g.DrawPath(borderPen, path);
                }
                if (_subFont != null)
                {
                    Color btnText = isClearHovered ? Color.White : Color.FromArgb(180, 180, 180);
                    using var textBrush = new SolidBrush(btnText);
                    string clearText = GetText("convert_clear");
                    var size = g.MeasureString(clearText, _subFont);
                    g.DrawString(clearText, _subFont, textBrush, 650 + (48 - size.Width) / 2, zoneY + 12 + (22 - size.Height) / 2);
                }
            }

            if (_selectedFiles.Count > 0)
            {
                bool isBtnHovered = _hoveredElement == 18;
                Color btnBg = GetSystemColorizationColor();
                if (isBtnHovered) btnBg = Lighten(btnBg, 0.15f);

                using (var path = GetRoundedRectPath(new RectangleF(zoneX, 340, zoneW, 36), 5))
                using (var bgBrush = new SolidBrush(btnBg))
                {
                    g.FillPath(bgBrush, path);
                }

                if (_tabFont != null)
                {
                    string btnText = GetText("convert_start");
                    using var textBrush = new SolidBrush(Color.White);
                    var size = g.MeasureString(btnText, _tabFont);
                    g.DrawString(btnText, _tabFont, textBrush, zoneX + (zoneW - size.Width) / 2, 340 + (36 - size.Height) / 2);
                }
            }
        }
    }
}
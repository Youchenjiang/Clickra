using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Text;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using Clickra.Core;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
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
    }
}

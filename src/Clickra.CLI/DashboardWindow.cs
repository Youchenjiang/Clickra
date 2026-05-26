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
        static int _historyScrollOffset = 0;
        static int _langScrollOffset = 0;
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


        // Extra UI State Variables for v3.0.9
        static float _dpiScale = 1.0f;
        static int _expandedHistoryIndex = -1;
        static NOTIFYICONDATAW _nid;
        static bool _trayIconAdded = false;
        static float _aboutBtnY = 365;
        static float _githubBtnY = 240;
        static int _langDropdownY = 390;
        static float _sidebarWidth = 170f;
        static IntPtr _hIcon = IntPtr.Zero;
        static float _wSource = 110f;
        static float _wDesktop = 65f;
        static float _wDownloads = 80f;
        static float _wCustom = 100f;
        static float _wGit = 160f;
        static float _wGmail = 160f;

        // Content Area Scroll State
        static float _contentScrollX = 0;
        static float _contentScrollY = 0;
        static bool _isDraggingScrollX = false;
        static bool _isDraggingScrollY = false;
        static float _dragStartMouseX = 0;
        static float _dragStartMouseY = 0;
        static float _dragStartScrollX = 0;
        static float _dragStartScrollY = 0;

        static int GetClientWidth(IntPtr hwnd)
        {
            if (GetClientRect(hwnd, out var rect))
                return rect.right - rect.left;
            return 760;
        }

        static int GetClientHeight(IntPtr hwnd)
        {
            if (GetClientRect(hwnd, out var rect))
                return rect.bottom - rect.top;
            return 460;
        }

        static float LogicalWidth(IntPtr hwnd) => GetClientWidth(hwnd) / _dpiScale;
        static float LogicalHeight(IntPtr hwnd) => GetClientHeight(hwnd) / _dpiScale;

        public static float GetSidebarWidth(float logW)
        {
            return _sidebarWidth;
        }

        public static float GetContentX(float logW)
        {
            return _sidebarWidth + 30f;
        }

        static float GetContentHeight(IntPtr hwnd)
        {
            if (_activeTab == 0) // Overview
            {
                return 440;
            }
            if (_activeTab == 1) // Convert
            {
                return 400;
            }
            if (_activeTab == 2) // History
            {
                var activeEntry = ClickraStorage.GetActiveEntry();
                int totalHeight = 90 + (activeEntry.HasValue ? 52 : 0);
                for (int i = 0; i < _historyEntries.Count; i++)
                {
                    totalHeight += (i == _expandedHistoryIndex ? 160 : 44) + 8;
                }
                return Math.Max(460, totalHeight + 20);
            }
            if (_activeTab == 3) // Settings
            {
                string outputDirMode = ClickraStorage.GetSetting("OutputDir");
                bool isCustom = !outputDirMode.Equals("source", StringComparison.OrdinalIgnoreCase) &&
                                !outputDirMode.Equals("desktop", StringComparison.OrdinalIgnoreCase) &&
                                !outputDirMode.Equals("downloads", StringComparison.OrdinalIgnoreCase);
                float langY = isCustom && !string.IsNullOrEmpty(outputDirMode) ? 365 : 340;
                return langY + 120;
            }
            if (_activeTab == 4) // About
            {
                return Math.Max(460, _aboutBtnY + 60);
            }
            return 460;
        }

        static void RecreateBuffer(int w, int h)
        {
            try { _bufferGraphics?.Dispose(); _bufferGraphics = null; } catch {}
            try { _bufferBmp?.Dispose(); _bufferBmp = null; } catch {}
            if (w <= 0 || h <= 0) return;
            _bufferBmp = new Bitmap(w, h);
            _bufferGraphics = Graphics.FromImage(_bufferBmp);
            _bufferGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            _bufferGraphics.SmoothingMode = SmoothingMode.AntiAlias;
        }

        static void RecreateScaledFonts()
        {
            try { _titleFont?.Dispose(); } catch {}
            try { _subFont?.Dispose(); } catch {}
            try { _tabFont?.Dispose(); } catch {}
            try { _contentTitleFont?.Dispose(); } catch {}
            try { _sectionFont?.Dispose(); } catch {}
            try { _bodyFont?.Dispose(); } catch {}
            try { _tagFont?.Dispose(); } catch {}
            try { _iconFont?.Dispose(); } catch {}

            string lang = ClickraStorage.GetSetting("Language");
            lang = Clickra.Core.Localization.NormalizeLanguageCode(lang);
            string fontName = "Segoe UI";
            if (lang.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) || lang.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase))
                fontName = "Microsoft JhengHei UI";
            else if (lang.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase) || lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                fontName = "Microsoft YaHei UI";
            else if (lang.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                fontName = "Yu Gothic UI";
            else if (lang.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
                fontName = "Malgun Gothic";

            _titleFont = new Font(fontName, 16 * _dpiScale, FontStyle.Bold);
            _subFont = new Font(fontName, 8 * _dpiScale);
            _tabFont = new Font(fontName, 9 * _dpiScale);
            _contentTitleFont = new Font(fontName, 14 * _dpiScale, FontStyle.Bold);
            _sectionFont = new Font(fontName, 10 * _dpiScale, FontStyle.Bold);
            _bodyFont = new Font(fontName, 8.5f * _dpiScale);
            _tagFont = new Font(fontName, 7.5f * _dpiScale, FontStyle.Bold);
            _iconFont = new Font("Segoe MDL2 Assets", 10 * _dpiScale);

            // Measure the tab button text widths to determine sidebar width dynamically
            float maxLabelW = 0;
            using (var tempBmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(tempBmp))
            {
                string[] keys = { "tab_status", "tab_convert", "tab_history", "tab_settings", "tab_about" };
                foreach (var key in keys)
                {
                    string text = Clickra.Core.Localization.T(key, lang);
                    var size = g.MeasureString(text, _tabFont);
                    if (size.Width > maxLabelW)
                    {
                        maxLabelW = size.Width;
                    }
                }
            }
            // Sidebar width: 24 (left margin) + 16 (icon) + 12 (icon to text margin) = 52. Plus padding of 24.
            _sidebarWidth = (52f * _dpiScale + maxLabelW + 24f * _dpiScale) / _dpiScale;
            _sidebarWidth = Math.Max(130f, _sidebarWidth); // Ensure it's at least 130px

            // Cache button widths to avoid GC pressure in HitTest
            using (var tempBmp = new Bitmap(1, 1))
            using (var tempG = Graphics.FromImage(tempBmp))
            {
                if (_subFont != null)
                {
                    string textSource = GetText("setting_output_same_as_source");
                    string textDesktop = GetText("setting_output_desktop");
                    string textDownloads = GetText("setting_output_downloads");
                    string textCustom = GetText("setting_output_custom");
                    
                    _wSource = Math.Max(110f, tempG.MeasureString(textSource, _subFont).Width / _dpiScale + 20f);
                    _wDesktop = Math.Max(65f, tempG.MeasureString(textDesktop, _subFont).Width / _dpiScale + 20f);
                    _wDownloads = Math.Max(80f, tempG.MeasureString(textDownloads, _subFont).Width / _dpiScale + 20f);
                    _wCustom = Math.Max(100f, tempG.MeasureString(textCustom, _subFont).Width / _dpiScale + 20f);

                    string textGit = GetText("about_btn_github");
                    string textGmail = GetText("about_btn_gmail");
                    if (_iconFont != null)
                    {
                        float iconW_git = tempG.MeasureString("\uE71B", _iconFont).Width / _dpiScale;
                        float textW_git = tempG.MeasureString(textGit, _subFont).Width / _dpiScale;
                        _wGit = Math.Max(160f, iconW_git + 6f + textW_git + 24f);

                        float iconW_gmail = tempG.MeasureString("\uE715", _iconFont).Width / _dpiScale;
                        float textW_gmail = tempG.MeasureString(textGmail, _subFont).Width / _dpiScale;
                        _wGmail = Math.Max(160f, iconW_gmail + 6f + textW_gmail + 24f);
                    }
                    else
                    {
                        _wGit = Math.Max(160f, tempG.MeasureString(textGit, _subFont).Width / _dpiScale + 24f);
                        _wGmail = Math.Max(160f, tempG.MeasureString(textGmail, _subFont).Width / _dpiScale + 24f);
                    }
                }
            }
        }

        static void SetupTrayIcon(IntPtr hwnd, IntPtr hIcon)
        {
            _nid = new NOTIFYICONDATAW();
            _nid.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>();
            _nid.hWnd = hwnd;
            _nid.uID = 1;
            _nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            _nid.uCallbackMessage = WM_TRAYICON;
            _nid.hIcon = hIcon;
            _nid.szTip = "Clickra Dashboard";

            Shell_NotifyIcon(NIM_ADD, ref _nid);
            _trayIconAdded = true;
        }

        static void RemoveTrayIcon()
        {
            if (_trayIconAdded)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _nid);
                _trayIconAdded = false;
            }
        }

        static string BrowseForFolder(IntPtr hwndOwner, string title)
        {
            var bi = new BROWSEINFO();
            bi.hwndOwner = hwndOwner;
            bi.lpszTitle = Marshal.StringToHGlobalUni(title);
            bi.ulFlags = 0x00000040 | 0x00000010; // BIF_NEWDIALOGSTYLE | BIF_EDITBOX
            try
            {
                IntPtr pidl = SHBrowseForFolder(ref bi);
                if (pidl != IntPtr.Zero)
                {
                    IntPtr pathBuffer = Marshal.AllocHGlobal(260 * 2);
                    string path = "";
                    if (SHGetPathFromIDList(pidl, pathBuffer))
                    {
                        path = Marshal.PtrToStringUni(pathBuffer) ?? "";
                    }
                    Marshal.FreeHGlobal(pathBuffer);
                    CoTaskMemFree(pidl);
                    return path;
                }
            }
            finally
            {
                if (bi.lpszTitle != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(bi.lpszTitle);
                }
            }
            return "";
        }

        static bool IsHoveringHistoryRow(IntPtr hwnd)
        {
            if (_activeTab != 2) return false;
            var pt = new Point();
            if (GetCursorPos(out pt))
            {
                ScreenToClient(hwnd, ref pt);
                int mouseX = (int)(pt.X / _dpiScale);
                int mouseY = (int)(pt.Y / _dpiScale);
                float logW = LogicalWidth(hwnd);
                float sidebarW = GetSidebarWidth(logW);
                float contentX = GetContentX(logW);
                int adjMouseX = mouseX >= sidebarW ? (int)(mouseX + _contentScrollX) : mouseX;
                int adjMouseY = mouseX >= sidebarW ? (int)(mouseY + _contentScrollY) : mouseY;
                float virtLogW = Math.Max(760f, logW);
                if (adjMouseX >= contentX && adjMouseX < virtLogW - 40)
                {
                    int startY = 90 + (ClickraStorage.GetActiveEntry().HasValue ? 52 : 0);
                    int currentY = startY;
                    for (int i = 0; i < _historyEntries.Count; i++)
                    {
                        bool isExpanded = (i == _expandedHistoryIndex);
                        int rowH = isExpanded ? 160 : 44;
                        if (adjMouseY >= currentY && adjMouseY < currentY + rowH)
                        {
                            return true;
                        }
                        currentY += rowH + 8;
                    }
                }
            }
            return false;
        }

        public static void Show()
        {
            RefreshHistoryData();

            try { SetProcessDpiAwarenessContext((IntPtr)(-4)); } catch {}
            uint dpi = 96;
            try { dpi = GetDpiForSystem(); } catch {}
            _dpiScale = dpi / 96.0f;

            string className = "ClickraWnd";
            IntPtr hClass = Marshal.StringToHGlobalUni(className);

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandle(null),
                hCursor = LoadCursorW(IntPtr.Zero, 32512),
                hbrBackground = IntPtr.Zero,
                lpszClassName = hClass
            };

            ushort regResult = RegisterClassEx(ref wc);

            int clientW = (int)(760 * _dpiScale);
            int clientH = (int)(460 * _dpiScale);
            var rect = new RECT { left = 0, top = 0, right = clientW, bottom = clientH };
            AdjustWindowRectEx(ref rect, WS_OVERLAPPEDWINDOW, false, 0);
            int winW = rect.right - rect.left;
            int winH = rect.bottom - rect.top;

            var hwnd = CreateWindowEx(0, className, "Clickra",
                WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, winW, winH,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                MessageBox(IntPtr.Zero, $"CreateWindowEx failed!\nhInstance: {wc.hInstance}\nregResult: {regResult}\nclientW: {clientW}\nclientH: {clientH}\nwinW: {winW}\nwinH: {winH}", "Clickra Diagnostics", 0x10);
                return;
            }

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
                _hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
                if (_hIcon != IntPtr.Zero)
                {
                    SendMessageW(hwnd, 0x0080, (IntPtr)0, _hIcon); // ICON_BIG
                    SendMessageW(hwnd, 0x0080, (IntPtr)1, _hIcon); // ICON_SMALL
                }
            }

            SetupTrayIcon(hwnd, _hIcon);

            RecreateScaledFonts();
            RecreateBuffer(clientW, clientH);

            ShowWindow(hwnd, 5);

            // 每 250ms 刷新一次歷史資料（供即時轉換狀態顯示）
            SetTimer(hwnd, TIMER_ID_REFRESH, 250, IntPtr.Zero);

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

            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
        }

        static int HitTest(IntPtr hwnd, int x, int y)
        {
            float rawLogW = LogicalWidth(hwnd);
            float rawLogH = LogicalHeight(hwnd);
            float logW = Math.Max(760f, rawLogW);
            float logH = Math.Max(460f, rawLogH);

            float sidebarW = GetSidebarWidth(logW);
            float contentX = GetContentX(logW);

            // Sidebar tabs (always active)
            if (x >= 0 && x < sidebarW)
            {
                if (y >= 120 && y < 160) return 0;
                if (y >= 168 && y < 208) return 1;
                if (y >= 216 && y < 256) return 2;
                if (y >= 264 && y < 304) return 3;
                if (y >= 312 && y < 352) return 4;
            }

            if (_activeTab == 1) // Convert
            {
                int zoneW = (int)logW - (int)contentX - 50;
                int buttonY = 340;
                int zoneH = 120;
                int clearX = (int)logW - 110;

                int availableWidth = (int)logW - (int)contentX - 50;
                int cardW = (availableWidth - 2 * 12) / 3;

                for (int i = 0; i < 6; i++)
                {
                    int col = i % 3;
                    int row = i / 3;
                    int cardX = (int)contentX + col * (cardW + 12);
                    int cardY = 230 + row * 50;
                    if (x >= cardX && x < cardX + cardW && y >= cardY && y < cardY + 40)
                    {
                        if (ValidateConvertFiles(ConvertCommands[i], _selectedFiles, out _))
                        {
                            return 11 + i;
                        }
                    }
                }

                if (_selectedFiles.Count > 0 && x >= clearX && x < clearX + 48 && y >= 107 && y < 107 + 22) return 19; // Clear button
                if (x >= contentX && x < contentX + zoneW && y >= 95 && y < 95 + zoneH) return 17; // Drag & Drop zone
                if (_selectedFiles.Count > 0 && _convertCommandIndex != -1 && x >= contentX && x < contentX + zoneW && y >= buttonY && y < buttonY + 36) return 18; // Start button
            }
            else if (_activeTab == 2) // History
            {
                // Clear history button
                int clearX = (int)logW - 130;
                if (x >= clearX && x < clearX + 90 && y >= 38 && y < 66) return 22;
            }
            else if (_activeTab == 3) // Settings
            {
                int rightToggleX = (int)logW - 100;

                // Quiet Mode toggle
                if (x >= rightToggleX && x < rightToggleX + 44 && y >= 105 && y < 127) return 5;
                // Notification toggle
                if (x >= rightToggleX && x < rightToggleX + 44 && y >= 175 && y < 197) return 6;

                // OutputDir buttons
                float wSource = _wSource;
                float wDesktop = _wDesktop;
                float wDownloads = _wDownloads;
                float wCustom = _wCustom;

                float margin = 10f;
                float xSource = contentX;
                float xDesktop = xSource + wSource + margin;
                float xDownloads = xDesktop + wDesktop + margin;
                float xCustom = xDownloads + wDownloads + margin;

                if (x >= xSource && x < xSource + wSource && y >= 290 && y < 320) return 7;
                if (x >= xDesktop && x < xDesktop + wDesktop && y >= 290 && y < 320) return 8;
                if (x >= xDownloads && x < xDownloads + wDownloads && y >= 290 && y < 320) return 9;
                if (x >= xCustom && x < xCustom + wCustom && y >= 290 && y < 320) return 20;

                // Language dropdown button
                if (x >= contentX && x < contentX + 240 && y >= _langDropdownY && y < _langDropdownY + 30) return 10;
            }
            else if (_activeTab == 4) // About
            {
                float wGit = _wGit;
                float wGmail = _wGmail;

                // GitHub Button: x from contentX to contentX + wGit
                if (x >= contentX && x < contentX + wGit && y >= _githubBtnY && y < _githubBtnY + 32) return 23;

                // Gmail button: x from contentX to contentX + wGmail
                if (x >= contentX && x < contentX + wGmail && y >= _aboutBtnY && y < _aboutBtnY + 32) return 24;
            }

            return -1;
        }

        static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            switch (msg)
            {
                case 0x0005: // WM_SIZE
                    {
                        int clientW = GetClientWidth(hwnd);
                        int clientH = GetClientHeight(hwnd);
                        if (w.ToInt64() == 1) // SIZE_MINIMIZED
                        {
                            ShowWindow(hwnd, SW_HIDE);
                            return IntPtr.Zero;
                        }
                        float logW = clientW / _dpiScale;
                        float logH = clientH / _dpiScale;
                        float contentH = GetContentHeight(hwnd);
                        int maxScrollX = Math.Max(0, 760 - (int)logW);
                        int maxScrollY = Math.Max(0, (int)contentH - (int)logH);
                        _contentScrollX = Math.Max(0, Math.Min(_contentScrollX, maxScrollX));
                        _contentScrollY = Math.Max(0, Math.Min(_contentScrollY, maxScrollY));
                        RecreateBuffer(clientW, clientH);
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;
                case 0x0112: // WM_SYSCOMMAND
                    if ((w.ToInt64() & 0xFFF0) == SC_MINIMIZE)
                    {
                        ShowWindow(hwnd, SW_HIDE);
                        return IntPtr.Zero;
                    }
                    break;
                case WM_TRAYICON:
                    if (l.ToInt64() == 0x0203) // WM_LBUTTONDBLCLK
                    {
                        ShowWindow(hwnd, SW_SHOW);
                        ShowWindow(hwnd, SW_RESTORE);
                        SetForegroundWindow(hwnd);
                    }
                    return IntPtr.Zero;
                case 0x02E0: // WM_DPICHANGED
                    {
                        uint newDpi = (uint)(w.ToInt64() & 0xFFFF);
                        _dpiScale = newDpi / 96.0f;
                        RecreateScaledFonts();
                        var rect = Marshal.PtrToStructure<RECT>(l);
                        SetWindowPos(hwnd, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, 0x0010 | 0x0004);
                    }
                    return IntPtr.Zero;
                case 0x0014: return (IntPtr)1; // WM_ERASEBKGND
                case 0x000F: // WM_PAINT
                    var ps = new PAINTSTRUCT();
                    var hdc = BeginPaint(hwnd, out ps);
                    Paint(hwnd, hdc);
                    EndPaint(hwnd, ref ps);
                    return IntPtr.Zero;
                case 0x0200: // WM_MOUSEMOVE
                    {
                        int rawX = (short)(l.ToInt64() & 0xFFFF);
                        int rawY = (short)((l.ToInt64() >> 16) & 0xFFFF);
                        int mouseX = (int)(rawX / _dpiScale);
                        int mouseY = (int)(rawY / _dpiScale);

                        float logW = LogicalWidth(hwnd);
                        float logH = LogicalHeight(hwnd);
                        float sidebarW = GetSidebarWidth(logW);
                        float contentX = GetContentX(logW);
                        
                        if (_isDraggingScrollY)
                        {
                            float deltaY = mouseY - _dragStartMouseY;
                            float contentH = GetContentHeight(hwnd);
                            float trackY = 4;
                            float trackH = logH - 8;
                            if (logW < 760) trackH = logH - 16;
                            float thumbH = Math.Max(20f, (logH / contentH) * trackH);
                            float scrollRange = contentH - logH;
                            float trackRange = trackH - thumbH;
                            if (trackRange > 0)
                            {
                                float deltaScrollY = (deltaY / trackRange) * scrollRange;
                                _contentScrollY = Math.Max(0, Math.Min(_dragStartScrollY + deltaScrollY, scrollRange));
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                        }
                        else if (_isDraggingScrollX)
                        {
                            float deltaX = mouseX - _dragStartMouseX;
                            float trackX = sidebarW + 4;
                            float trackW_sb = (logW - sidebarW) - 8;
                            float contentH = GetContentHeight(hwnd);
                            bool showV = logH < contentH;
                            if (showV) trackW_sb = (logW - sidebarW) - 16;
                            if (trackW_sb > 0)
                            {
                                float thumbW = Math.Max(20f, ((logW - sidebarW) / (760f - sidebarW)) * trackW_sb);
                                float scrollRange = 760f - logW;
                                float trackRange = trackW_sb - thumbW;
                                if (trackRange > 0)
                                {
                                    float deltaScrollX = (deltaX / trackRange) * scrollRange;
                                    _contentScrollX = Math.Max(0, Math.Min(_dragStartScrollX + deltaScrollX, scrollRange));
                                    InvalidateRect(hwnd, IntPtr.Zero, false);
                                }
                            }
                        }

                        int adjMouseX = mouseX >= sidebarW ? (int)(mouseX + _contentScrollX) : mouseX;
                        int adjMouseY = mouseX >= sidebarW ? (int)(mouseY + _contentScrollY) : mouseY;

                        if (_langDropdownOpen)
                        {
                            int popupY = _langDropdownY - 180;
                            if (adjMouseX >= contentX && adjMouseX <= contentX + 240 && adjMouseY >= popupY + 38 && adjMouseY < _langDropdownY)
                            {
                                int idx = _langScrollOffset + (adjMouseY - (popupY + 38)) / 26;
                                var filtered = GetFilteredLanguages();
                                if (idx >= 0 && idx < filtered.Count && idx != _langHoveredIndex)
                                {
                                    _langHoveredIndex = idx;
                                    InvalidateRect(hwnd, IntPtr.Zero, false);
                                }
                            }
                        }

                        int prevHovered = _hoveredElement;
                        _hoveredElement = HitTest(hwnd, adjMouseX, adjMouseY);
                        if (_hoveredElement != prevHovered)
                        {
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                    }
                    return IntPtr.Zero;
                case 0x0201: // WM_LBUTTONDOWN
                    {
                        int rawX = (short)(l.ToInt64() & 0xFFFF);
                        int rawY = (short)((l.ToInt64() >> 16) & 0xFFFF);
                        int mouseX = (int)(rawX / _dpiScale);
                        int mouseY = (int)(rawY / _dpiScale);

                        float logW = LogicalWidth(hwnd);
                        float logH = LogicalHeight(hwnd);
                        float contentH = GetContentHeight(hwnd);
                        bool showV = logH < contentH;
                        bool showH = logW < 760;
                        float sidebarW = GetSidebarWidth(logW);
                        float contentX = GetContentX(logW);

                        if (showV && mouseX >= logW - 8 && mouseX < logW)
                        {
                            float trackY = 4;
                            float trackH = logH - 8;
                            if (showH) trackH = logH - 16;
                            float thumbH = Math.Max(20f, (logH / contentH) * trackH);
                            float thumbY = trackY + (_contentScrollY / (contentH - logH)) * (trackH - thumbH);

                            if (mouseY >= trackY && mouseY < trackY + trackH)
                            {
                                if (mouseY >= thumbY && mouseY < thumbY + thumbH)
                                {
                                    _isDraggingScrollY = true;
                                    _dragStartMouseY = mouseY;
                                    _dragStartScrollY = _contentScrollY;
                                    SetCapture(hwnd);
                                }
                                else
                                {
                                    float relativePos = (mouseY - trackY - thumbH / 2f) / (trackH - thumbH);
                                    _contentScrollY = Math.Max(0, Math.Min(relativePos * (contentH - logH), contentH - logH));
                                    _isDraggingScrollY = true;
                                    _dragStartMouseY = mouseY;
                                    _dragStartScrollY = _contentScrollY;
                                    SetCapture(hwnd);
                                    InvalidateRect(hwnd, IntPtr.Zero, false);
                                }
                                return IntPtr.Zero;
                            }
                        }

                        if (showH && mouseY >= logH - 8 && mouseY < logH && mouseX >= sidebarW)
                        {
                            float trackX = sidebarW + 4;
                            float trackW_sb = (logW - sidebarW) - 8;
                            if (showV) trackW_sb = (logW - sidebarW) - 16;
                            if (trackW_sb > 0)
                            {
                                float thumbW = Math.Max(20f, ((logW - sidebarW) / (760f - sidebarW)) * trackW_sb);
                                float thumbX = trackX + (_contentScrollX / (760f - logW)) * (trackW_sb - thumbW);

                                if (mouseX >= trackX && mouseX < trackX + trackW_sb)
                                {
                                    if (mouseX >= thumbX && mouseX < thumbX + thumbW)
                                    {
                                        _isDraggingScrollX = true;
                                        _dragStartMouseX = mouseX;
                                        _dragStartScrollX = _contentScrollX;
                                        SetCapture(hwnd);
                                    }
                                    else
                                    {
                                        float trackRange = trackW_sb - thumbW;
                                        float relativePos = trackRange > 0 ? (mouseX - trackX - thumbW / 2f) / trackRange : 0f;
                                        _contentScrollX = Math.Max(0, Math.Min(relativePos * (760f - logW), 760f - logW));
                                        _isDraggingScrollX = true;
                                        _dragStartMouseX = mouseX;
                                        _dragStartScrollX = _contentScrollX;
                                        SetCapture(hwnd);
                                        InvalidateRect(hwnd, IntPtr.Zero, false);
                                    }
                                    return IntPtr.Zero;
                                }
                            }
                        }

                        int adjMouseX = mouseX >= sidebarW ? (int)(mouseX + _contentScrollX) : mouseX;
                        int adjMouseY = mouseX >= sidebarW ? (int)(mouseY + _contentScrollY) : mouseY;

                        if (_langDropdownOpen)
                        {
                            int popupY = _langDropdownY - 180;
                            if (adjMouseX >= GetContentX(logW) && adjMouseX <= GetContentX(logW) + 240)
                            {
                                if (adjMouseY >= popupY && adjMouseY < popupY + 38)
                                {
                                    // Clicked search box or container top, do nothing but keep open
                                    return IntPtr.Zero;
                                }
                                else if (adjMouseY >= popupY + 38 && adjMouseY < _langDropdownY)
                                {
                                    int clickedIdx = _langScrollOffset + (adjMouseY - (popupY + 38)) / 26;
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

                        if (_activeTab == 2)
                        {
                            float virtLogW = Math.Max(760f, logW);
                            if (adjMouseX >= GetContentX(logW) && adjMouseX < virtLogW - 40)
                            {
                                int startY = 90 + (ClickraStorage.GetActiveEntry().HasValue ? 52 : 0);
                                int currentY = startY;
                                int clickedIndex = -1;
                                for (int i = 0; i < _historyEntries.Count; i++)
                                {
                                    bool isExpanded = (i == _expandedHistoryIndex);
                                    int rowH = isExpanded ? 160 : 44;
                                    if (adjMouseY >= currentY && adjMouseY < currentY + rowH)
                                    {
                                        clickedIndex = i;
                                        break;
                                    }
                                    currentY += rowH + 8;
                                }

                                if (clickedIndex != -1)
                                {
                                    if (_expandedHistoryIndex == clickedIndex)
                                    {
                                        _expandedHistoryIndex = -1; // Collapse
                                    }
                                    else
                                    {
                                        _expandedHistoryIndex = clickedIndex; // Expand
                                    }
                                    InvalidateRect(hwnd, IntPtr.Zero, false);
                                    return IntPtr.Zero;
                                }
                            }
                        }

                        int element = HitTest(hwnd, adjMouseX, adjMouseY);
                        if (element >= 0 && element <= 4)
                        {
                            _activeTab = element;
                            if (_activeTab == 0 || _activeTab == 2)
                            {
                                RefreshHistoryData();
                            }
                            _historyScrollOffset = 0;
                            _langScrollOffset = 0;
                            _contentScrollX = 0;
                            _contentScrollY = 0;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 22) // Clear history
                        {
                            if (MessageBox(hwnd, GetText("history_clear_confirm"), "Clickra", 0x24) == 6) // MB_YESNO | MB_ICONQUESTION, 6 is IDYES
                            {
                                ClickraStorage.ClearHistory();
                                _expandedHistoryIndex = -1;
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
                        else if (element == 20) // OutputDir: custom
                        {
                            string title = GetText("setting_output_browse_title");
                            string folder = BrowseForFolder(hwnd, title);
                            if (!string.IsNullOrEmpty(folder))
                            {
                                ClickraStorage.SaveSetting("OutputDir", folder);
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                        }
                        else if (element == 10) // Language dropdown button
                        {
                            _langDropdownOpen = !_langDropdownOpen;
                            if (_langDropdownOpen)
                            {
                                _langSearchQuery = "";
                                _langHoveredIndex = 0;
                                _langScrollOffset = 0;
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
                            string title = GetText("convert_drag_drop_hint");
                            const string allFilter = "Supported Files (*.doc;*.docx;*.ppt;*.pptx;*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp)\0*.doc;*.docx;*.ppt;*.pptx;*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp\0All Files (*.*)\0*.*\0\0";
                            var chosen = OpenFiles(hwnd, allFilter, title);
                            if (chosen.Count > 0)
                            {
                                _selectedFiles = chosen;
                                // Auto-select first enabled command for the chosen files
                                _convertCommandIndex = -1;
                                for (int i = 0; i < 6; i++)
                                {
                                    if (ValidateConvertFiles(ConvertCommands[i], _selectedFiles, out _))
                                    {
                                        _convertCommandIndex = i;
                                        break;
                                    }
                                }
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
                            _convertCommandIndex = -1;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }

                        else if (element == 23) // Open GitHub project URL
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "https://github.com/Youchenjiang/Clickra",
                                    UseShellExecute = true
                                });
                            }
                            catch (Exception ex)
                            {
                                MessageBox(hwnd, $"Cannot open browser: {ex.Message}", "Clickra", 0x10);
                            }
                        }
                        else if (element == 24) // One-click Gmail Diagnostics & highlight history.log in Explorer
                        {
                            try
                            {
                                string dataDir = ClickraStorage.GetDataDir();
                                string logPath = Path.Combine(dataDir, "history.log");

                                // Open Explorer and highlight history.log
                                if (File.Exists(logPath))
                                {
                                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{logPath}\"");
                                }
                                else
                                {
                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = dataDir,
                                        UseShellExecute = true
                                    });
                                }

                                // Open Gmail composer link
                                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                                string verStr = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "Unknown";
                                string timeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                                string subject = Uri.EscapeDataString("Clickra Diagnostics Report");
                                string body = Uri.EscapeDataString(
                                    "感謝您提交 Clickra 診斷回報！\r\n\r\n" +
                                    "請直接將已為您選取好的「history.log」拖曳到此郵件中作為附件。\r\n\r\n" +
                                    $"[系統資訊]\r\n" +
                                    $"作業系統: Windows\r\n" +
                                    $"Clickra 版本: {verStr}\r\n" +
                                    $"時間: {timeStr}\r\n\r\n" +
                                    "[問題描述]\r\n" +
                                    "（請在此處填寫您遇到的問題...）"
                                );
                                string gmailUrl = $"https://mail.google.com/mail/?view=cm&fs=1&to=jiangyouchen%40gmail.com&su={subject}&body={body}";
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = gmailUrl,
                                    UseShellExecute = true
                                });
                            }
                            catch (Exception ex)
                            {
                                MessageBox(hwnd, $"Cannot start feedback: {ex.Message}", "Clickra", 0x10);
                            }
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
                                _langScrollOffset = 0;
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
                            _langScrollOffset = 0;
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
                                if (_langHoveredIndex < _langScrollOffset)
                                {
                                    _langScrollOffset = _langHoveredIndex;
                                }
                                else if (_langHoveredIndex >= _langScrollOffset + 5)
                                {
                                    _langScrollOffset = _langHoveredIndex - 4;
                                }
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
                                if (_langHoveredIndex < _langScrollOffset)
                                {
                                    _langScrollOffset = _langHoveredIndex;
                                }
                                else if (_langHoveredIndex >= _langScrollOffset + 5)
                                {
                                    _langScrollOffset = _langHoveredIndex - 4;
                                }
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                            return IntPtr.Zero;
                        }
                    }
                    break;
                case 0x0202: // WM_LBUTTONUP
                    if (_isDraggingScrollX || _isDraggingScrollY)
                    {
                        _isDraggingScrollX = false;
                        _isDraggingScrollY = false;
                        ReleaseCapture();
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;
                case 0x0020: // WM_SETCURSOR
                    {
                        var pt = new Point();
                        if (GetCursorPos(out pt))
                        {
                            ScreenToClient(hwnd, ref pt);
                            int mouseX = (int)(pt.X / _dpiScale);
                            int mouseY = (int)(pt.Y / _dpiScale);
                            float logW = LogicalWidth(hwnd);
                            float logH = LogicalHeight(hwnd);
                            float contentH = GetContentHeight(hwnd);
                            bool showV = logH < contentH;
                            bool showH = logW < 760;
                             if ((showV && mouseX >= logW - 8 && mouseX < logW) ||
                                 (showH && mouseY >= logH - 8 && mouseY < logH && mouseX >= GetSidebarWidth(logW)))
                            {
                                SetCursor(LoadCursorW(IntPtr.Zero, 32649)); // IDC_HAND = 32649
                                return (IntPtr)1;
                            }
                        }
                        if (_hoveredElement != -1 || _langDropdownOpen || IsHoveringHistoryRow(hwnd) || _isDraggingScrollX || _isDraggingScrollY)
                        {
                            SetCursor(LoadCursorW(IntPtr.Zero, 32649)); // IDC_HAND = 32649
                            return (IntPtr)1; // Handled
                        }
                    }
                    break;
                case 0x0113: // WM_TIMER
                    if (w == TIMER_ID_REFRESH)
                    {
                        RefreshHistoryData();
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;
                case 0x020A: // WM_MOUSEWHEEL
                    {
                        short delta = (short)((w.ToInt64() >> 16) & 0xFFFF);
                        int scrollDir = delta > 0 ? -1 : 1;
                        if (_langDropdownOpen)
                        {
                            var filtered = GetFilteredLanguages();
                            _langScrollOffset = Math.Max(0, Math.Min(_langScrollOffset + scrollDir, filtered.Count - 5));
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else
                        {
                            float logH = LogicalHeight(hwnd);
                            float contentH = GetContentHeight(hwnd);
                            if (logH < contentH)
                            {
                                int maxScrollY = (int)contentH - (int)logH;
                                _contentScrollY = Math.Max(0, Math.Min(_contentScrollY + scrollDir * 20, maxScrollY));
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                        }
                    }
                    return IntPtr.Zero;
                case 0x0002: // WM_DESTROY
                    KillTimer(hwnd, TIMER_ID_REFRESH);
                    RemoveTrayIcon();
                    CleanupResources();
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, w, l);
        }
    }
}

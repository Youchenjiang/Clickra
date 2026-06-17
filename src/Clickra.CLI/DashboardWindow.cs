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

        private static float GetMaxValW(float logW)
        {
            float contentX = GetContentX(logW);
            float virtLogW = Math.Max(760f, logW);
            float rowW = virtLogW - contentX - 40;
            
            float w1, w2, w3, w4;
            using (var tempBmp = new Bitmap(1, 1))
            using (var tempG = Graphics.FromImage(tempBmp))
            {
                w1 = tempG.MeasureString(GetText("history_detail_inputs") + ":", _subFont).Width / _dpiScale;
                w2 = tempG.MeasureString(GetText("history_detail_outputs") + ":", _subFont).Width / _dpiScale;
                w3 = tempG.MeasureString(GetText("history_detail_time") + ":", _subFont).Width / _dpiScale;
                w4 = tempG.MeasureString(GetText("history_detail_error") + ":", _subFont).Width / _dpiScale;
            }
            float maxLabelW = Math.Max(w1, Math.Max(w2, Math.Max(w3, w4)));
            float valX = contentX + 12 + maxLabelW + 16;
            return contentX + rowW - 12 - valX;
        }

        static float GetContentHeight(IntPtr hwnd)
        {
            if (_activeTab == 0) // Overview
            {
                return 440;
            }
            if (_activeTab == 1) // Convert
            {
                return 450;
            }
            if (_activeTab == 2) // History
            {
                var activeEntry = ClickraStorage.GetActiveEntry();
                int activeCount = 0;
                if (activeEntry.HasValue)
                {
                    var ae = activeEntry.Value;
                    var activeFiles = !string.IsNullOrEmpty(ae.InputPaths)
                        ? ae.InputPaths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                        : Array.Empty<string>();
                    activeCount = activeFiles.Length > 0 ? activeFiles.Length : 1;
                }
                int totalHeight = 90 + activeCount * 52;
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
                return langY + 280;
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
                    var activeEntry = ClickraStorage.GetActiveEntry();
                    int activeCount = 0;
                    if (activeEntry.HasValue)
                    {
                        var ae = activeEntry.Value;
                        var activeFiles = !string.IsNullOrEmpty(ae.InputPaths)
                            ? ae.InputPaths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                            : Array.Empty<string>();
                        activeCount = activeFiles.Length > 0 ? activeFiles.Length : 1;
                    }
                    int startY = 90 + activeCount * 52;
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
            bool createdNew;
            _mutex = new System.Threading.Mutex(true, "Global\\Clickra_Dashboard_Mutex", out createdNew);
            if (!createdNew)
            {
                IntPtr existingHwnd = FindWindow("ClickraWnd", null);
                if (existingHwnd != IntPtr.Zero)
                {
                    ShowWindow(existingHwnd, 5); // SW_SHOW
                    ShowWindow(existingHwnd, 9); // SW_RESTORE
                    SetForegroundWindow(existingHwnd);
                }
                _mutex.Dispose();
                _mutex = null;
                return;
            }

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
                _mutex.Dispose();
                _mutex = null;
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
                int buttonY = 390;
                int zoneH = 120;
                int clearX = (int)logW - 110;

                int availableWidth = (int)logW - (int)contentX - 50;
                int cardW = (availableWidth - 2 * 12) / 3;

                for (int i = 0; i < 8; i++)
                {
                    int col = i % 3;
                    int row = i / 3;
                    int cardX = (int)contentX + col * (cardW + 12);
                    int cardY = 230 + row * 50;
                    if (x >= cardX && x < cardX + cardW && y >= cardY && y < cardY + 40)
                    {
                        if (ValidateConvertFiles(ConvertCommands[i], _selectedFiles, out _))
                        {
                            return 50 + i;
                        }
                    }
                }

                if (_selectedFiles.Count > 0 && x >= clearX && x < clearX + 48 && y >= 107 && y < 107 + 22) return 25; // Clear button
                if (x >= contentX && x < contentX + zoneW && y >= 95 && y < 95 + zoneH) return 18; // Drag & Drop zone
                if (_selectedFiles.Count > 0 && _convertCommandIndex != -1 && x >= contentX && x < contentX + zoneW && y >= buttonY && y < buttonY + 36) return 19; // Start button
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

                // PDF Translation dropdown buttons
                if (x >= contentX && x < contentX + 240 && y >= _pdfLangDropdownY && y < _pdfLangDropdownY + 30) return 31;
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

        
    }
}

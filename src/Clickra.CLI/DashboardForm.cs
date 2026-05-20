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

        const uint WS_OVERLAPPED_FIXED = 0x00CF0000 & ~0x00040000u & ~0x00020000u;
        const int DWMWA_DARK_MODE = 20;
        const int CW_USEDEFAULT = unchecked((int)0x80000000);

        delegate IntPtr WndProcDelegate(IntPtr h, uint msg, IntPtr w, IntPtr l);
        static WndProcDelegate _wndProc = WndProc;

        // UI State Variables
        static int _activeTab = 0; // 0: Overview, 1: History, 2: Settings
        static int _hoveredElement = -1; // IDs of hovered elements
        
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

            if (_bufferBmp == null)
            {
                _bufferBmp = new Bitmap(760, 460);
                _bufferGraphics = Graphics.FromImage(_bufferBmp);
                _bufferGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                _bufferGraphics.SmoothingMode = SmoothingMode.AntiAlias;
            }

            ShowWindow(hwnd, 5);

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
                DispatchMessage(ref msg);
            
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
            try { _bufferGraphics?.Dispose(); _bufferGraphics = null; } catch { }
            try { _bufferBmp?.Dispose(); _bufferBmp = null; } catch { }
        }

        static int HitTest(int x, int y)
        {
            // Sidebar tabs (always active)
            if (x >= 0 && x < 200 && y >= 120 && y < 160) return 0;
            if (x >= 0 && x < 200 && y >= 168 && y < 208) return 1;
            if (x >= 0 && x < 200 && y >= 216 && y < 256) return 2;

            if (_activeTab == 1) // History
            {
                // Clear history button
                if (x >= 630 && x < 720 && y >= 38 && y < 66) return 3;
            }
            else if (_activeTab == 2) // Settings
            {
                // Quiet Mode toggle
                if (x >= 660 && x < 704 && y >= 105 && y < 127) return 4;
                // Notification toggle
                if (x >= 660 && x < 704 && y >= 175 && y < 197) return 5;
                // OutputDir: Source
                if (x >= 236 && x < 346 && y >= 290 && y < 320) return 6;
                // OutputDir: Desktop
                if (x >= 356 && x < 431 && y >= 290 && y < 320) return 7;
                // OutputDir: Downloads
                if (x >= 441 && x < 516 && y >= 290 && y < 320) return 8;
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
                        int element = HitTest(mouseX, mouseY);
                        if (element >= 0 && element <= 2)
                        {
                            _activeTab = element;
                            if (_activeTab == 0 || _activeTab == 1)
                            {
                                RefreshHistoryData();
                            }
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 3) // Clear history
                        {
                            if (MessageBox(hwnd, "您確定要清除所有的轉換歷史紀錄嗎？", "Clickra", 0x24) == 6) // MB_YESNO | MB_ICONQUESTION, 6 is IDYES
                            {
                                ClickraStorage.ClearHistory();
                                RefreshHistoryData();
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                        }
                        else if (element == 4) // Toggle Quiet Mode
                        {
                            bool current = ClickraStorage.GetSetting("QuietMode").Equals("true", StringComparison.OrdinalIgnoreCase);
                            ClickraStorage.SaveSetting("QuietMode", current ? "false" : "true");
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 5) // Toggle Notification
                        {
                            bool current = ClickraStorage.GetSetting("Notification").Equals("true", StringComparison.OrdinalIgnoreCase);
                            ClickraStorage.SaveSetting("Notification", current ? "false" : "true");
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 6) // OutputDir: source
                        {
                            ClickraStorage.SaveSetting("OutputDir", "source");
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 7) // OutputDir: desktop
                        {
                            ClickraStorage.SaveSetting("OutputDir", "desktop");
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        else if (element == 8) // OutputDir: downloads
                        {
                            ClickraStorage.SaveSetting("OutputDir", "downloads");
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                    }
                    return IntPtr.Zero;
                case 0x0020: // WM_SETCURSOR
                    if (_hoveredElement != -1)
                    {
                        SetCursor(LoadCursorW(IntPtr.Zero, 32649)); // IDC_HAND = 32649
                        return (IntPtr)1; // Handled
                    }
                    break;
                case 0x0002: // WM_DESTROY
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
            DrawTabButton(g, "🏠  首頁狀態", 0, 120);
            DrawTabButton(g, "📜  轉換歷史", 1, 168);
            DrawTabButton(g, "⚙  偏好設定", 2, 216);

            // 2. Draw Content Area
            if (_activeTab == 0)
            {
                DrawOverviewTab(g);
            }
            else if (_activeTab == 1)
            {
                DrawHistoryTab(g);
            }
            else if (_activeTab == 2)
            {
                DrawSettingsTab(g);
            }

            // Draw double buffer to screen
            using var targetG = Graphics.FromHdc(hdc);
            targetG.DrawImage(_bufferBmp, 0, 0);
        }

        static void DrawTabButton(Graphics g, string text, int tabIndex, int y)
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
            if (_tabFont != null)
                g.DrawString(text, _tabFont, textBrush, 28, y + 10);
        }

        static void DrawOverviewTab(Graphics g)
        {
            // Title
            if (_contentTitleFont != null)
                g.DrawString("首頁狀態", _contentTitleFont, Brushes.White, 236, 30);

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, 236, 75, 720, 75);
            }

            // Engine status
            if (_sectionFont != null)
                g.DrawString("轉換引擎狀態", _sectionFont, Brushes.White, 236, 95);

            DrawEngineRow(g, "PDF 處理核心 (PDF Engine)", true, 236, 125);
            DrawEngineRow(g, "PowerPoint 轉換器 (PowerPoint)", IsOfficeInstalled("PowerPoint"), 236, 165);
            DrawEngineRow(g, "Word 轉換器 (Word)", IsOfficeInstalled("Word"), 236, 205);

            // Statistics
            if (_sectionFont != null)
                g.DrawString("轉換統計", _sectionFont, Brushes.White, 236, 260);

            // Draw Cards
            DrawStatCard(g, "總轉換次數", _statTotal.ToString(), Color.FromArgb(200, 200, 200), 236, 290, 140);
            DrawStatCard(g, "成功次數", _statSuccess.ToString(), Color.FromArgb(100, 220, 100), 396, 290, 140);
            DrawStatCard(g, "失敗次數", _statFailed.ToString(), Color.FromArgb(255, 90, 70), 556, 290, 140);
            
            // Footer tips
            if (_subFont != null)
            {
                using var tipBrush = new SolidBrush(Color.FromArgb(100, 100, 100));
                g.DrawString("提示：直接在檔案總管選取檔案，右鍵即可呼叫 Clickra 選單進行轉換。", _subFont, tipBrush, 236, 400);
            }
        }

        static void DrawHistoryTab(Graphics g)
        {
            // Title
            if (_contentTitleFont != null)
                g.DrawString("轉換歷史", _contentTitleFont, Brushes.White, 236, 30);

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
                var size = g.MeasureString("清除紀錄", _subFont);
                g.DrawString("清除紀錄", _subFont, btnTextBrush, 630 + (90 - size.Width) / 2, 38 + (28 - size.Height) / 2);
            }

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, 236, 75, 720, 75);
            }

            if (_historyEntries == null || _historyEntries.Count == 0)
            {
                if (_tabFont != null)
                {
                    using var textBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
                    g.DrawString("尚無任何轉換紀錄。", _tabFont, textBrush, 236, 110);
                }
                return;
            }

            // Draw history rows (up to 6)
            int limit = Math.Min(6, _historyEntries.Count);
            for (int i = 0; i < limit; i++)
            {
                var entry = _historyEntries[i];
                int rowY = 100 + i * 52;
                int rowW = 484, rowH = 44;

                using var path = GetRoundedRectPath(new RectangleF(236, rowY, rowW, rowH), 6);
                using var rowBg = new SolidBrush(Color.FromArgb(36, 36, 36));
                g.FillPath(rowBg, path);

                using var borderPen = new Pen(Color.FromArgb(48, 48, 48));
                g.DrawPath(borderPen, path);

                // Time
                if (_bodyFont != null)
                {
                    using var timeBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    g.DrawString(entry.Time, _bodyFont, timeBrush, 248, rowY + 13);
                }

                // Command Tag
                DrawCommandTag(g, entry.Command, 380, rowY + 11);

                // File Count
                if (_bodyFont != null)
                {
                    using var countBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                    g.DrawString($"{entry.FileCount} 個檔案", _bodyFont, countBrush, 470, rowY + 13);
                }

                // Status tag
                Color statusColor = entry.IsSuccess ? Color.FromArgb(100, 220, 100) : Color.FromArgb(255, 90, 70);
                string statusText = entry.IsSuccess ? "成功" : "失敗";
                if (_tagFont != null)
                {
                    using var statusBrush = new SolidBrush(statusColor);
                    g.DrawString(statusText, _tagFont, statusBrush, 550, rowY + 13);
                }

                // Error Message (if failed, truncated)
                if (!entry.IsSuccess && !string.IsNullOrEmpty(entry.ErrorMessage))
                {
                    if (_subFont != null)
                    {
                        using var errBrush = new SolidBrush(Color.FromArgb(230, 90, 70));
                        string errText = entry.ErrorMessage.Length > 16 ? entry.ErrorMessage.Substring(0, 16) + "..." : entry.ErrorMessage;
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
                    text = "Word → PDF";
                    break;
                case "ppt2pdf":
                    tagBg = Color.FromArgb(180, 50, 30);
                    text = "PPT → PDF";
                    break;
                case "merge-pdf":
                    tagBg = Color.FromArgb(16, 124, 65);
                    text = "合併 PDF";
                    break;
                case "img2pdf":
                    tagBg = Color.FromArgb(100, 60, 180);
                    text = "圖片 → PDF";
                    break;
                case "img-merge":
                    tagBg = Color.FromArgb(0, 130, 135);
                    text = "圖片合併";
                    break;
                case "img-stitch":
                    tagBg = Color.FromArgb(216, 59, 1);
                    text = "圖片拼接";
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
                g.DrawString("偏好設定", _contentTitleFont, Brushes.White, 236, 30);

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, 236, 75, 720, 75);
            }

            // Quiet mode setting
            bool quietMode = ClickraStorage.GetSetting("QuietMode").Equals("true", StringComparison.OrdinalIgnoreCase);
            bool isQuietHovered = _hoveredElement == 4;
            if (_tabFont != null)
                g.DrawString("背景靜默轉檔", _tabFont, Brushes.White, 236, 100);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString("在右鍵選單點擊時直接於背景處理，不顯示進度視窗", _subFont, subBrush, 236, 122);
            }
            DrawToggleSwitch(g, quietMode, isQuietHovered, 660, 105, 44, 22);

            // Notification setting
            bool notification = ClickraStorage.GetSetting("Notification").Equals("true", StringComparison.OrdinalIgnoreCase);
            bool isNotifHovered = _hoveredElement == 5;
            if (_tabFont != null)
                g.DrawString("顯示轉換通知", _tabFont, Brushes.White, 236, 170);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString("作業完成或失敗後，於系統右下角彈出 Windows Toast 通知", _subFont, subBrush, 236, 192);
            }
            DrawToggleSwitch(g, notification, isNotifHovered, 660, 175, 44, 22);

            // Output path setting
            if (_tabFont != null)
                g.DrawString("預設輸出路徑", _tabFont, Brushes.White, 236, 240);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString("選擇轉換後 PDF 與圖片預設的儲存位置", _subFont, subBrush, 236, 262);
            }

            string outputDirMode = ClickraStorage.GetSetting("OutputDir");
            DrawOutputDirButton(g, "與來源相同", outputDirMode.Equals("source", StringComparison.OrdinalIgnoreCase), 6, 236, 290, 110);
            DrawOutputDirButton(g, "桌面", outputDirMode.Equals("desktop", StringComparison.OrdinalIgnoreCase), 7, 356, 290, 75);
            DrawOutputDirButton(g, "下載", outputDirMode.Equals("downloads", StringComparison.OrdinalIgnoreCase), 8, 441, 290, 75);
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
                string statusText = ok ? "Ready" : "Office Not Installed";
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
    }
}

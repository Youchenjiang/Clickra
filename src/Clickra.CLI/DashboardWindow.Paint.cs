using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using Microsoft.Win32;
using Clickra.Core;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
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
            bool isClearHovered = _hoveredElement == 4;
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
    }
}

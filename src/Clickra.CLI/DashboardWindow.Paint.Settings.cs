using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using Microsoft.Win32;
using Clickra.Core;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        static void DrawSettingsTab(Graphics g, float logW, float logH, float contentX)
        {
            float s = _dpiScale;
            // Title
            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_settings"), _contentTitleFont, Brushes.White, contentX * s, 30 * s);

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, contentX * s, 75 * s, (logW - 40) * s, 75 * s);
            }

            // Quiet mode setting
            bool quietMode = ClickraStorage.GetSetting("QuietMode").Equals("true", StringComparison.OrdinalIgnoreCase);
            bool isQuietHovered = _hoveredElement == 5;
            if (_tabFont != null)
                g.DrawString(GetText("setting_silent_title"), _tabFont, Brushes.White, contentX * s, 100 * s);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(GetText("setting_silent_desc"), _subFont, subBrush, contentX * s, 122 * s);
            }
            DrawToggleSwitch(g, quietMode, isQuietHovered, (int)logW - 100, 105, 44, 22);

            // Notification setting
            bool notification = ClickraStorage.GetSetting("Notification").Equals("true", StringComparison.OrdinalIgnoreCase);
            bool isNotifHovered = _hoveredElement == 6;
            if (_tabFont != null)
                g.DrawString(GetText("setting_notify_title"), _tabFont, Brushes.White, contentX * s, 170 * s);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(GetText("setting_notify_desc"), _subFont, subBrush, contentX * s, 192 * s);
            }
            DrawToggleSwitch(g, notification, isNotifHovered, (int)logW - 100, 175, 44, 22);

            // Output path setting
            if (_tabFont != null)
                g.DrawString(GetText("setting_output_title"), _tabFont, Brushes.White, contentX * s, 240 * s);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(GetText("setting_output_desc"), _subFont, subBrush, contentX * s, 262 * s);
            }

            string outputDirMode = ClickraStorage.GetSetting("OutputDir");
            bool isSource = outputDirMode.Equals("source", StringComparison.OrdinalIgnoreCase);
            bool isDesktop = outputDirMode.Equals("desktop", StringComparison.OrdinalIgnoreCase);
            bool isDownloads = outputDirMode.Equals("downloads", StringComparison.OrdinalIgnoreCase);
            bool isCustom = !isSource && !isDesktop && !isDownloads;

            string textSource = GetText("setting_output_same_as_source");
            string textDesktop = GetText("setting_output_desktop");
            string textDownloads = GetText("setting_output_downloads");
            string textCustom = GetText("setting_output_custom");

            float wSource = _wSource;
            float wDesktop = _wDesktop;
            float wDownloads = _wDownloads;
            float wCustom = _wCustom;

            float margin = 10f;
            float xSource = contentX;
            float xDesktop = xSource + wSource + margin;
            float xDownloads = xDesktop + wDesktop + margin;
            float xCustom = xDownloads + wDownloads + margin;

            DrawOutputDirButton(g, textSource, isSource, 7, (int)xSource, 290, (int)wSource);
            DrawOutputDirButton(g, textDesktop, isDesktop, 8, (int)xDesktop, 290, (int)wDesktop);
            DrawOutputDirButton(g, textDownloads, isDownloads, 9, (int)xDownloads, 290, (int)wDownloads);
            DrawOutputDirButton(g, textCustom, isCustom, 20, (int)xCustom, 290, (int)wCustom);

            float langY = 340;
            if (isCustom && !string.IsNullOrEmpty(outputDirMode))
            {
                if (_subFont != null)
                {
                    using var pathBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
                    string displayText = outputDirMode;
                    if (displayText.Length > 60)
                    {
                        displayText = "..." + displayText.Substring(displayText.Length - 57);
                    }
                    g.DrawString($"{GetText("setting_output_selected_path")}: {displayText}", _subFont, pathBrush, contentX * s, 328 * s);
                }
                langY = 365;
            }

            _langDropdownY = (int)(langY + 50);

            // Language setting UI block
            if (_tabFont != null)
                g.DrawString(GetText("setting_lang_title"), _tabFont, Brushes.White, contentX * s, langY * s);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(GetText("setting_lang_desc"), _subFont, subBrush, contentX * s, (langY + 22) * s);
            }

            // Draw Dropdown Selector
            DrawLanguageDropdown(g, _langDropdownY, contentX);

            float transY = langY + 110;
            _pdfLangDropdownY = (int)(transY + 50);

            // PDF Translation title
            if (_tabFont != null)
                g.DrawString(GetText("setting_pdf_title"), _tabFont, Brushes.White, contentX * s, transY * s);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(GetText("setting_pdf_desc"), _subFont, subBrush, contentX * s, (transY + 22) * s);
            }

            // Draw Target Lang dropdown selector
            DrawPdfLangDropdown(g, _pdfLangDropdownY, contentX);
        }

        static void DrawToggleSwitch(Graphics g, bool state, bool hovered, int x, int y, int w, int h)
        {
            float s = _dpiScale;
            // Track
            Color trackColor = state ? UIHelper.GetSystemColorizationColor() : Color.FromArgb(60, 60, 60);
            if (hovered)
            {
                trackColor = state ? UIHelper.Lighten(trackColor, 0.15f) : Color.FromArgb(80, 80, 80);
            }
            using var trackBrush = new SolidBrush(trackColor);
            using var path = UIHelper.GetRoundedRectPath(new RectangleF(x * s, y * s, w * s, h * s), (h / 2f) * s);
            g.FillPath(trackBrush, path);

            // Thumb
            int thumbMargin = 2;
            int thumbSize = h - thumbMargin * 2;
            int thumbX = state ? (x + w - thumbSize - thumbMargin) : (x + thumbMargin);
            using var thumbBrush = new SolidBrush(Color.White);
            g.FillEllipse(thumbBrush, thumbX * s, (y + thumbMargin) * s, thumbSize * s, thumbSize * s);
        }

        static void DrawOutputDirButton(Graphics g, string text, bool selected, int elementId, int x, int y, int w)
        {
            float s = _dpiScale;
            bool isHovered = _hoveredElement == elementId;
            Color btnBg;
            Color btnBorder;
            Color textColor;

            if (selected)
            {
                btnBg = UIHelper.GetSystemColorizationColor();
                if (isHovered) btnBg = UIHelper.Lighten(btnBg, 0.15f);
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
            using var path = UIHelper.GetRoundedRectPath(new RectangleF(x * s, y * s, w * s, h * s), 4 * s);
            using var bgBrush = new SolidBrush(btnBg);
            g.FillPath(bgBrush, path);

            using var borderPen = new Pen(btnBorder);
            g.DrawPath(borderPen, path);

            if (_subFont != null)
            {
                using var textBrush = new SolidBrush(textColor);
                var size = g.MeasureString(text, _subFont);
                g.DrawString(text, _subFont, textBrush, (x + (w - size.Width / s) / 2) * s, (y + (h - size.Height / s) / 2) * s);
            }
        }

        static void DrawEngineRow(Graphics g, string label, bool ok, int x, int y)
        {
            float s = _dpiScale;
            Color dotColor = ok ? Color.FromArgb(100, 220, 100) : Color.FromArgb(255, 90, 70);
            using var dotBrush = new SolidBrush(dotColor);
            g.FillEllipse(dotBrush, x * s, (y + 4) * s, 10 * s, 10 * s);

            using var textBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
            if (_tabFont != null)
            {
                string statusText = ok ? GetText("engine_ready") : GetText("engine_office_not_installed");
                g.DrawString($"{label}:  ", _tabFont, textBrush, (x + 20) * s, y * s);
                
                using var statusBrush = new SolidBrush(dotColor);
                var labelSize = g.MeasureString($"{label}:  ", _tabFont);
                g.DrawString(statusText, _tabFont, statusBrush, (x + 20) * s + labelSize.Width, y * s);
            }
        }

        static void DrawStatCard(Graphics g, string title, string val, Color valColor, int x, int y, int w)
        {
            float s = _dpiScale;
            int h = 70;
            using var path = UIHelper.GetRoundedRectPath(new RectangleF(x * s, y * s, w * s, h * s), 6 * s);
            using var cardBg = new SolidBrush(Color.FromArgb(40, 40, 40));
            g.FillPath(cardBg, path);

            using var borderPen = new Pen(Color.FromArgb(55, 55, 55));
            g.DrawPath(borderPen, path);

            if (_subFont != null)
            {
                using var titleBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(title, _subFont, titleBrush, (x + 12) * s, (y + 10) * s);
            }

            if (_sectionFont != null)
            {
                using var valBrush = new SolidBrush(valColor);
                g.DrawString(val, _sectionFont, valBrush, (x + 12) * s, (y + 32) * s);
            }
        }
    }
}

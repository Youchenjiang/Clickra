using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using Microsoft.Win32;
using Clickra.Core;
using Clickra.Core.Processors;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        static void DrawSettingsTab(Graphics g, float logW, float logH, float contentX)
        {
            float s = _dpiScale;
            _settingsHitRects.Clear();

            void AddHitRect(int elementId, float x, float y, float w, float h)
            {
                _settingsHitRects[elementId] = new RectangleF(x, y, w, h);
            }

            void DrawSectionHeader(string titleKey, string descKey, float y)
            {
                if (_tabFont != null)
                    g.DrawString(GetText(titleKey), _tabFont, Brushes.White, contentX * s, y * s);
                if (_subFont != null)
                {
                    using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    g.DrawString(GetText(descKey), _subFont, subBrush, contentX * s, (y + 22) * s);
                }
            }

            void DrawToggleSection(string titleKey, string descKey, bool state, int elementId, float y)
            {
                DrawSectionHeader(titleKey, descKey, y);
                int toggleX = (int)logW - 100;
                DrawToggleSwitch(g, state, _hoveredElement == elementId, toggleX, (int)(y + 5), 44, 22);
                AddHitRect(elementId, toggleX, y + 5, 44, 22);
            }

            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_settings"), _contentTitleFont, Brushes.White, contentX * s, 30 * s);

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, contentX * s, 75 * s, (logW - 40) * s, 75 * s);
            }

            float y = 100f;
            float margin = 10f;

            bool quietMode = ClickraStorage.GetSetting("QuietMode").Equals("true", StringComparison.OrdinalIgnoreCase);
            DrawToggleSection("setting_silent_title", "setting_silent_desc", quietMode, 5, y);
            y += 70f;

            bool notification = ClickraStorage.GetSetting("Notification").Equals("true", StringComparison.OrdinalIgnoreCase);
            DrawToggleSection("setting_notify_title", "setting_notify_desc", notification, 6, y);
            y += 70f;

            DrawSectionHeader("setting_output_title", "setting_output_desc", y);

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

            float xSource = contentX;
            float xDesktop = xSource + wSource + margin;
            float xDownloads = xDesktop + wDesktop + margin;
            float xCustom = xDownloads + wDownloads + margin;
            float buttonY = y + 50f;

            DrawOutputDirButton(g, textSource, isSource, 7, (int)xSource, (int)buttonY, (int)wSource);
            DrawOutputDirButton(g, textDesktop, isDesktop, 8, (int)xDesktop, (int)buttonY, (int)wDesktop);
            DrawOutputDirButton(g, textDownloads, isDownloads, 9, (int)xDownloads, (int)buttonY, (int)wDownloads);
            DrawOutputDirButton(g, textCustom, isCustom, 20, (int)xCustom, (int)buttonY, (int)wCustom);
            AddHitRect(7, xSource, buttonY, wSource, 30);
            AddHitRect(8, xDesktop, buttonY, wDesktop, 30);
            AddHitRect(9, xDownloads, buttonY, wDownloads, 30);
            AddHitRect(20, xCustom, buttonY, wCustom, 30);

            y += 88f;
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
                    g.DrawString($"{GetText("setting_output_selected_path")}: {displayText}", _subFont, pathBrush, contentX * s, y * s);
                }
                y += 24f;
            }

            DrawSectionHeader("setting_engine_title", "setting_engine_desc", y);

            string engineMode = ClickraStorage.GetSetting("OfficeEngine");
            bool isAutoEngine = string.IsNullOrEmpty(engineMode) || engineMode.Equals("auto", StringComparison.OrdinalIgnoreCase);
            bool isMicrosoftEngine = engineMode.Equals("microsoft", StringComparison.OrdinalIgnoreCase);
            bool isLibreOfficeEngine = engineMode.Equals("libreoffice", StringComparison.OrdinalIgnoreCase);

            float xEngineAuto = contentX;
            float xEngineMicrosoft = xEngineAuto + _wEngineAuto + margin;
            float xEngineLibreOffice = xEngineMicrosoft + _wEngineMicrosoft + margin;
            float engineButtonY = y + 50f;

            DrawOutputDirButton(g, GetText("setting_engine_auto"), isAutoEngine, 32, (int)xEngineAuto, (int)engineButtonY, (int)_wEngineAuto);
            DrawOutputDirButton(g, GetText("setting_engine_microsoft"), isMicrosoftEngine, 33, (int)xEngineMicrosoft, (int)engineButtonY, (int)_wEngineMicrosoft);
            DrawOutputDirButton(g, GetText("setting_engine_libreoffice"), isLibreOfficeEngine, 34, (int)xEngineLibreOffice, (int)engineButtonY, (int)_wEngineLibreOffice);
            AddHitRect(32, xEngineAuto, engineButtonY, _wEngineAuto, 30);
            AddHitRect(33, xEngineMicrosoft, engineButtonY, _wEngineMicrosoft, 30);
            AddHitRect(34, xEngineLibreOffice, engineButtonY, _wEngineLibreOffice, 30);

            y += 88f;
            bool isLibreOfficeSetupRunning;
            int downloadProgress;
            string downloadStatus;
            lock (_libreOfficeDownloadLock)
            {
                isLibreOfficeSetupRunning = _libreOfficeDownloadInProgress;
                downloadProgress = _libreOfficeDownloadProgress;
                downloadStatus = _libreOfficeDownloadStatus;
            }
            string resolvedLibreOffice = isLibreOfficeSetupRunning ? "" : LibreOfficeHelper.GetResolvedExecutablePath();
            bool removalPendingRestart = ClickraStorage.GetSetting("LibreOfficeRemovalPendingRestart").Equals("true", StringComparison.OrdinalIgnoreCase);
            bool libreOfficeReady = !string.IsNullOrEmpty(resolvedLibreOffice);
            bool officeReady = IsOfficeInstalled("Word") && IsOfficeInstalled("Excel") && IsOfficeInstalled("PowerPoint");

            if (_subFont != null)
            {
                Color statusColor;
                string statusText;

                if (isMicrosoftEngine)
                {
                    statusColor = officeReady ? Color.FromArgb(100, 220, 100) : Color.FromArgb(255, 90, 70);
                    statusText = GetText(officeReady ? "setting_microsoft_ready" : "setting_microsoft_missing");
                }
                else if (isAutoEngine)
                {
                    if (officeReady)
                    {
                        statusColor = Color.FromArgb(100, 220, 100);
                        statusText = string.Format(GetText("setting_engine_auto_using"), GetText("setting_engine_microsoft"));
                    }
                    else if (libreOfficeReady)
                    {
                        statusColor = Color.FromArgb(100, 220, 100);
                        statusText = string.Format(GetText("setting_engine_auto_using"), GetText("setting_engine_libreoffice"));
                    }
                    else
                    {
                        statusColor = Color.FromArgb(255, 90, 70);
                        statusText = GetText("setting_engine_none_available");
                    }
                }
                else
                {
                    statusColor = isLibreOfficeSetupRunning
                        ? Color.FromArgb(190, 190, 190)
                        : removalPendingRestart
                            ? Color.FromArgb(255, 190, 90)
                        : libreOfficeReady
                            ? Color.FromArgb(100, 220, 100)
                            : Color.FromArgb(255, 90, 70);
                    statusText = isLibreOfficeSetupRunning
                        ? downloadStatus
                        : removalPendingRestart
                            ? GetText("setting_libreoffice_removal_pending")
                        : libreOfficeReady
                        ? $"{GetText("setting_libreoffice_ready")}: {ShortPath(resolvedLibreOffice, 62)}"
                        : GetText("setting_libreoffice_missing");
                }

                if (!(isLibreOfficeEngine && isLibreOfficeSetupRunning))
                {
                    using var statusBrush = new SolidBrush(statusColor);
                    g.DrawString(statusText, _subFont, statusBrush, contentX * s, y * s);
                }
            }

            if (isLibreOfficeEngine)
            {
                y += 28f;
                if (isLibreOfficeSetupRunning)
                {
                    DrawDownloadProgress(g, downloadStatus, downloadProgress, (int)contentX, (int)y, 360);
                    y += 42f;
                }
                else if (removalPendingRestart)
                {
                    DrawOutputDirButton(
                        g,
                        GetText("setting_libreoffice_reinstall"),
                        false,
                        36,
                        (int)contentX,
                        (int)y,
                        (int)_wLibreOfficeDownload);
                    AddHitRect(36, contentX, y, _wLibreOfficeDownload, 30);

                    float browseX = contentX + _wLibreOfficeDownload + margin;
                    DrawOutputDirButton(
                        g,
                        GetText("setting_libreoffice_browse"),
                        false,
                        35,
                        (int)browseX,
                        (int)y,
                        (int)_wLibreOfficeBrowse);
                    AddHitRect(35, browseX, y, _wLibreOfficeBrowse, 30);
                    y += 55f;
                }
                else if (libreOfficeReady)
                {
                    DrawOutputDirButton(
                        g,
                        GetText("setting_libreoffice_update"),
                        false,
                        36,
                        (int)contentX,
                        (int)y,
                        (int)_wLibreOfficeDownload);
                    AddHitRect(36, contentX, y, _wLibreOfficeDownload, 30);

                    float uninstallX = contentX + _wLibreOfficeDownload + margin;
                    DrawOutputDirButton(
                        g,
                        GetText("setting_libreoffice_uninstall"),
                        false,
                        38,
                        (int)uninstallX,
                        (int)y,
                        (int)_wLibreOfficeUninstall);
                    AddHitRect(38, uninstallX, y, _wLibreOfficeUninstall, 30);
                    y += 55f;
                }
                else
                {
                    DrawOutputDirButton(
                        g,
                        GetText("setting_libreoffice_download"),
                        false,
                        36,
                        (int)contentX,
                        (int)y,
                        (int)_wLibreOfficeDownload);
                    AddHitRect(36, contentX, y, _wLibreOfficeDownload, 30);

                    float browseX = contentX + _wLibreOfficeDownload + margin;
                    DrawOutputDirButton(
                        g,
                        GetText("setting_libreoffice_browse"),
                        false,
                        35,
                        (int)browseX,
                        (int)y,
                        (int)_wLibreOfficeBrowse);
                    AddHitRect(35, browseX, y, _wLibreOfficeBrowse, 30);
                    y += 55f;
                }
            }
            else
            {
                y += 32f;
            }

            DrawSectionHeader("setting_lang_title", "setting_lang_desc", y);
            _langDropdownY = (int)(y + 50);

            DrawLanguageDropdown(g, _langDropdownY, contentX);
            AddHitRect(10, contentX, _langDropdownY, 240, 30);

            y += 110f;
            DrawSectionHeader("setting_pdf_title", "setting_pdf_desc", y);
            _pdfLangDropdownY = (int)(y + 50);

            DrawPdfLangDropdown(g, _pdfLangDropdownY, contentX);
            AddHitRect(31, contentX, _pdfLangDropdownY, 240, 30);

            y += 95f;
            DrawSectionHeader("setting_pdf_compress_title", "setting_pdf_compress_desc", y);
            y += 48f;

            // Compact slider: one level maps to both DPI + JPEG quality
            int compressLevel = GetPdfCompressLevel();
            float sliderW = 300f;
            _pdfSliderTrackX = contentX;
            _pdfSliderTrackW = sliderW;
            DrawCompressSlider(g, contentX, y, sliderW, compressLevel);
            AddHitRect(83, contentX - 10, y - 4, sliderW + 20, 62);
            y += 72f;

            // Strip Fonts Toggle
            bool stripFonts = ClickraStorage.GetSetting("PdfCompressStripFonts").Equals("true", StringComparison.OrdinalIgnoreCase);
            DrawToggleSection("setting_pdf_compress_strip_fonts", "", stripFonts, 81, y);
            y += 44f;

            // Minify Content Toggle
            bool minifyContent = !ClickraStorage.GetSetting("PdfCompressMinifyContent").Equals("false", StringComparison.OrdinalIgnoreCase);
            DrawToggleSection("setting_pdf_compress_minify_content", "", minifyContent, 82, y);
            y += 44f;

            _settingsContentHeight = Math.Max(460f, y + 80f);
        }

        static void DrawDownloadProgress(Graphics g, string status, int progress, int x, int y, int w)
        {
            float s = _dpiScale;
            int barH = 8;
            using var bgPath = UIHelper.GetRoundedRectPath(new RectangleF(x * s, y * s, w * s, barH * s), 4 * s);
            using var bgBrush = new SolidBrush(Color.FromArgb(45, 45, 45));
            g.FillPath(bgBrush, bgPath);

            int fillW = Math.Max(4, (int)(w * Math.Max(0, Math.Min(100, progress)) / 100f));
            using var fillPath = UIHelper.GetRoundedRectPath(new RectangleF(x * s, y * s, fillW * s, barH * s), 4 * s);
            using var fillBrush = new SolidBrush(UIHelper.GetSystemColorizationColor());
            g.FillPath(fillBrush, fillPath);
        }

        static string ShortPath(string value, int maxLength)
        {
            if (value.Length <= maxLength)
                return value;
            return "..." + value.Substring(value.Length - maxLength + 3);
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
        static void DrawSubGroupLabel(Graphics g, string text, float x, float y)
        {
            float s = _dpiScale;
            if (_subFont == null) return;

            // Measure text width for pill background
            var textSize = g.MeasureString(text, _subFont);
            float pillW = textSize.Width / s + 18f;
            float pillH = 22f;

            using var pillPath = UIHelper.GetRoundedRectPath(
                new RectangleF(x * s, y * s, pillW * s, pillH * s), 4 * s);
            using var pillBrush = new SolidBrush(Color.FromArgb(50, 50, 50));
            g.FillPath(pillBrush, pillPath);

            using var borderPen = new Pen(Color.FromArgb(70, 70, 70));
            g.DrawPath(borderPen, pillPath);

            Color accentColor = UIHelper.GetSystemColorizationColor();
            using var textBrush = new SolidBrush(accentColor);
            g.DrawString(text, _subFont, textBrush, (x + 9) * s, (y + (pillH - textSize.Height / s) / 2f) * s);
        }

        static int GetPdfCompressLevel()
        {
            string levelStr = ClickraStorage.GetSetting("PdfCompressImageLevel");
            if (int.TryParse(levelStr, out int lvl) && lvl >= 0 && lvl <= 3)
                return lvl;
            // Backward compat: derive from DPI setting
            return ClickraStorage.GetSetting("PdfCompressTargetDpi") switch {
                "300" => 3,
                "150" => 2,
                "0" => 0,
                _ => 1  // default: 120 DPI = level 1 (小檔)
            };
        }

        static void DrawCompressSlider(Graphics g, float x, float y, float w, int level)
        {
            float s = _dpiScale;
            const int stops = 4;
            float trackY = y + 18f;   // guidance labels occupy top 18px
            float trackH = 5f;
            Color accent = UIHelper.GetSystemColorizationColor();

            // Guidance labels from localization
            if (_subFont != null)
            {
                string leftLabel  = GetText("setting_pdf_compress_smaller");
                string rightLabel = GetText("setting_pdf_compress_higher");
                using var dimBrush = new SolidBrush(Color.FromArgb(110, 110, 110));
                g.DrawString(leftLabel, _subFont, dimBrush, x * s, y * s);
                var rSize = g.MeasureString(rightLabel, _subFont);
                g.DrawString(rightLabel, _subFont, dimBrush,
                    (x + w - rSize.Width / s) * s, y * s);
            }

            // Track background
            using var bgPath = UIHelper.GetRoundedRectPath(
                new RectangleF(x * s, trackY * s, w * s, trackH * s), (trackH / 2f) * s);
            using var bgBrush = new SolidBrush(Color.FromArgb(55, 55, 55));
            g.FillPath(bgBrush, bgPath);

            // Filled portion (left of active stop)
            float thumbX = x + (float)level / (stops - 1) * w;
            float fillW = thumbX - x;
            if (fillW > 0.5f)
            {
                using var fillPath = UIHelper.GetRoundedRectPath(
                    new RectangleF(x * s, trackY * s, fillW * s, trackH * s), (trackH / 2f) * s);
                using var fillBrush = new SolidBrush(accent);
                g.FillPath(fillBrush, fillPath);
            }

            // Stop dots + labels below from localization
            string[] stopLabels = new[]
            {
                GetText("setting_pdf_compress_level_min"),
                GetText("setting_pdf_compress_level_small"),
                GetText("setting_pdf_compress_level_std"),
                GetText("setting_pdf_compress_level_high")
            };

            for (int i = 0; i < stops; i++)
            {
                float sx = x + (float)i / (stops - 1) * w;
                bool active = (i == level);

                // Dot
                float dotR = active ? 7.5f : 3.5f;
                Color dotColor = i <= level ? accent : Color.FromArgb(65, 65, 65);
                if (active)
                {
                    // White ring around active thumb
                    using var ringBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                    g.FillEllipse(ringBrush,
                        (sx - dotR - 2f) * s, (trackY + trackH / 2f - dotR - 2f) * s,
                        (dotR + 2f) * 2f * s, (dotR + 2f) * 2f * s);
                }
                using var dotBrush = new SolidBrush(dotColor);
                g.FillEllipse(dotBrush,
                    (sx - dotR) * s, (trackY + trackH / 2f - dotR) * s,
                    dotR * 2f * s, dotR * 2f * s);

                // Label
                if (_subFont != null)
                {
                    using var lBrush = new SolidBrush(active ? Color.White : Color.FromArgb(95, 95, 95));
                    var lSize = g.MeasureString(stopLabels[i], _subFont);
                    g.DrawString(stopLabels[i], _subFont, lBrush,
                        (sx - lSize.Width / s / 2f) * s,
                        (trackY + trackH / 2f + dotR + 5f) * s);
                }
            }
        }
    }
}

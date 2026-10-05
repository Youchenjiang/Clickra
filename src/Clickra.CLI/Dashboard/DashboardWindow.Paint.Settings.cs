using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using Microsoft.Win32;
using Clickra.Core;
using Clickra.Core.Layout;
using Clickra.Core.Processors;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        private const string ParkedRetentionDaysTextKey = "setting_parked_ttl_days";

        // skipcq: CS-R1140
        static void DrawSettingsTab(Graphics g, float logW, float logH, float contentX)
        {
            float s = _dpiScale;
            _settingsHitRects.Clear();

            void AddHitRect(int elementId, float x, float y, float w, float h)
            {
                _settingsHitRects[elementId] = new RectangleF(x, y, w, h);
            }

            // 版面表算出來的矩形直接登記為命中區：畫的位置與登記的位置不可能不一致。
            void AddLayoutHitRect(int elementId, LayoutRect rect)
            {
                AddHitRect(elementId, rect.X, rect.Y, rect.Width, rect.Height);
            }

            void DrawSectionHeader(string titleKey, string descKey, float y)
                => DrawSectionHeaderAt(titleKey, descKey, contentX, y);

            void DrawSectionHeaderAt(string titleKey, string descKey, float x, float y)
            {
                if (_tabFont != null)
                    g.DrawString(GetText(titleKey), _tabFont, Brushes.White, x * s, y * s);
                if (_subFont != null)
                {
                    using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    g.DrawString(GetText(descKey), _subFont, subBrush, x * s, (y + SettingsLayout.HeaderDescriptionOffset) * s);
                }
            }

            void DrawGroupSubheader(string titleKey, float y)
            {
                if (_subFont != null)
                {
                    using var subHeaderBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
                    g.DrawString(GetText(titleKey), _subFont, subHeaderBrush, contentX * s, y * s);
                }
            }

            void DrawToggleSection(string titleKey, string descKey, bool state, int elementId, float y)
            {
                DrawSectionHeader(titleKey, descKey, y);
                LayoutRect toggle = SettingsLayout.ToggleRect((int)logW, (int)y);
                DrawToggleSwitch(g, state, _hoveredElement == elementId, toggle.X, toggle.Y, toggle.Width, toggle.Height);
                AddLayoutHitRect(elementId, toggle);
            }

            void DrawCard(LayoutRect rect)
            {
                using var path = UIHelper.GetRoundedRectPath(
                    new RectangleF(rect.X * s, rect.Y * s, rect.Width * s, rect.Height * s), 6 * s);
                using var fill = new SolidBrush(Color.FromArgb(38, 38, 38));
                using var border = new Pen(Color.FromArgb(54, 54, 54));
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            void DrawCompactToggleRow(string titleKey, bool state, int elementId, int x, int y, int width)
            {
                if (_tabFont != null)
                    g.DrawString(GetText(titleKey), _tabFont, Brushes.White, x * s, y * s);

                LayoutRect toggle = SettingsLayout.ToggleRectWithin(x, width, y);
                DrawToggleSwitch(g, state, _hoveredElement == elementId, toggle.X, toggle.Y, toggle.Width, toggle.Height);
                AddLayoutHitRect(elementId, toggle);
            }

            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_settings"), _contentTitleFont, Brushes.White, contentX * s, 30 * s);

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, contentX * s, 75 * s, (logW - 40) * s, 75 * s);
            }

            float y = SettingsLayout.ContentTop;
            float margin = SettingsLayout.InlineGap;
            bool wideSettings = SettingsLayout.IsWide((int)logW);
            int settingsColumnWidth = wideSettings
                ? SettingsLayout.ColumnWidth((int)contentX, (int)logW)
                : (int)(logW - contentX - SettingsLayout.ContentRightMargin);

            bool quietMode = ClickraStorage.GetSettingBool(ClickraSettings.QuietMode);
            bool notification = ClickraStorage.GetSettingBool(ClickraSettings.Notification);
            if (wideSettings)
            {
                int leftX = (int)contentX;
                int rightX = SettingsLayout.ColumnX((int)contentX, (int)logW, 1);
                int cardY = (int)y;
                LayoutRect behaviorCard = SettingsLayout.CardRect(leftX, cardY, settingsColumnWidth, SettingsLayout.OverviewCardHeight);
                LayoutRect languageCard = SettingsLayout.CardRect(rightX, cardY, settingsColumnWidth, SettingsLayout.OverviewCardHeight);
                DrawCard(behaviorCard);
                DrawCard(languageCard);

                int behaviorX = behaviorCard.X + SettingsLayout.CardPadding;
                int behaviorWidth = behaviorCard.Width - 2 * SettingsLayout.CardPadding;
                DrawCompactToggleRow("setting_silent_title", quietMode, 5, behaviorX, behaviorCard.Y + 16, behaviorWidth);
                DrawCompactToggleRow("setting_notify_title", notification, 6, behaviorX,
                    behaviorCard.Y + 16 + SettingsLayout.OverviewRowGap, behaviorWidth);

                int languageX = languageCard.X + SettingsLayout.CardPadding;
                int languageWidth = languageCard.Width - 2 * SettingsLayout.CardPadding;
                if (_tabFont != null)
                {
                    g.DrawString(GetText("setting_lang_title"), _tabFont, Brushes.White, languageX * s, (languageCard.Y + 12) * s);
                    g.DrawString(GetText("setting_pdf_title"), _tabFont, Brushes.White, languageX * s,
                        (languageCard.Y + 12 + SettingsLayout.OverviewRowGap) * s);
                }

                _langDropdownX = languageX;
                _langDropdownY = languageCard.Y + 36;
                _langDropdownWidth = languageWidth;
                DrawLanguageDropdown(g, _langDropdownY, _langDropdownX, _langDropdownWidth);
                AddLayoutHitRect(10, DashboardLayout.DropdownButtonRect(_langDropdownX, _langDropdownY, _langDropdownWidth));

                _pdfLangDropdownX = languageX;
                _pdfLangDropdownY = languageCard.Y + 36 + SettingsLayout.OverviewRowGap;
                _pdfLangDropdownWidth = languageWidth;
                DrawPdfLangDropdown(g, _pdfLangDropdownY, _pdfLangDropdownX, _pdfLangDropdownWidth);
                AddLayoutHitRect(31, DashboardLayout.DropdownButtonRect(_pdfLangDropdownX, _pdfLangDropdownY, _pdfLangDropdownWidth));

                y += SettingsLayout.OverviewCardHeight + SettingsLayout.CardGap;
            }
            else
            {
                DrawToggleSection("setting_silent_title", "setting_silent_desc", quietMode, 5, y);
                y += SettingsLayout.PrimaryToggleSectionHeight;
                DrawToggleSection("setting_notify_title", "setting_notify_desc", notification, 6, y);
                y += SettingsLayout.PrimaryToggleSectionHeight;
            }

            int outputContentX = (int)contentX;
            if (wideSettings)
            {
                LayoutRect outputCard = SettingsLayout.CardRect(
                    (int)contentX,
                    (int)y,
                    (int)(logW - contentX - SettingsLayout.ContentRightMargin),
                    SettingsLayout.ChoiceCardHeight);
                DrawCard(outputCard);
                outputContentX = outputCard.X + SettingsLayout.CardPadding;
                if (_tabFont != null)
                    g.DrawString(GetText("setting_output_title"), _tabFont, Brushes.White,
                        outputContentX * s, (outputCard.Y + 10) * s);
            }
            else
            {
                DrawSectionHeader("setting_output_title", "setting_output_desc", y);
            }

            string outputDirMode = ClickraStorage.GetSetting(ClickraSettings.OutputDir);
            bool isSource = outputDirMode.Equals(ClickraSettings.DefaultOutputDirSource, StringComparison.OrdinalIgnoreCase);
            bool isDesktop = outputDirMode.Equals(ClickraSettings.OutputDirDesktop, StringComparison.OrdinalIgnoreCase);
            bool isDownloads = outputDirMode.Equals(ClickraSettings.OutputDirDownloads, StringComparison.OrdinalIgnoreCase);
            bool isCustom = !isSource && !isDesktop && !isDownloads;

            string textSource = GetText("setting_output_same_as_source");
            string textDesktop = GetText("setting_output_desktop");
            string textDownloads = GetText("setting_output_downloads");
            string textCustom = GetText("setting_output_custom");

            float wSource = _wSource;
            float wDesktop = _wDesktop;
            float wDownloads = _wDownloads;
            float wCustom = _wCustom;

            float xSource = outputContentX;
            float xDesktop = xSource + wSource + margin;
            float xDownloads = xDesktop + wDesktop + margin;
            float xCustom = xDownloads + wDownloads + margin;
            float buttonY = wideSettings ? y + 34f : y + SettingsLayout.ControlTopOffset;

            DrawOutputDirButton(g, textSource, isSource, 7, (int)xSource, (int)buttonY, (int)wSource);
            DrawOutputDirButton(g, textDesktop, isDesktop, 8, (int)xDesktop, (int)buttonY, (int)wDesktop);
            DrawOutputDirButton(g, textDownloads, isDownloads, 9, (int)xDownloads, (int)buttonY, (int)wDownloads);
            DrawOutputDirButton(g, textCustom, isCustom, 20, (int)xCustom, (int)buttonY, (int)wCustom);
            AddLayoutHitRect(7, SettingsLayout.ButtonRect((int)xSource, (int)buttonY, (int)wSource));
            AddLayoutHitRect(8, SettingsLayout.ButtonRect((int)xDesktop, (int)buttonY, (int)wDesktop));
            AddLayoutHitRect(9, SettingsLayout.ButtonRect((int)xDownloads, (int)buttonY, (int)wDownloads));
            AddLayoutHitRect(20, SettingsLayout.ButtonRect((int)xCustom, (int)buttonY, (int)wCustom));

            y += wideSettings
                ? SettingsLayout.ChoiceCardHeight + SettingsLayout.CardGap
                : SettingsLayout.PrimaryChoiceSectionHeight;
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
                    g.DrawString($"{GetText("setting_output_selected_path")}: {displayText}", _subFont, pathBrush, outputContentX * s, y * s);
                }
                y += 24f;
            }

            DrawSectionHeader("setting_engine_title", "setting_engine_desc", y);

            string engineMode = ClickraStorage.GetSetting(ClickraSettings.OfficeEngine);
            bool isAutoEngine = string.IsNullOrEmpty(engineMode) || engineMode.Equals(ClickraSettings.DefaultOfficeEngineAuto, StringComparison.OrdinalIgnoreCase);
            bool isMicrosoftEngine = engineMode.Equals(ClickraSettings.OfficeEngineMicrosoft, StringComparison.OrdinalIgnoreCase);
            bool isLibreOfficeEngine = engineMode.Equals(ClickraSettings.OfficeEngineLibreOffice, StringComparison.OrdinalIgnoreCase);

            float xEngineAuto = contentX;
            float xEngineMicrosoft = xEngineAuto + _wEngineAuto + margin;
            float xEngineLibreOffice = xEngineMicrosoft + _wEngineMicrosoft + margin;
            float engineButtonY = y + SettingsLayout.ControlTopOffset;

            DrawOutputDirButton(g, GetText("setting_engine_auto"), isAutoEngine, 32, (int)xEngineAuto, (int)engineButtonY, (int)_wEngineAuto);
            DrawOutputDirButton(g, GetText("setting_engine_microsoft"), isMicrosoftEngine, 33, (int)xEngineMicrosoft, (int)engineButtonY, (int)_wEngineMicrosoft);
            DrawOutputDirButton(g, GetText("setting_engine_libreoffice"), isLibreOfficeEngine, 34, (int)xEngineLibreOffice, (int)engineButtonY, (int)_wEngineLibreOffice);
            AddLayoutHitRect(32, SettingsLayout.ButtonRect((int)xEngineAuto, (int)engineButtonY, (int)_wEngineAuto));
            AddLayoutHitRect(33, SettingsLayout.ButtonRect((int)xEngineMicrosoft, (int)engineButtonY, (int)_wEngineMicrosoft));
            AddLayoutHitRect(34, SettingsLayout.ButtonRect((int)xEngineLibreOffice, (int)engineButtonY, (int)_wEngineLibreOffice));

            y += SettingsLayout.PrimaryChoiceSectionHeight;
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
            bool removalPendingRestart = ClickraStorage.GetSettingBool(ClickraSettings.LibreOfficeRemovalPendingRestart);
            bool libreOfficeReady = !string.IsNullOrEmpty(resolvedLibreOffice);
            // Only a LibreOffice with a freshly verified Clickra management identity may be removed
            // from the dashboard; all other installations remain user-managed.
            bool libreOfficeInstalledByClickra = LibreOfficeEngineInstaller.WasInstalledByClickra();
            bool canAdoptLibreOffice = !libreOfficeInstalledByClickra &&
                                       LibreOfficeEngineInstaller.CanAdoptExistingInstallation(resolvedLibreOffice);
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

                    if (libreOfficeInstalledByClickra)
                    {
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
                    }
                    else if (canAdoptLibreOffice)
                    {
                        float adoptX = contentX + _wLibreOfficeDownload + margin;
                        DrawOutputDirButton(
                            g,
                            GetText("setting_libreoffice_adopt"),
                            false,
                            39,
                            (int)adoptX,
                            (int)y,
                            (int)_wLibreOfficeAdopt);
                        AddHitRect(39, adoptX, y, _wLibreOfficeAdopt, 30);

                    }
                    if (!libreOfficeInstalledByClickra && _subFont != null)
                    {
                        // Explain external installation provenance even when a custom/portable install
                        // cannot safely be adopted for system-MSI management.
                        using var externalBrush = new SolidBrush(Color.FromArgb(150, 150, 150));
                        g.DrawString(GetText("setting_libreoffice_external_hint"), _subFont, externalBrush, contentX * s, (y + 32f) * s);
                    }
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

            if (!wideSettings)
            {
                float languageY = y;
                float languageX = contentX;
                int languageDropdownWidth = DashboardLayout.DropdownWidth;
                float pdfLanguageY = y + SettingsLayout.LanguageSectionHeight;

                DrawSectionHeaderAt("setting_lang_title", "setting_lang_desc", languageX, languageY);
                _langDropdownX = (int)languageX;
                _langDropdownY = (int)(languageY + SettingsLayout.LanguageDropdownOffset);
                _langDropdownWidth = languageDropdownWidth;
                DrawLanguageDropdown(g, _langDropdownY, languageX, _langDropdownWidth);
                AddLayoutHitRect(10, DashboardLayout.DropdownButtonRect(_langDropdownX, _langDropdownY, _langDropdownWidth));

                DrawSectionHeaderAt("setting_pdf_title", "setting_pdf_desc", contentX, pdfLanguageY);
                if (_subFont != null)
                {
                    using var pdfLangLabelBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
                    g.DrawString(GetText("setting_pdf_lang"), _subFont, pdfLangLabelBrush,
                        contentX * s, (pdfLanguageY + SettingsLayout.PdfLanguageLabelOffset) * s);
                }
                _pdfLangDropdownX = (int)contentX;
                _pdfLangDropdownY = (int)(pdfLanguageY + SettingsLayout.PdfLanguageDropdownOffset);
                _pdfLangDropdownWidth = languageDropdownWidth;
                DrawPdfLangDropdown(g, _pdfLangDropdownY, contentX, _pdfLangDropdownWidth);
                AddLayoutHitRect(31, DashboardLayout.DropdownButtonRect(_pdfLangDropdownX, _pdfLangDropdownY, _pdfLangDropdownWidth));

                y += SettingsLayout.LanguageSectionHeight + SettingsLayout.PdfLanguageSectionHeight;
            }

            // Fluent UI section
            bool fluentAvailable = Clickra.Core.FluentRuntimeHelper.IsAvailable();
            if (wideSettings)
            {
                LayoutRect fluentCard = SettingsLayout.CardRect(
                    (int)contentX,
                    (int)y,
                    (int)(logW - contentX - SettingsLayout.ContentRightMargin),
                    SettingsLayout.FluentCardHeight);
                DrawCard(fluentCard);
                int fluentX = fluentCard.X + SettingsLayout.CardPadding;
                if (_subFont != null)
                {
                    if (_tabFont != null)
                        g.DrawString(GetText("setting_fluent_title"), _tabFont, Brushes.White,
                            fluentX * s, (fluentCard.Y + 10) * s);
                    using var statusBrush = new SolidBrush(fluentAvailable
                        ? Color.FromArgb(100, 220, 100)
                        : Color.FromArgb(255, 190, 90));
                    g.DrawString(GetText(fluentAvailable ? "setting_fluent_ready" : "setting_fluent_not_installed"),
                        _subFont, statusBrush, fluentX * s, (fluentCard.Y + 42) * s);
                }

                if (!fluentAvailable && Clickra.Core.FluentRuntimeHelper.SupportsStoreFluentAddon())
                {
                    int installWidth = 200;
                    int installX = fluentCard.Right - SettingsLayout.CardPadding - installWidth;
                    int installY = fluentCard.Y + 32;
                    DrawOutputDirButton(g, GetText("setting_fluent_install"), false, 40,
                        installX, installY, installWidth);
                    AddLayoutHitRect(40, SettingsLayout.ButtonRect(installX, installY, installWidth));
                }

                y += SettingsLayout.FluentCardHeight + SettingsLayout.CardGap;
            }
            else
            {
                DrawSectionHeader("setting_fluent_title", "setting_fluent_desc", y);
                y += 50f;

                if (fluentAvailable)
                {
                    if (_subFont != null)
                    {
                        using var statusBrush = new SolidBrush(Color.FromArgb(100, 220, 100));
                        g.DrawString(GetText("setting_fluent_ready"), _subFont, statusBrush, contentX * s, y * s);
                    }
                }
                else
                {
                    if (_subFont != null)
                    {
                        using var statusBrush = new SolidBrush(Color.FromArgb(255, 190, 90));
                        g.DrawString(GetText("setting_fluent_not_installed"), _subFont, statusBrush, contentX * s, y * s);
                    }
                    if (Clickra.Core.FluentRuntimeHelper.SupportsStoreFluentAddon())
                    {
                        y += 28f;
                        DrawOutputDirButton(
                            g,
                            GetText("setting_fluent_install"),
                            false,
                            40,
                            (int)contentX,
                            (int)y,
                            200);
                        AddHitRect(40, contentX, y, 200, 30);
                    }
                }
                y += 48f;
            }

            if (wideSettings)
            {
                int cardY = (int)y;
                int leftX = (int)contentX;
                int fullWidth = (int)(logW - contentX - SettingsLayout.ContentRightMargin);
                LayoutRect pdfCard = SettingsLayout.CardRect(
                    leftX, cardY, fullWidth, SettingsLayout.CompressionCardHeight);
                DrawCard(pdfCard);

                int pdfX = pdfCard.X + SettingsLayout.CardPadding;
                if (_tabFont != null)
                {
                    g.DrawString(GetText("setting_pdf_compress_title"), _tabFont, Brushes.White,
                        pdfX * s, (pdfCard.Y + 12) * s);
                }

                int columnInnerWidth = settingsColumnWidth - 2 * SettingsLayout.CardPadding;
                int sliderWidth = SettingsLayout.SliderWidthFor(columnInnerWidth);
                int pdfSliderY = pdfCard.Y + SettingsLayout.CompressionSliderTop;
                int compressLevel = ConvertCommandRegistry.GetPdfCompressLevel();
                _pdfSliderTrackX = pdfX;
                _pdfSliderTrackW = sliderWidth;
                DrawCompressSlider(g, pdfX, pdfSliderY, sliderWidth, compressLevel);
                AddLayoutHitRect(83, SettingsLayout.SliderHitRect(pdfX, pdfSliderY, sliderWidth));

                bool stripFonts = ClickraStorage.GetSettingBool(ClickraSettings.PdfCompressStripFonts);
                bool minifyContent = ClickraStorage.GetSettingBool(ClickraSettings.PdfCompressMinifyContent);
                int pdfSecondaryX = SettingsLayout.ColumnX((int)contentX, (int)logW, 1)
                    + SettingsLayout.CardPadding;
                int pdfSecondaryY = pdfCard.Y + SettingsLayout.CompressionSecondaryTop;
                DrawCompactToggleRow("setting_pdf_compress_strip_fonts", stripFonts, 81,
                    pdfSecondaryX, pdfSecondaryY, columnInnerWidth);
                DrawCompactToggleRow("setting_pdf_compress_minify_content", minifyContent, 82,
                    pdfSecondaryX, pdfSecondaryY + SettingsLayout.OverviewRowGap, columnInnerWidth);

                y += SettingsLayout.CompressionCardHeight + SettingsLayout.CardGap;
            }
            else
            {
                float pdfCompressionX = contentX;
                float pdfCompressionY = y;
                DrawSectionHeaderAt("setting_pdf_compress_title", "setting_pdf_compress_desc", pdfCompressionX, pdfCompressionY);
                pdfCompressionY += 50f;
                DrawGroupSubheader("setting_pdf_compress_group_image", pdfCompressionY);
                pdfCompressionY += 24f;

                int compressLevel = ConvertCommandRegistry.GetPdfCompressLevel();
                float sliderW = SettingsLayout.SliderWidth;
                _pdfSliderTrackX = pdfCompressionX;
                _pdfSliderTrackW = sliderW;
                DrawCompressSlider(g, pdfCompressionX, pdfCompressionY, sliderW, compressLevel);
                AddLayoutHitRect(83, SettingsLayout.SliderHitRect((int)pdfCompressionX, (int)pdfCompressionY, (int)sliderW));
                pdfCompressionY += SettingsLayout.SliderSectionHeight;
                DrawGroupSubheader("setting_pdf_compress_group_other", pdfCompressionY);
                pdfCompressionY += 24f;

                bool stripFonts = ClickraStorage.GetSettingBool(ClickraSettings.PdfCompressStripFonts);
                DrawToggleSection("setting_pdf_compress_strip_fonts", "", stripFonts, 81, pdfCompressionY);
                pdfCompressionY += SettingsLayout.CompactToggleSectionHeight;
                bool minifyContent = ClickraStorage.GetSettingBool(ClickraSettings.PdfCompressMinifyContent);
                DrawToggleSection("setting_pdf_compress_minify_content", "", minifyContent, 82, pdfCompressionY);
                pdfCompressionY += SettingsLayout.CompactToggleSectionHeight;
                y = pdfCompressionY + SettingsLayout.CardGap;
            }

            // Parked Task Retention Section
            y += 2f;
            DrawSectionHeader("setting_parked_ttl_title", "setting_parked_ttl_desc", y);
            y += 50f;

            int currentDays = ClickraStorage.GetParkedRetentionDays();
            int defaultDays = GetDefaultParkedRetentionDays();
            string currentDaysText = currentDays switch
            {
                0 => GetText("setting_parked_ttl_unlimited"),
                1 => string.Format(GetText("setting_parked_ttl_day_single"), currentDays),
                _ => string.Format(GetText(ParkedRetentionDaysTextKey), currentDays)
            };
            string currentLabel = string.Format(GetText("setting_parked_ttl_current"), currentDaysText);

            if (_subFont != null)
            {
                using var currentBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                g.DrawString(currentLabel, _subFont, currentBrush, contentX * s, y * s);
            }
            y += 28f;

            float curX = contentX;
            float btnY = y;
            float wStep = 34f;

            DrawOutputDirButton(g, "-", false, 90, (int)curX, (int)btnY, (int)wStep);
            AddHitRect(90, curX, btnY, wStep, 30);
            curX += wStep + margin;

            DrawOutputDirButton(g, "+", false, 91, (int)curX, (int)btnY, (int)wStep);
            AddHitRect(91, curX, btnY, wStep, 30);
            curX += wStep + margin + 8f;

            (int days, int elemId, string label)[] presets = new[]
            {
                (0, 92, GetText("setting_parked_ttl_unlimited")),
                (3, 93, string.Format(GetText(ParkedRetentionDaysTextKey), 3)),
                (defaultDays, 94, string.Format(GetText("setting_parked_ttl_default"), defaultDays)),
                (14, 95, string.Format(GetText(ParkedRetentionDaysTextKey), 14)),
                (30, 96, string.Format(GetText(ParkedRetentionDaysTextKey), 30))
            };

            var measureFont = _subFont ?? SystemFonts.DefaultFont;
            foreach (var (days, elemId, label) in presets)
            {
                float btnW = Math.Max(50f, g.MeasureString(label, measureFont).Width / s + 20f);
                bool isSelected = (currentDays == days);
                DrawOutputDirButton(g, label, isSelected, elemId, (int)curX, (int)btnY, (int)btnW);
                AddHitRect(elemId, curX, btnY, btnW, 30);
                curX += btnW + margin;
            }

            y += 50f;

            // Dynamic settings from SettingPageRegistry
            for (int i = 0; i < SettingPageRegistry.AllDescriptors.Count; i++)
            {
                var descriptor = SettingPageRegistry.AllDescriptors[i];
                if (LegacyPaintedSettings.Contains(descriptor.Key)) continue;
                if (descriptor.Key.Equals(ClickraSettings.ImageCompressLevel, StringComparison.OrdinalIgnoreCase) ||
                    descriptor.Key.Equals(ClickraSettings.ImageCompressMaxDimension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DrawDynamicSettingDescriptor(g, descriptor, i, logW, contentX, margin, ref y);
            }

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

        static void DrawCompressSlider(Graphics g, float x, float y, float w, int level)
        {
            float s = _dpiScale;
            int stops = ClickraSettings.MaxPdfCompressLevel - ClickraSettings.MinPdfCompressLevel + 1;
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
            float thumbX = x + (float)(level - ClickraSettings.MinPdfCompressLevel) / (stops - 1) * w;
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
                GetText("setting_pdf_compress_level_small"),
                GetText("setting_pdf_compress_level_std"),
                GetText("setting_pdf_compress_level_high")
            };

            for (int i = 0; i < stops; i++)
            {
                float sx = x + (float)i / (stops - 1) * w;
                bool active = (i + ClickraSettings.MinPdfCompressLevel == level);

                float dotR = DrawSliderStopMarker(g, sx, trackY, trackH, active, i <= level, accent);

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

        private static readonly HashSet<string> LegacyPaintedSettings = new(StringComparer.OrdinalIgnoreCase)
        {
            ClickraSettings.QuietMode,
            ClickraSettings.Notification,
            ClickraSettings.OutputDir,
            ClickraSettings.OfficeEngine,
            ClickraSettings.Language,
            ClickraSettings.TranslateTargetLang,
            ClickraSettings.PdfCompressImageLevel,
            ClickraSettings.PdfCompressStripFonts,
            ClickraSettings.PdfCompressMinifyContent,
            ClickraSettings.ParkedTaskRetention,
        };

        static void DrawDynamicSettingDescriptor(
            Graphics g,
            SettingDescriptor descriptor,
            int descriptorIndex,
            float logW,
            float contentX,
            float margin,
            ref float y,
            float? sliderWidth = null)
        {
            int baseElemId = 1000 + descriptorIndex * 10;
            switch (descriptor.EditorKind)
            {
                case SettingEditorKind.Toggle:
                    DrawDynamicToggleSetting(g, descriptor, logW, contentX, baseElemId, ref y);
                    break;
                case SettingEditorKind.Slider:
                    DrawDynamicSliderSetting(g, descriptor, contentX, baseElemId, ref y, sliderWidth);
                    break;
                case SettingEditorKind.Number:
                    DrawDynamicNumberSetting(g, descriptor, contentX, margin, baseElemId, ref y);
                    break;
                case SettingEditorKind.Choice:
                    DrawDynamicChoiceSetting(g, descriptor, contentX, margin, baseElemId, ref y);
                    break;
                default:
                    // Ignore unsupported future editor kinds until a renderer is defined.
                    break;
            }
        }

        static void DrawDynamicSettingHeader(Graphics g, SettingDescriptor descriptor, float contentX, float y)
        {
            float s = _dpiScale;
            if (_tabFont != null)
                g.DrawString(GetText(descriptor.TitleKey), _tabFont, Brushes.White, contentX * s, y * s);
            if (string.IsNullOrEmpty(descriptor.DescriptionKey) || _subFont == null) return;

            using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
            g.DrawString(GetText(descriptor.DescriptionKey), _subFont, subBrush, contentX * s, (y + SettingsLayout.HeaderDescriptionOffset) * s);
        }

        static void DrawDynamicToggleSetting(
            Graphics g, SettingDescriptor descriptor, float logW, float contentX, int baseElemId, ref float y)
        {
            DrawDynamicSettingHeader(g, descriptor, contentX, y);
            LayoutRect toggle = SettingsLayout.ToggleRect((int)logW, (int)y);
            bool state = ClickraStorage.GetSettingBool(descriptor.Key);
            DrawToggleSwitch(g, state, _hoveredElement == baseElemId, toggle.X, toggle.Y, toggle.Width, toggle.Height);
            _settingsHitRects[baseElemId] = new RectangleF(toggle.X, toggle.Y, toggle.Width, toggle.Height);
            y += SettingsLayout.ToggleSectionHeight;
        }

        static void DrawDynamicSliderSetting(
            Graphics g, SettingDescriptor descriptor, float contentX, int baseElemId, ref float y, float? sliderWidth)
        {
            DrawDynamicSettingHeader(g, descriptor, contentX, y);
            y += SettingsLayout.SliderHeaderGap;

            float sliderW = sliderWidth ?? SettingsLayout.SliderWidth;
            _dynamicSliderTrackX = contentX;
            _dynamicSliderTrackW = sliderW;
            var range = descriptor.GetEffectiveNumericRange() ?? new NumericSettingRange(0, 1, 0);
            int currentLevel = Math.Clamp(ClickraStorage.GetSettingInt(descriptor.Key), range.Min, range.Max);
            DrawDynamicSlider(g, contentX, y, sliderW, descriptor, currentLevel, range);
            LayoutRect sliderHit = SettingsLayout.SliderHitRect((int)contentX, (int)y, (int)sliderW);
            _settingsHitRects[baseElemId] = new RectangleF(sliderHit.X, sliderHit.Y, sliderHit.Width, sliderHit.Height);
            y += SettingsLayout.SliderSectionHeight;
        }

        static void DrawDynamicNumberSetting(
            Graphics g, SettingDescriptor descriptor, float contentX, float margin, int baseElemId, ref float y)
        {
            DrawDynamicSettingHeader(g, descriptor, contentX, y);
            y += SettingsLayout.DynamicNumberHeaderGap;

            var range = descriptor.GetEffectiveNumericRange() ?? new NumericSettingRange(0, 100, 0);
            int value = Math.Clamp(ClickraStorage.GetSettingInt(descriptor.Key), range.Min, range.Max);
            if (_subFont != null)
            {
                using var valBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                g.DrawString(value.ToString(), _subFont, valBrush, contentX * _dpiScale, y * _dpiScale);
            }
            y += SettingsLayout.DynamicNumberValueHeight;

            float buttonWidth = SettingsLayout.DynamicNumberButtonWidth;
            float buttonY = y;
            DrawOutputDirButton(g, "-", false, baseElemId + 1, (int)contentX, (int)buttonY, (int)buttonWidth);
            LayoutRect minusRect = SettingsLayout.ButtonRect((int)contentX, (int)buttonY, (int)buttonWidth);
            _settingsHitRects[baseElemId + 1] = new RectangleF(minusRect.X, minusRect.Y, minusRect.Width, minusRect.Height);
            float plusX = contentX + buttonWidth + margin;
            DrawOutputDirButton(g, "+", false, baseElemId + 2, (int)plusX, (int)buttonY, (int)buttonWidth);
            LayoutRect plusRect = SettingsLayout.ButtonRect((int)plusX, (int)buttonY, (int)buttonWidth);
            _settingsHitRects[baseElemId + 2] = new RectangleF(plusRect.X, plusRect.Y, plusRect.Width, plusRect.Height);
            y += SettingsLayout.DynamicNumberSectionTail;
        }

        static void DrawDynamicChoiceSetting(
            Graphics g, SettingDescriptor descriptor, float contentX, float margin, int baseElemId, ref float y)
        {
            DrawDynamicSettingHeader(g, descriptor, contentX, y);
            y += SettingsLayout.ControlTopOffset;

            string currentValue = ClickraStorage.GetSetting(descriptor.Key);
            float buttonX = contentX;
            var options = descriptor.Options ?? Array.Empty<SettingOption>();
            for (int optionIndex = 0; optionIndex < options.Count; optionIndex++)
            {
                var option = options[optionIndex];
                bool selected = string.Equals(currentValue, option.Value, StringComparison.OrdinalIgnoreCase);
                string label = GetText(option.LabelKey);
                if (string.Equals(label, option.LabelKey, StringComparison.Ordinal) && !string.IsNullOrEmpty(option.FallbackText))
                    label = option.FallbackText;

                var measureFont = _subFont ?? SystemFonts.DefaultFont;
                float buttonWidth = Math.Max(60f, g.MeasureString(label, measureFont).Width / _dpiScale + 20f);
                int elementId = baseElemId + optionIndex;
                DrawOutputDirButton(g, label, selected, elementId, (int)buttonX, (int)y, (int)buttonWidth);
                LayoutRect choiceRect = SettingsLayout.ButtonRect((int)buttonX, (int)y, (int)buttonWidth);
                _settingsHitRects[elementId] = new RectangleF(choiceRect.X, choiceRect.Y, choiceRect.Width, choiceRect.Height);
                buttonX += buttonWidth + margin;
            }
            y += SettingsLayout.DynamicChoiceSectionHeight;
        }

        static void DrawDynamicSlider(Graphics g, float x, float y, float w, SettingDescriptor descriptor, int level, NumericSettingRange range)
        {
            float s = _dpiScale;
            int stops = Math.Max(2, range.Max - range.Min + 1);
            float trackY = y + 18f;
            float trackH = 5f;
            Color accent = UIHelper.GetSystemColorizationColor();

            // Track background
            using var bgPath = UIHelper.GetRoundedRectPath(
                new RectangleF(x * s, trackY * s, w * s, trackH * s), (trackH / 2f) * s);
            using var bgBrush = new SolidBrush(Color.FromArgb(55, 55, 55));
            g.FillPath(bgBrush, bgPath);

            // Filled portion
            float thumbX = x + (float)(level - range.Min) / (stops - 1) * w;
            float fillW = thumbX - x;
            if (fillW > 0.5f)
            {
                using var fillPath = UIHelper.GetRoundedRectPath(
                    new RectangleF(x * s, trackY * s, fillW * s, trackH * s), (trackH / 2f) * s);
                using var fillBrush = new SolidBrush(accent);
                g.FillPath(fillBrush, fillPath);
            }

            // Stop dots + labels
            var labels = descriptor.SliderLabels;
            for (int i = 0; i < stops; i++)
            {
                float sx = x + (float)i / (stops - 1) * w;
                bool active = (i + range.Min == level);

                float dotR = DrawSliderStopMarker(
                    g, sx, trackY, trackH, active, (i + range.Min) <= level, accent);

                if (_subFont != null && labels != null && i < labels.Count)
                {
                    string label = GetText(labels[i]);
                    using var lBrush = new SolidBrush(active ? Color.White : Color.FromArgb(95, 95, 95));
                    var lSize = g.MeasureString(label, _subFont);
                    g.DrawString(label, _subFont, lBrush,
                        (sx - lSize.Width / s / 2f) * s,
                        (trackY + trackH / 2f + dotR + 5f) * s);
                }
            }
        }

        static float DrawSliderStopMarker(
            Graphics g, float stopX, float trackY, float trackHeight, bool active, bool reached, Color accent)
        {
            float s = _dpiScale;
            float dotRadius = active ? 7.5f : 3.5f;
            if (active)
            {
                using var ringBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                g.FillEllipse(ringBrush,
                    (stopX - dotRadius - 2f) * s, (trackY + trackHeight / 2f - dotRadius - 2f) * s,
                    (dotRadius + 2f) * 2f * s, (dotRadius + 2f) * 2f * s);
            }

            using var dotBrush = new SolidBrush(reached ? accent : Color.FromArgb(65, 65, 65));
            g.FillEllipse(dotBrush,
                (stopX - dotRadius) * s, (trackY + trackHeight / 2f - dotRadius) * s,
                dotRadius * 2f * s, dotRadius * 2f * s);
            return dotRadius;
        }
    }
}

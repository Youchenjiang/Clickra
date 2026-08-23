using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Clickra.Core;
using Clickra.Core.Processors;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        /// <summary>Routes left-button clicks to the active dashboard tab's hit regions.</summary>
        static void HandleLButtonDown(IntPtr hwnd, IntPtr w, IntPtr l)
        {
            int rawX = (short)(l.ToInt64() & 0xFFFF);
            int rawY = (short)((l.ToInt64() >> 16) & 0xFFFF);
            int mouseX = (int)(rawX / _dpiScale);
            int mouseY = (int)(rawY / _dpiScale);

            float logW = GetLogicalWidth(hwnd);
            float sidebarW = GetSidebarWidth(logW);
            float contentX = GetContentX(logW);

            if (HandleScrollbarClick(hwnd, mouseX, mouseY)) return;

            int adjMouseX = mouseX >= sidebarW ? (int)(mouseX + _contentScrollX) : mouseX;
            int adjMouseY = mouseX >= sidebarW ? (int)(mouseY + _contentScrollY) : mouseY;

            if (HandleDropdownClick(hwnd, adjMouseX, adjMouseY, logW, contentX)) return;

            if (_activeTab == 2 && HandleHistoryClick(hwnd, mouseX, adjMouseX, adjMouseY, logW, contentX)) return;

            int element = HitTest(hwnd, adjMouseX, adjMouseY);
            if (IsTabBarElement(element))
            {
                HandleTabBarClick(hwnd, element);
            }
            else if (element == 22)
            {
                HandleHistoryToolbarClick(hwnd);
            }
            else if (IsSettingsElement(element))
            {
                HandleSettingsClick(hwnd, element);
            }
            else if (IsLibreOfficeElement(element))
            {
                HandleLibreOfficeClick(hwnd, element);
            }
            else if (IsDropdownToggleElement(element))
            {
                HandleDropdownToggleClick(hwnd, element);
            }
            else if (IsCompressSettingsElement(element))
            {
                HandleCompressSettingsClick(hwnd, element, adjMouseX);
            }
            else if (IsConvertElement(element))
            {
                HandleConvertClick(hwnd, element);
            }
            else if (IsAboutElement(element))
            {
                HandleAboutClick(hwnd, element);
            }
        }

        /// <summary>True when the element is one of the tab-bar buttons (0-4).</summary>
        static bool IsTabBarElement(int element) => element >= 0 && element <= 4;

        /// <summary>True when the element is one of the settings-page controls.</summary>
        static bool IsSettingsElement(int element)
            => element == 5 || element == 6 || element == 7 || element == 8 || element == 9 ||
               element == 20 || element == 32 || element == 33 || element == 34;

        /// <summary>True when the element is one of the LibreOffice setup buttons.</summary>
        static bool IsLibreOfficeElement(int element) => element == 35 || element == 36 || element == 38;

        /// <summary>True when the element is one of the language dropdown toggles.</summary>
        static bool IsDropdownToggleElement(int element) => element == 10 || element == 31;

        /// <summary>True when the element is one of the PDF compression settings controls.</summary>
        static bool IsCompressSettingsElement(int element) => element == 83 || element == 81 || element == 82;

        /// <summary>True when the element is one of the convert buttons, including the
        /// dynamically laid-out command cards that follow element 50.</summary>
        static bool IsConvertElement(int element)
            => element == 18 || element == 19 || element == 25 || (element >= 50 && element < 50 + ConvertCommands.Length);

        /// <summary>True when the element is one of the about-dialog buttons.</summary>
        static bool IsAboutElement(int element) => element == 23 || element == 24;

        /// <summary>Handles PDF-language and UI-language dropdown clicks, closing them on outside clicks.</summary>
        static bool HandleDropdownClick(IntPtr hwnd, int adjMouseX, int adjMouseY, float logW, float contentX)
        {
            if (_pdfLangDropdownOpen)
            {
                return HandlePdfLangDropdownClick(hwnd, adjMouseX, adjMouseY, contentX);
            }
            if (_langDropdownOpen)
            {
                return HandleLangDropdownClick(hwnd, adjMouseX, adjMouseY, logW);
            }
            return false;
        }

        /// <summary>Handles a click on the open PDF-language dropdown, saving the selection
        /// or closing the popup on an outside click.</summary>
        static bool HandlePdfLangDropdownClick(IntPtr hwnd, int adjMouseX, int adjMouseY, float contentX)
        {
            int popupHeight = PdfLangs.Length * 26 + 8;
            int popupY = _pdfLangDropdownY - popupHeight;
            if (adjMouseX >= contentX && adjMouseX <= contentX + 240 && adjMouseY >= popupY && adjMouseY < _pdfLangDropdownY)
            {
                if (adjMouseY >= popupY + 4 && adjMouseY < _pdfLangDropdownY - 4)
                {
                    int clickedIdx = (adjMouseY - (popupY + 4)) / 26;
                    if (clickedIdx >= 0 && clickedIdx < PdfLangs.Length)
                    {
                        ClickraStorage.SaveSetting("TranslateTargetLang", PdfLangs[clickedIdx].Code);
                    }
                }
                _pdfLangDropdownOpen = false;
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return true;
            }
            _pdfLangDropdownOpen = false;
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return true;
        }

        /// <summary>Handles a click on the open UI-language dropdown, selecting the hovered
        /// language or closing the popup on an outside click.</summary>
        static bool HandleLangDropdownClick(IntPtr hwnd, int adjMouseX, int adjMouseY, float logW)
        {
            int popupY = _langDropdownY - 180;
            if (adjMouseX >= GetContentX(logW) && adjMouseX <= GetContentX(logW) + 240)
            {
                if (adjMouseY >= popupY && adjMouseY < popupY + 38)
                {
                    return true;
                }
                if (adjMouseY >= popupY + 38 && adjMouseY < _langDropdownY)
                {
                    int clickedIdx = _langScrollOffset + (adjMouseY - (popupY + 38)) / 26;
                    var filtered = GetFilteredLanguages();
                    if (clickedIdx >= 0 && clickedIdx < filtered.Count)
                    {
                        SelectLanguage(filtered[clickedIdx].Code);
                    }
                    _langDropdownOpen = false;
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                    return true;
                }
            }

            _langDropdownOpen = false;
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return true;
        }

        /// <summary>Handles history tab row expansion and detail-field scrollbar clicks.</summary>
        static bool HandleHistoryClick(IntPtr hwnd, int mouseX, int adjMouseX, int adjMouseY, float logW, float contentX)
        {
            float virtLogW = Math.Max(760f, logW);
            if (adjMouseX < contentX || adjMouseX >= virtLogW - 40) return false;

            int currentY = 90 + GetActiveHistoryCount() * 52;
            FindClickedHistoryRow(adjMouseY, ref currentY, out int clickedIndex, out bool clickedDetails, out int detailFieldIndex);
            if (clickedIndex == -1) return false;

            if (clickedDetails)
            {
                if (detailFieldIndex != -1)
                {
                    TryStartHistoryDetailScroll(hwnd, mouseX, adjMouseX, logW, clickedIndex, detailFieldIndex);
                }
                return true;
            }

            ToggleHistoryRowExpand(hwnd, clickedIndex);
            return true;
        }

        /// <summary>Returns the number of in-flight tasks in the queue (one row each).</summary>
        static int GetActiveHistoryCount()
        {
            return ClickraStorage.GetActiveTasks().Count;
        }

        /// <summary>Finds the history row (and optional detail field) under the click point.</summary>
        static void FindClickedHistoryRow(int adjMouseY, ref int currentY, out int clickedIndex, out bool clickedDetails, out int detailFieldIndex)
        {
            clickedIndex = -1;
            clickedDetails = false;
            detailFieldIndex = -1;
            for (int i = 0; i < _historyEntries.Count; i++)
            {
                bool isExpanded = (i == _expandedHistoryIndex);
                int rowH = GetHistoryRowHeight(isExpanded);
                if (adjMouseY >= currentY && adjMouseY < currentY + rowH)
                {
                    if (isExpanded && adjMouseY >= currentY + 44)
                    {
                        clickedDetails = true;
                        clickedIndex = i;
                        detailFieldIndex = GetDetailFieldIndexFromY(adjMouseY - currentY);
                    }
                    else
                    {
                        clickedIndex = i;
                    }
                    break;
                }
                currentY += rowH + 8;
            }
        }

        /// <summary>Height of a history row: 44 collapsed, 160 when the detail pane is open.</summary>
        private static int GetHistoryRowHeight(bool isExpanded) => isExpanded ? 160 : 44;

        /// <summary>Maps a Y offset inside an expanded history row to its detail field index,
        /// or -1 when the offset is between fields.</summary>
        private static int GetDetailFieldIndexFromY(int relY)
        {
            if (relY >= 50 && relY < 76) return 0;
            if (relY >= 76 && relY < 102) return 1;
            if (relY >= 128 && relY < 156) return 2;
            return -1;
        }

        /// <summary>Expands or collapses the clicked history row.</summary>
        static void ToggleHistoryRowExpand(IntPtr hwnd, int clickedIndex)
        {
            _expandedHistoryIndex = _expandedHistoryIndex == clickedIndex ? -1 : clickedIndex;
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        /// <summary>Starts dragging a history detail-field scrollbar when the click lands on
        /// its thumb track.</summary>
        static void TryStartHistoryDetailScroll(IntPtr hwnd, int mouseX, int adjMouseX, float logW, int rowIndex, int fieldIndex)
        {
            string textToScroll = GetHistoryDetailText(_historyEntries[rowIndex], fieldIndex);
            if (string.IsNullOrEmpty(textToScroll)) return;

            float textW = 0f;
            using (var tempBmp = new Bitmap(1, 1))
            using (var tempG = Graphics.FromImage(tempBmp))
            {
                textW = tempG.MeasureString(textToScroll, _subFont!).Width / _dpiScale;
            }
            float maxValW = GetMaxValW(logW);
            float maxScroll = Math.Max(0f, textW - maxValW);
            if (maxScroll <= 0) return;

            float inputLabelW = 0f, outputLabelW = 0f, timeLabelW = 0f, errorLabelW = 0f;
            using (var tempBmp = new Bitmap(1, 1))
            using (var tempG = Graphics.FromImage(tempBmp))
            {
                inputLabelW = tempG.MeasureString(GetText("history_detail_inputs") + ":", _subFont!).Width / _dpiScale;
                outputLabelW = tempG.MeasureString(GetText("history_detail_outputs") + ":", _subFont!).Width / _dpiScale;
                timeLabelW = tempG.MeasureString(GetText("history_detail_time") + ":", _subFont!).Width / _dpiScale;
                errorLabelW = tempG.MeasureString(GetText("history_detail_error") + ":", _subFont!).Width / _dpiScale;
            }
            float maxLabelW = Math.Max(inputLabelW, Math.Max(outputLabelW, Math.Max(timeLabelW, errorLabelW)));
            float virtLogW = Math.Max(760f, logW);
            float valX = GetContentX(logW) + 12 + maxLabelW + 16;
            float rowWLocal = virtLogW - 40 - GetContentX(logW);
            if (adjMouseX < valX || adjMouseX > GetContentX(logW) + rowWLocal - 12) return;

            float clickX = adjMouseX - valX;
            float thumbW = Math.Max(15f, (maxValW / textW) * maxValW);
            float currentOffset = 0;
            DetailScrollOffsets.TryGetValue((rowIndex, fieldIndex), out currentOffset);

            float thumbX = (currentOffset / textW) * maxValW;
            if (thumbX + thumbW > maxValW) thumbX = maxValW - thumbW;

            float travelRange = maxValW - thumbW;
            float relativePos = travelRange > 0 ? (clickX - thumbW / 2f) / travelRange : 0f;
            float newOffset = Math.Max(0f, Math.Min(relativePos * maxScroll, maxScroll));

            DetailScrollOffsets[(rowIndex, fieldIndex)] = newOffset;

            _isDraggingDetailScroll = true;
            _draggingDetailRowIndex = rowIndex;
            _draggingDetailFieldIndex = fieldIndex;
            _dragDetailStartMouseX = mouseX;
            _dragDetailStartOffset = newOffset;
            SetCapture(hwnd);
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        /// <summary>Returns the text of a history detail field, localizing the user-abort marker.</summary>
        static string GetHistoryDetailText(ClickraStorage.HistoryEntry entry, int fieldIndex)
        {
            if (fieldIndex == 0)
            {
                return (entry.InputPaths ?? "").Replace(";", ", ");
            }
            if (fieldIndex == 1)
            {
                return entry.OutputPath ?? "";
            }
            if (entry.IsSuccess)
            {
                return entry.ElapsedMs >= 0 ? $"{entry.ElapsedMs / 1000.0:F2} s ({entry.ElapsedMs} ms)" : "N/A";
            }
            string errorMsg = !string.IsNullOrEmpty(entry.ErrorMessage) ? entry.ErrorMessage : "";
            if (errorMsg.Equals("User Aborted", StringComparison.OrdinalIgnoreCase))
            {
                errorMsg = GetText("error_user_aborted");
            }
            return errorMsg;
        }

        /// <summary>Handles vertical/horizontal scrollbar clicks: thumb drag start and track jump.</summary>
        static bool HandleScrollbarClick(IntPtr hwnd, int mouseX, int mouseY)
        {
            if (HandleVerticalScrollbarClick(hwnd, mouseX, mouseY)) return true;
            if (HandleHorizontalScrollbarClick(hwnd, mouseX, mouseY)) return true;
            return false;
        }

        /// <summary>Handles clicks on the vertical scrollbar: thumb drag start or track jump.</summary>
        static bool HandleVerticalScrollbarClick(IntPtr hwnd, int mouseX, int mouseY)
        {
            float logW = GetLogicalWidth(hwnd);
            float logH = GetLogicalHeight(hwnd);
            float contentH = GetContentHeight(hwnd);
            bool showV = logH < contentH;
            bool showH = logW < 760;
            if (!showV || mouseX < logW - 8 || mouseX >= logW) return false;

            float trackY = 4;
            float trackH = logH - 8;
            if (showH) trackH = logH - 16;
            float thumbH = Math.Max(20f, (logH / contentH) * trackH);
            float thumbY = trackY + (_contentScrollY / (contentH - logH)) * (trackH - thumbH);

            if (mouseY < trackY || mouseY >= trackY + trackH) return false;

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
            return true;
        }

        /// <summary>Handles clicks on the horizontal scrollbar: thumb drag start or track jump.</summary>
        static bool HandleHorizontalScrollbarClick(IntPtr hwnd, int mouseX, int mouseY)
        {
            float logW = GetLogicalWidth(hwnd);
            float logH = GetLogicalHeight(hwnd);
            float contentH = GetContentHeight(hwnd);
            float sidebarW = GetSidebarWidth(logW);
            bool showV = logH < contentH;
            bool showH = logW < 760;
            if (!showH || mouseY < logH - 8 || mouseY >= logH || mouseX < sidebarW) return false;

            float trackX = sidebarW + 4;
            float trackW = (logW - sidebarW) - 8;
            if (showV) trackW = (logW - sidebarW) - 16;
            if (trackW <= 0) return false;

            float thumbW = Math.Max(20f, ((logW - sidebarW) / (760f - sidebarW)) * trackW);
            float thumbX = trackX + (_contentScrollX / (760f - logW)) * (trackW - thumbW);

            if (mouseX < trackX || mouseX >= trackX + trackW) return false;

            if (mouseX >= thumbX && mouseX < thumbX + thumbW)
            {
                _isDraggingScrollX = true;
                _dragStartMouseX = mouseX;
                _dragStartScrollX = _contentScrollX;
                SetCapture(hwnd);
            }
            else
            {
                float trackRange = trackW - thumbW;
                float relativePos = trackRange > 0 ? (mouseX - trackX - thumbW / 2f) / trackRange : 0f;
                _contentScrollX = Math.Max(0, Math.Min(relativePos * (760f - logW), 760f - logW));
                _isDraggingScrollX = true;
                _dragStartMouseX = mouseX;
                _dragStartScrollX = _contentScrollX;
                SetCapture(hwnd);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return true;
        }

        /// <summary>Switches the active tab and resets scroll state.</summary>
        static void HandleTabBarClick(IntPtr hwnd, int element)
        {
            _activeTab = element;
            if (_activeTab == 0 || _activeTab == 2)
            {
                RefreshHistoryData();
            }

            _langScrollOffset = 0;
            _contentScrollX = 0;
            _contentScrollY = 0;
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        /// <summary>Handles the history toolbar clear button.</summary>
        static void HandleHistoryToolbarClick(IntPtr hwnd)
        {
            if (MessageBox(hwnd, GetText("history_clear_confirm"), "Clickra", 0x24) == 6)
            {
                ClickraStorage.ClearHistory();
                _expandedHistoryIndex = -1;
                RefreshHistoryData();
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
        }

        /// <summary>Handles settings-tab element clicks: toggles, output dirs and office engine selection.</summary>
        static void HandleSettingsClick(IntPtr hwnd, int element)
        {
            if (element == 5)
            {
                bool current = ClickraStorage.GetSetting("QuietMode").Equals("true", StringComparison.OrdinalIgnoreCase);
                ClickraStorage.SaveSetting("QuietMode", current ? "false" : "true");
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 6)
            {
                bool current = ClickraStorage.GetSetting("Notification").Equals("true", StringComparison.OrdinalIgnoreCase);
                ClickraStorage.SaveSetting("Notification", current ? "false" : "true");
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 7)
            {
                ClickraStorage.SaveSetting("OutputDir", "source");
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 8)
            {
                ClickraStorage.SaveSetting("OutputDir", "desktop");
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 9)
            {
                ClickraStorage.SaveSetting("OutputDir", "downloads");
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 20)
            {
                string title = GetText("setting_output_browse_title");
                string folder = BrowseForFolder(hwnd, title);
                if (!string.IsNullOrEmpty(folder))
                {
                    ClickraStorage.SaveSetting("OutputDir", folder);
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
            }
            else if (element == 32)
            {
                ClickraStorage.SaveSetting("OfficeEngine", "auto");
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 33)
            {
                ClickraStorage.SaveSetting("OfficeEngine", "microsoft");
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 34)
            {
                ClickraStorage.SaveSetting("OfficeEngine", "libreoffice");
                ClickraStorage.SaveSetting("LibreOfficePath", "");
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
        }

        /// <summary>Handles LibreOffice setup clicks: browse, install/download and uninstall.</summary>
        static void HandleLibreOfficeClick(IntPtr hwnd, int element)
        {
            if (element == 35)
            {
                HandleLibreOfficeBrowse(hwnd);
            }
            else if (element == 36)
            {
                HandleLibreOfficeDownload(hwnd);
            }
            else if (element == 38)
            {
                HandleLibreOfficeUninstall(hwnd);
            }
        }

        /// <summary>Lets the user browse for a soffice.exe and validates the selection.</summary>
        static void HandleLibreOfficeBrowse(IntPtr hwnd)
        {
            const string sofficeFilter = "LibreOffice soffice.exe\0soffice.exe\0Executable Files (*.exe)\0*.exe\0All Files (*.*)\0*.*\0\0";
            var chosen = OpenFiles(hwnd, sofficeFilter, GetText("setting_libreoffice_browse_title"));
            if (chosen.Count == 0) return;

            string candidate = chosen[0];
            if (Path.GetFileName(candidate).Equals("soffice.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (LibreOfficeHelper.LooksLikeLibreOfficeExecutable(candidate))
                {
                    ClickraStorage.SaveSetting("LibreOfficePath", candidate);
                    ClickraStorage.SaveSetting("LibreOfficeRemovalPendingRestart", "false");
                    MessageBox(hwnd, string.Format(GetText("setting_libreoffice_validated"), Path.GetDirectoryName(candidate)), "Clickra", 0x40);
                }
                else
                {
                    MessageBox(hwnd, GetText("setting_libreoffice_validation_failed"), "Clickra", 0x30);
                }
            }
            else
            {
                MessageBox(hwnd, GetText("setting_libreoffice_invalid"), "Clickra", 0x30);
            }
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        /// <summary>Starts the LibreOffice download/install flow after the confirmation prompt.</summary>
        static void HandleLibreOfficeDownload(IntPtr hwnd)
        {
            lock (_libreOfficeDownloadLock)
            {
                if (_libreOfficeDownloadInProgress)
                {
                    MessageBox(hwnd, GetText("setting_libreoffice_download_in_progress"), "Clickra", 0x40);
                    return;
                }
            }

            bool removalPendingRestart = ClickraStorage.GetSetting("LibreOfficeRemovalPendingRestart").Equals("true", StringComparison.OrdinalIgnoreCase);
            var package = LibreOfficeEngineInstaller.RecommendedPackage;
            string installedVersion = LibreOfficeEngineInstaller.GetInstalledSystemVersion();
            if (!removalPendingRestart &&
                !string.IsNullOrWhiteSpace(installedVersion) &&
                LibreOfficeEngineInstaller.IsRecommendedVersionInstalled())
            {
                string resolvedPath = LibreOfficeEngineInstaller.ResolveSystemSofficePath();
                if (!string.IsNullOrWhiteSpace(resolvedPath))
                {
                    ClickraStorage.SaveSetting("LibreOfficePath", resolvedPath);
                }

                MessageBox(
                    hwnd,
                    string.Format(GetText("setting_libreoffice_already_current"), installedVersion),
                    "Clickra",
                    0x40);
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return;
            }

            string prompt = string.Format(
                GetText("setting_libreoffice_download_prompt"),
                package.Version,
                package.Edition,
                FormatBytes(package.DownloadBytes),
                LibreOfficeEngineInstaller.GetDefaultInstallRoot(),
                package.Sha256);

            if (MessageBox(hwnd, prompt, "Clickra", 0x41) != 1) return;

            lock (_libreOfficeDownloadLock)
            {
                _libreOfficeDownloadInProgress = true;
                _libreOfficeDownloadProgress = 0;
                _libreOfficeDownloadStatus = removalPendingRestart
                    ? GetText("setting_libreoffice_reinstall_starting")
                    : GetText("setting_libreoffice_download_starting");
            }
            InvalidateRect(hwnd, IntPtr.Zero, false);

            var thread = new System.Threading.Thread(() => DownloadLibreOfficeInBackground(hwnd));
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        /// <summary>Downloads, verifies and installs LibreOffice on a background STA thread,
        /// reporting progress and result through dashboard actions.</summary>
        static void DownloadLibreOfficeInBackground(IntPtr hwnd)
        {
            var package = LibreOfficeEngineInstaller.RecommendedPackage;
            try
            {
                string downloadDir = Path.Combine(ClickraStorage.GetDataDir(), "downloads");
                var progress = new Progress<int>(percent => ReportDownloadProgress(hwnd, percent));
                string installerPath = LibreOfficeEngineInstaller.DownloadAndVerifyAsync(
                        package,
                        downloadDir,
                        progress,
                        System.Threading.CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                PostDashboardAction(hwnd, () => SetLibreOfficeSetupStatus(85, GetText("setting_libreoffice_installing")));

                LibreOfficeInstallResult installResult = LibreOfficeEngineInstaller.InstallMsiPackageAsync(
                        installerPath,
                        System.Threading.CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                string sofficePath = installResult.SofficePath;
                if (!installResult.RestartRequired && !LibreOfficeHelper.LooksLikeLibreOfficeExecutable(sofficePath))
                    throw new InvalidOperationException(GetText("setting_libreoffice_validation_failed"));

                PostDashboardAction(hwnd, () => SetLibreOfficeSetupStatus(95, GetText("setting_libreoffice_installing")));

                if (!string.IsNullOrWhiteSpace(sofficePath))
                    ClickraStorage.SaveSetting("LibreOfficePath", sofficePath);
                ClickraStorage.SaveSetting("LibreOfficeInstalledByClickra", "true");
                ClickraStorage.SaveSetting("LibreOfficeRemovalPendingRestart", "false");

                PostDashboardAction(hwnd, () => ShowInstallResultMessage(hwnd, installResult.RestartRequired, sofficePath));
            }
            catch (Exception ex)
            {
                ClickraStorage.SaveSetting("LibreOfficePath", "");
                PostDashboardAction(hwnd, () => ShowDownloadFailureMessage(hwnd, ex.Message));
            }
            finally
            {
                PostDashboardAction(hwnd, FinishLibreOfficeSetupStatus);
            }
        }

        /// <summary>Posts the LibreOffice download progress percentage to the dashboard.</summary>
        private static void ReportDownloadProgress(IntPtr hwnd, int percent)
        {
            int displayPercent = Math.Min(80, Math.Max(1, percent * 80 / 100));
            PostDashboardAction(hwnd, () => SetLibreOfficeSetupStatus(
                displayPercent,
                percent >= 100
                    ? GetText("setting_libreoffice_verifying")
                    : string.Format(GetText("setting_libreoffice_download_progress"), percent)));
        }

        /// <summary>Shows the LibreOffice install result (restart-required or ready) on the dashboard.</summary>
        private static void ShowInstallResultMessage(IntPtr hwnd, bool restartRequired, string sofficePath)
        {
            MessageBox(
                hwnd,
                string.Format(
                    GetText(restartRequired
                        ? "setting_libreoffice_install_restart_required"
                        : "setting_libreoffice_download_ready"),
                    string.IsNullOrWhiteSpace(sofficePath) ? LibreOfficeEngineInstaller.GetDefaultInstallRoot() : sofficePath),
                "Clickra",
                0x40);
        }

        /// <summary>Shows the LibreOffice download/install failure message on the dashboard.</summary>
        private static void ShowDownloadFailureMessage(IntPtr hwnd, string errorMessage)
        {
            MessageBox(hwnd, string.Format(GetText("setting_libreoffice_download_failed"), errorMessage), "Clickra", 0x10);
        }

        /// <summary>Starts the LibreOffice uninstall flow after the confirmation prompt.</summary>
        static void HandleLibreOfficeUninstall(IntPtr hwnd)
        {
            lock (_libreOfficeDownloadLock)
            {
                if (_libreOfficeDownloadInProgress)
                {
                    MessageBox(hwnd, GetText("setting_libreoffice_download_in_progress"), "Clickra", 0x40);
                    return;
                }
            }

            if (ClickraStorage.GetSetting("LibreOfficeRemovalPendingRestart").Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox(hwnd, GetText("setting_libreoffice_removal_pending"), "Clickra", 0x40);
                return;
            }

            if (MessageBox(hwnd, GetText("setting_libreoffice_uninstall_confirm"), "Clickra", 0x31) != 1) return;

            lock (_libreOfficeDownloadLock)
            {
                _libreOfficeDownloadInProgress = true;
                _libreOfficeDownloadProgress = 60;
                _libreOfficeDownloadStatus = GetText("setting_libreoffice_uninstalling");
            }
            InvalidateRect(hwnd, IntPtr.Zero, false);

            var thread = new System.Threading.Thread(() => UninstallLibreOfficeInBackground(hwnd));
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        /// <summary>Uninstalls LibreOffice on a background STA thread, reporting the result
        /// through a dashboard action.</summary>
        static void UninstallLibreOfficeInBackground(IntPtr hwnd)
        {
            try
            {
                LibreOfficeUninstallResult uninstallResult = LibreOfficeEngineInstaller.UninstallSystemLibreOfficeAsync(
                        System.Threading.CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                ClickraStorage.SaveSetting("LibreOfficePath", "");
                ClickraStorage.SaveSetting("LibreOfficeInstalledByClickra", "false");
                ClickraStorage.SaveSetting("LibreOfficeRemovalPendingRestart", uninstallResult.RestartRequired ? "true" : "false");
                ClickraStorage.SaveSetting("OfficeEngine", "auto");

                PostDashboardAction(hwnd, () => MessageBox(
                    hwnd,
                    GetText(uninstallResult.RestartRequired
                        ? "setting_libreoffice_uninstall_restart_required"
                        : "setting_libreoffice_uninstall_ready"),
                    "Clickra",
                    0x40));
            }
            catch (Exception ex)
            {
                PostDashboardAction(hwnd, () => MessageBox(
                    hwnd,
                    string.Format(GetText("setting_libreoffice_uninstall_failed"), ex.Message),
                    "Clickra",
                    0x10));
            }
            finally
            {
                PostDashboardAction(hwnd, FinishLibreOfficeSetupStatus);
            }
        }

        /// <summary>Toggles the UI-language and PDF-language dropdowns.</summary>
        static void HandleDropdownToggleClick(IntPtr hwnd, int element)
        {
            if (element == 10)
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
            else if (element == 31)
            {
                _pdfLangDropdownOpen = !_pdfLangDropdownOpen;
                _langDropdownOpen = false;
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
        }

        /// <summary>Handles PDF compression settings: slider drag start and option toggles.</summary>
        static void HandleCompressSettingsClick(IntPtr hwnd, int element, int adjMouseX)
        {
            if (element == 83)
            {
                // PDF compress slider clicked — snap to nearest of 4 stops via equal-width segments + enable drag
                float relX = adjMouseX - _pdfSliderTrackX;
                float fraction = Math.Max(0f, Math.Min(1f, relX / _pdfSliderTrackW));
                int newLevel = (int)Math.Max(0, Math.Min(3, Math.Round(fraction * 3, MidpointRounding.AwayFromZero)));
                ApplyPdfCompressLevel(hwnd, newLevel);
                _isDraggingPdfSlider = true;
                SetCapture(hwnd);
            }
            else if (element == 81)
            {
                bool current = ClickraStorage.GetSetting("PdfCompressStripFonts").Equals("true", StringComparison.OrdinalIgnoreCase);
                ClickraStorage.SaveSetting("PdfCompressStripFonts", current ? "false" : "true");
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 82)
            {
                bool current = !ClickraStorage.GetSetting("PdfCompressMinifyContent").Equals("false", StringComparison.OrdinalIgnoreCase);
                ClickraStorage.SaveSetting("PdfCompressMinifyContent", current ? "false" : "true");
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
        }

        /// <summary>Handles convert-tab clicks: command selection, file picking, run and clear.</summary>
        static void HandleConvertClick(IntPtr hwnd, int element)
        {
            if (element >= 50 && element < 50 + ConvertCommands.Length)
            {
                ConvertCommand.Select(ConvertCommands[element - 50]);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 18)
            {
                HandlePickFiles(hwnd);
            }
            else if (element == 19)
            {
                RunConversion(hwnd);
            }
            else if (element == 25)
            {
                _selectedFiles.Clear();
                _convertCommandIndex = -1;
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
        }

        /// <summary>Shows the file-open dialog and keeps the user's current command when it
        /// accepts the chosen files, auto-selecting only when it can't.</summary>
        static void HandlePickFiles(IntPtr hwnd)
        {
            string title = GetText("convert_drag_drop_hint");
            const string allFilter = "Supported Files (*.doc;*.docx;*.ppt;*.pptx;*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp)\0*.doc;*.docx;*.ppt;*.pptx;*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp\0All Files (*.*)\0*.*\0\0";
            var chosen = OpenFiles(hwnd, allFilter, title);
            if (chosen.Count == 0) return;

            _selectedFiles = chosen;
            if (CurrentSelectionAcceptsFiles(_selectedFiles))
            {
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return;
            }

            // Only auto-select when the user's current command can't accept the files
            // (e.g. 分割 PDF stays selected after picking a PDF).
            _convertCommandIndex = -1;
            for (int i = 0; i < ConvertCommands.Length; i++)
            {
                if (ConvertCommands[i].ValidateFiles(_selectedFiles, out _))
                {
                    _convertCommandIndex = i;
                    break;
                }
            }
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        /// <summary>Handles about/help clicks: GitHub link and diagnostics feedback.</summary>
        static void HandleAboutClick(IntPtr hwnd, int element)
        {
            if (element == 23)
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
            else if (element == 24)
            {
                try
                {
                    string dataDir = ClickraStorage.GetDataDir();
                    string logPath = Path.Combine(dataDir, "history.log");

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

                    var ver = typeof(DashboardWindow).Assembly.GetName().Version;
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

        /// <summary>Formats a byte count as a human-readable size string.</summary>
        static string FormatBytes(long bytes)
        {
            const double mb = 1024d * 1024d;
            return $"{bytes / mb:F0} MB";
        }

        /// <summary>Applies a PDF compression level selection and refreshes the settings tab.</summary>
        static void ApplyPdfCompressLevel(IntPtr hwnd, int level)
        {
            var (dpi, quality) = level switch
            {
                0 => ("120", "65"),
                1 => ("120", "75"),
                2 => ("150", "80"),
                _ => ("300", "85")
            };
            ClickraStorage.SaveSetting("PdfCompressImageLevel", level.ToString());
            ClickraStorage.SaveSetting("PdfCompressTargetDpi", dpi);
            ClickraStorage.SaveSetting("PdfCompressJpegQuality", quality);
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        /// <summary>Posts an action to run on the dashboard's UI thread.</summary>
        static void PostDashboardAction(IntPtr hwnd, Action action)
        {
            _uiActions.Enqueue(action);
            PostMessageW(hwnd, WM_USER_DASHBOARD_ACTION, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Updates the LibreOffice setup progress/status from a background thread.</summary>
        static void SetLibreOfficeSetupStatus(int progress, string status)
        {
            lock (_libreOfficeDownloadLock)
            {
                _libreOfficeDownloadProgress = progress;
                _libreOfficeDownloadStatus = status;
            }
        }

        static void FinishLibreOfficeSetupStatus()
        {
            lock (_libreOfficeDownloadLock)
            {
                _libreOfficeDownloadInProgress = false;
                _libreOfficeDownloadProgress = 0;
                _libreOfficeDownloadStatus = "";
            }
        }
    }
}

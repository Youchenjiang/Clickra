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
        static void HandleLButtonDown(IntPtr hwnd, IntPtr w, IntPtr l)
        {
            int rawX = (short)(l.ToInt64() & 0xFFFF);
            int rawY = (short)((l.ToInt64() >> 16) & 0xFFFF);
            int mouseX = (int)(rawX / _dpiScale);
            int mouseY = (int)(rawY / _dpiScale);

            float logW = GetLogicalWidth(hwnd);
            float logH = GetLogicalHeight(hwnd);
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
                    return;
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
                        return;
                    }
                }
            }

            int adjMouseX = mouseX >= sidebarW ? (int)(mouseX + _contentScrollX) : mouseX;
            int adjMouseY = mouseX >= sidebarW ? (int)(mouseY + _contentScrollY) : mouseY;

            if (_pdfLangDropdownOpen)
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
                    return;
                }
                _pdfLangDropdownOpen = false;
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return;
            }

            if (_langDropdownOpen)
            {
                int popupY = _langDropdownY - 180;
                if (adjMouseX >= GetContentX(logW) && adjMouseX <= GetContentX(logW) + 240)
                {
                    if (adjMouseY >= popupY && adjMouseY < popupY + 38)
                    {
                        return;
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
                        return;
                    }
                }

                _langDropdownOpen = false;
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return;
            }

            if (_activeTab == 2)
            {
                float virtLogW = Math.Max(760f, logW);
                if (adjMouseX >= GetContentX(logW) && adjMouseX < virtLogW - 40)
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
                    int clickedIndex = -1;
                    bool clickedDetails = false;
                    int detailFieldIndex = -1;
                    for (int i = 0; i < _historyEntries.Count; i++)
                    {
                        bool isExpanded = (i == _expandedHistoryIndex);
                        int rowH = isExpanded ? 160 : 44;
                        if (adjMouseY >= currentY && adjMouseY < currentY + rowH)
                        {
                            if (isExpanded && adjMouseY >= currentY + 44)
                            {
                                clickedDetails = true;
                                clickedIndex = i;
                                int relY = adjMouseY - currentY;
                                if (relY >= 50 && relY < 76) detailFieldIndex = 0;
                                else if (relY >= 76 && relY < 102) detailFieldIndex = 1;
                                else if (relY >= 128 && relY < 156) detailFieldIndex = 2;
                            }
                            else
                            {
                                clickedIndex = i;
                            }
                            break;
                        }
                        currentY += rowH + 8;
                    }

                    if (clickedIndex != -1)
                    {
                        if (clickedDetails)
                        {
                            if (detailFieldIndex != -1)
                            {
                                string textToScroll = "";
                                if (detailFieldIndex == 0)
                                {
                                    textToScroll = (_historyEntries[clickedIndex].InputPaths ?? "").Replace(";", ", ");
                                }
                                else if (detailFieldIndex == 1)
                                {
                                    textToScroll = _historyEntries[clickedIndex].OutputPath;
                                }
                                else if (detailFieldIndex == 2)
                                {
                                    textToScroll = !string.IsNullOrEmpty(_historyEntries[clickedIndex].ErrorMessage) ? _historyEntries[clickedIndex].ErrorMessage : "";
                                    if (textToScroll.Equals("User Aborted", StringComparison.OrdinalIgnoreCase))
                                    {
                                        textToScroll = GetText("error_user_aborted");
                                    }
                                }

                                if (!string.IsNullOrEmpty(textToScroll))
                                {
                                    float textW;
                                    using (var tempBmp = new Bitmap(1, 1))
                                    using (var tempG = Graphics.FromImage(tempBmp))
                                    {
                                        textW = tempG.MeasureString(textToScroll, _subFont!).Width / _dpiScale;
                                    }
                                    float maxValW = GetMaxValW(logW);
                                    float maxScroll = Math.Max(0f, textW - maxValW);

                                    if (maxScroll > 0)
                                    {
                                        float inputLabelW, outputLabelW, timeLabelW, errorLabelW;
                                        using (var tempBmp = new Bitmap(1, 1))
                                        using (var tempG = Graphics.FromImage(tempBmp))
                                        {
                                            inputLabelW = tempG.MeasureString(GetText("history_detail_inputs") + ":", _subFont!).Width / _dpiScale;
                                            outputLabelW = tempG.MeasureString(GetText("history_detail_outputs") + ":", _subFont!).Width / _dpiScale;
                                            timeLabelW = tempG.MeasureString(GetText("history_detail_time") + ":", _subFont!).Width / _dpiScale;
                                            errorLabelW = tempG.MeasureString(GetText("history_detail_error") + ":", _subFont!).Width / _dpiScale;
                                        }
                                        float maxLabelW = Math.Max(inputLabelW, Math.Max(outputLabelW, Math.Max(timeLabelW, errorLabelW)));
                                        float valX = contentX + 12 + maxLabelW + 16;
                                        float rowWLocal = virtLogW - 40 - contentX;

                                        if (adjMouseX >= valX && adjMouseX <= contentX + rowWLocal - 12)
                                        {
                                            float clickX = adjMouseX - valX;
                                            float thumbW = Math.Max(15f, (maxValW / textW) * maxValW);
                                            float currentOffset = 0;
                                            DetailScrollOffsets.TryGetValue((clickedIndex, detailFieldIndex), out currentOffset);

                                            float thumbX = (currentOffset / textW) * maxValW;
                                            if (thumbX + thumbW > maxValW) thumbX = maxValW - thumbW;

                                            float travelRange = maxValW - thumbW;
                                            float relativePos = travelRange > 0 ? (clickX - thumbW / 2f) / travelRange : 0f;
                                            float newOffset = Math.Max(0f, Math.Min(relativePos * maxScroll, maxScroll));

                                            DetailScrollOffsets[(clickedIndex, detailFieldIndex)] = newOffset;

                                            _isDraggingDetailScroll = true;
                                            _draggingDetailRowIndex = clickedIndex;
                                            _draggingDetailFieldIndex = detailFieldIndex;
                                            _dragDetailStartMouseX = mouseX;
                                            _dragDetailStartOffset = newOffset;
                                            SetCapture(hwnd);
                                            InvalidateRect(hwnd, IntPtr.Zero, false);
                                        }
                                    }
                                }
                            }
                            return;
                        }
                        else
                        {
                            if (_expandedHistoryIndex == clickedIndex)
                            {
                                _expandedHistoryIndex = -1;
                            }
                            else
                            {
                                _expandedHistoryIndex = clickedIndex;
                            }
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                            return;
                        }
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

                _langScrollOffset = 0;
                _contentScrollX = 0;
                _contentScrollY = 0;
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 22)
            {
                if (MessageBox(hwnd, GetText("history_clear_confirm"), "Clickra", 0x24) == 6)
                {
                    ClickraStorage.ClearHistory();
                    _expandedHistoryIndex = -1;
                    RefreshHistoryData();
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
            }
            else if (element == 5)
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
            else if (element == 35)
            {
                const string sofficeFilter = "LibreOffice soffice.exe\0soffice.exe\0Executable Files (*.exe)\0*.exe\0All Files (*.*)\0*.*\0\0";
                var chosen = OpenFiles(hwnd, sofficeFilter, GetText("setting_libreoffice_browse_title"));
                if (chosen.Count > 0)
                {
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
            }
            else if (element == 36)
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

                if (MessageBox(hwnd, prompt, "Clickra", 0x41) == 1)
                {
                    lock (_libreOfficeDownloadLock)
                    {
                        _libreOfficeDownloadInProgress = true;
                        _libreOfficeDownloadProgress = 0;
                        _libreOfficeDownloadStatus = removalPendingRestart
                            ? GetText("setting_libreoffice_reinstall_starting")
                            : GetText("setting_libreoffice_download_starting");
                    }
                    InvalidateRect(hwnd, IntPtr.Zero, false);

                    var thread = new System.Threading.Thread(() =>
                    {
                        try
                        {
                            string downloadDir = Path.Combine(ClickraStorage.GetDataDir(), "downloads");
                            var progress = new Progress<int>(percent =>
                            {
                                int displayPercent = Math.Min(80, Math.Max(1, percent * 80 / 100));
                                PostDashboardAction(hwnd, () =>
                                {
                                    SetLibreOfficeSetupStatus(
                                        displayPercent,
                                        percent >= 100
                                            ? GetText("setting_libreoffice_verifying")
                                            : string.Format(GetText("setting_libreoffice_download_progress"), percent));
                                });
                            });
                            string installerPath = LibreOfficeEngineInstaller.DownloadAndVerifyAsync(
                                    package,
                                    downloadDir,
                                    progress,
                                    System.Threading.CancellationToken.None)
                                .GetAwaiter()
                                .GetResult();

                            PostDashboardAction(hwnd, () =>
                            {
                                SetLibreOfficeSetupStatus(85, GetText("setting_libreoffice_installing"));
                            });

                            LibreOfficeInstallResult installResult = LibreOfficeEngineInstaller.InstallMsiPackageAsync(
                                    installerPath,
                                    System.Threading.CancellationToken.None)
                                .GetAwaiter()
                                .GetResult();

                            string sofficePath = installResult.SofficePath;
                            if (!installResult.RestartRequired && !LibreOfficeHelper.LooksLikeLibreOfficeExecutable(sofficePath))
                                throw new Exception(GetText("setting_libreoffice_validation_failed"));

                            PostDashboardAction(hwnd, () =>
                            {
                                SetLibreOfficeSetupStatus(95, GetText("setting_libreoffice_installing"));
                            });

                            if (!string.IsNullOrWhiteSpace(sofficePath))
                                ClickraStorage.SaveSetting("LibreOfficePath", sofficePath);
                            ClickraStorage.SaveSetting("LibreOfficeInstalledByClickra", "true");
                            ClickraStorage.SaveSetting("LibreOfficeRemovalPendingRestart", "false");

                            PostDashboardAction(hwnd, () =>
                            {
                                MessageBox(
                                    hwnd,
                                    string.Format(
                                        GetText(installResult.RestartRequired
                                            ? "setting_libreoffice_install_restart_required"
                                            : "setting_libreoffice_download_ready"),
                                        string.IsNullOrWhiteSpace(sofficePath) ? LibreOfficeEngineInstaller.GetDefaultInstallRoot() : sofficePath),
                                    "Clickra",
                                    0x40);
                            });
                        }
                        catch (Exception ex)
                        {
                            ClickraStorage.SaveSetting("LibreOfficePath", "");
                            PostDashboardAction(hwnd, () =>
                            {
                                MessageBox(
                                    hwnd,
                                    string.Format(GetText("setting_libreoffice_download_failed"), ex.Message),
                                    "Clickra",
                                    0x10);
                            });
                        }
                        finally
                        {
                            PostDashboardAction(hwnd, () =>
                            {
                                FinishLibreOfficeSetupStatus();
                            });
                        }
                    });
                    thread.SetApartmentState(System.Threading.ApartmentState.STA);
                    thread.IsBackground = true;
                    thread.Start();
                }
            }
            else if (element == 38)
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

                if (MessageBox(hwnd, GetText("setting_libreoffice_uninstall_confirm"), "Clickra", 0x31) == 1)
                {
                    lock (_libreOfficeDownloadLock)
                    {
                        _libreOfficeDownloadInProgress = true;
                        _libreOfficeDownloadProgress = 60;
                        _libreOfficeDownloadStatus = GetText("setting_libreoffice_uninstalling");
                    }
                    InvalidateRect(hwnd, IntPtr.Zero, false);

                    var thread = new System.Threading.Thread(() =>
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

                            PostDashboardAction(hwnd, () =>
                            {
                                MessageBox(
                                    hwnd,
                                    GetText(uninstallResult.RestartRequired
                                        ? "setting_libreoffice_uninstall_restart_required"
                                        : "setting_libreoffice_uninstall_ready"),
                                    "Clickra",
                                    0x40);
                            });
                        }
                        catch (Exception ex)
                        {
                            PostDashboardAction(hwnd, () =>
                            {
                                MessageBox(
                                    hwnd,
                                    string.Format(GetText("setting_libreoffice_uninstall_failed"), ex.Message),
                                    "Clickra",
                                    0x10);
                            });
                        }
                        finally
                        {
                            PostDashboardAction(hwnd, () =>
                            {
                                FinishLibreOfficeSetupStatus();
                            });
                        }
                    });
                    thread.SetApartmentState(System.Threading.ApartmentState.STA);
                    thread.IsBackground = true;
                    thread.Start();
                }
            }
            else if (element == 10)
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
            else if (element == 83)
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
            else if (element >= 50 && element <= 57)
            {
                ChangeConvertCommand(element - 50);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 18)
            {
                string title = GetText("convert_drag_drop_hint");
                const string allFilter = "Supported Files (*.doc;*.docx;*.ppt;*.pptx;*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp)\0*.doc;*.docx;*.ppt;*.pptx;*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp\0All Files (*.*)\0*.*\0\0";
                var chosen = OpenFiles(hwnd, allFilter, title);
                if (chosen.Count > 0)
                {
                    _selectedFiles = chosen;
                    _convertCommandIndex = -1;
                    for (int i = 0; i < ConvertCommands.Length; i++)
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
            else if (element == 23)
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

        static string FormatBytes(long bytes)
        {
            const double mb = 1024d * 1024d;
            return $"{bytes / mb:F0} MB";
        }

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

        static void PostDashboardAction(IntPtr hwnd, Action action)
        {
            _uiActions.Enqueue(action);
            PostMessageW(hwnd, WM_USER_DASHBOARD_ACTION, IntPtr.Zero, IntPtr.Zero);
        }

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

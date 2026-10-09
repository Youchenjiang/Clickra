using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Clickra.Core;
using Clickra.Core.Layout;
using Clickra.Core.Processors;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        private const string AppTitle = "Clickra";

        /// <summary>Routes left-button clicks to the active dashboard tab's hit regions.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S3776", Justification = "Dashboard hit-testing is intentionally ordered so overlapping UI regions resolve deterministically.")]
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

            float logH = GetLogicalHeight(hwnd);
            if (IsInsideConvertStickyFooter(mouseX, mouseY, logW, logH, contentX))
            {
                int fixedElement = HitTestConvertStickyAction(mouseX, mouseY, logW, logH, contentX);
                if (fixedElement == 19)
                    HandleConvertClick(hwnd, fixedElement);
                return;
            }

            int adjMouseX = mouseX >= sidebarW ? (int)(mouseX + _contentScrollX) : mouseX;
            int adjMouseY = mouseX >= sidebarW ? (int)(mouseY + _contentScrollY) : mouseY;

            if (HandleDropdownClick(hwnd, adjMouseX, adjMouseY)) return;

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
            else if (IsParkedActionElement(element))
            {
                HandleParkedActionClick(hwnd, element);
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
            => (element >= 1000 && element < 2000) ||
               element == 5 || element == 6 || element == 7 || element == 8 || element == 9 ||
               element == 20 || element == 32 || element == 33 || element == 34 ||
               (element >= 90 && element <= 96);

        /// <summary>True when the element is one of the LibreOffice setup buttons.</summary>
        static bool IsLibreOfficeElement(int element) => element == 35 || element == 36 || element == 38 || element == 39;

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
        static bool HandleDropdownClick(IntPtr hwnd, int adjMouseX, int adjMouseY)
        {
            if (_pdfLangDropdownOpen)
            {
                return HandlePdfLangDropdownClick(hwnd, adjMouseX, adjMouseY);
            }
            if (_langDropdownOpen)
            {
                return HandleLangDropdownClick(hwnd, adjMouseX, adjMouseY);
            }
            return false;
        }

        /// <summary>Handles a click on the open PDF-language dropdown, saving the selection
        /// or closing the popup on an outside click.</summary>
        static bool HandlePdfLangDropdownClick(IntPtr hwnd, int adjMouseX, int adjMouseY)
        {
            int popupHeight = DashboardLayout.PdfPopupHeight(PdfLangs.Length);
            LayoutRect popup = DashboardLayout.DropdownPopupRect(_pdfLangDropdownX, _pdfLangDropdownY, popupHeight, _pdfLangDropdownWidth);
            if (popup.Contains(adjMouseX, adjMouseY))
            {
                int clickedIdx = DashboardLayout.DropdownItemAt(popup.Y, DashboardLayout.PdfPopupListTop, popup.Height, adjMouseY);
                if (clickedIdx >= 0 && clickedIdx < PdfLangs.Length)
                {
                    ClickraStorage.SaveSetting(ClickraSettings.TranslateTargetLang, PdfLangs[clickedIdx].Code);
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
        static bool HandleLangDropdownClick(IntPtr hwnd, int adjMouseX, int adjMouseY)
        {
            var langPopup = DashboardLayout.DropdownPopupRect(_langDropdownX, _langDropdownY, DashboardLayout.LanguagePopupHeight, _langDropdownWidth);
            if (langPopup.Contains(adjMouseX, adjMouseY))
            {
                // 搜尋框佔住清單上方的區域：點在那裡不選任何語言，也不關閉清單。
                if (adjMouseY < langPopup.Y + DashboardLayout.LanguagePopupListTop)
                {
                    return true;
                }

                // 列號與繪製走同一個對應；不在任何列上（-1，例如清單下方的留白）就只關閉清單。
                int itemIndex = DashboardLayout.DropdownItemAt(
                    langPopup.Y, DashboardLayout.LanguagePopupListTop, langPopup.Height, adjMouseY);
                if (itemIndex >= 0)
                {
                    int clickedIdx = _langScrollOffset + itemIndex;
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
            bool showH = _activeTab != 3 && logW < 760;
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
            bool showH = _activeTab != 3 && logW < 760;
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

        private static void OpenStorePage(IntPtr hwnd)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Clickra.Core.FluentRuntimeHelper.StoreUri, // NOSONAR
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox(hwnd, $"Cannot open Store: {ex.Message}", AppTitle, 0x10);
            }
        }

        /// <summary>Handles LibreOffice setup clicks: browse, install/download and uninstall.</summary>
        /// <summary>Lets the user browse for a soffice.exe and validates the selection.</summary>
        /// <summary>Starts the LibreOffice download/install flow after the confirmation prompt.</summary>
        /// <summary>Downloads, verifies and installs LibreOffice on a background STA thread,
        /// reporting progress and result through dashboard actions.</summary>
        /// <summary>Posts the LibreOffice download progress percentage to the dashboard.</summary>
        /// <summary>Shows the LibreOffice install result (restart-required or ready) on the dashboard.</summary>
        /// <summary>Shows the LibreOffice download/install failure message on the dashboard.</summary>
        /// <summary>Starts the LibreOffice uninstall flow after the confirmation prompt.</summary>
        /// <summary>Uninstalls LibreOffice on a background STA thread, reporting the result
        /// through a dashboard action.</summary>
        /// <summary>Allows the user to explicitly adopt an existing system LibreOffice into Clickra's management.</summary>
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
                    MessageBox(hwnd, $"Cannot open browser: {ex.Message}", AppTitle, 0x10);
                }
            }
            else if (element == 24)
            {
                try
                {
                    string dataDir = ClickraStorage.GetDataDir();
                    string logPath = Path.Combine(dataDir, ClickraStorage.HistoryFileName);

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
                    var (subjectText, bodyText) = Localization.BuildDiagnosticsEmail(verStr, timeStr);
                    string subject = Uri.EscapeDataString(subjectText);
                    string body = Uri.EscapeDataString(bodyText);
                    string gmailUrl = $"https://mail.google.com/mail/?view=cm&fs=1&to=jiangyouchen%40gmail.com&su={subject}&body={body}";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = gmailUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox(hwnd, $"Cannot start feedback: {ex.Message}", AppTitle, 0x10);
                }
            }
        }

        /// <summary>Posts an action to run on the dashboard's UI thread.</summary>
        static void PostDashboardAction(IntPtr hwnd, Action action)
        {
            _uiActions.Enqueue(action);
            PostMessageW(hwnd, WM_USER_DASHBOARD_ACTION, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Updates the LibreOffice setup progress/status from a background thread.</summary>
    }
}

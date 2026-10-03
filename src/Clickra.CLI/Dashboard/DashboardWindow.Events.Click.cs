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

        /// <summary>
        /// 待繼續任務列右端的動作按鈕：縮短、延長、繼續、取消。
        /// 天數微調與取消/繼續全部調用 Core 的共用入口。
        /// 取消動作沿用 Fluent 的確認文案（task_parked_cancel_confirm）。
        /// 繼續動作直接啟動 ProgressWindow 續傳，不預先翻轉狀態（保持與 Fluent 一致的安全約定）。
        /// </summary>
        static void HandleParkedActionClick(IntPtr hwnd, int element)
        {
            int offset = element - ParkedActionElementBase;
            int rowIndex = offset / ParkedActionCount;
            int action = offset % ParkedActionCount;

            var parkedItems = ParkedItems;
            if (rowIndex < 0 || rowIndex >= parkedItems.Count) return;
            string taskId = parkedItems[rowIndex].Entry.Id;
            if (string.IsNullOrEmpty(taskId)) return;

            if (action is ParkedActionShorten or ParkedActionExtend)
                HandleParkedRetentionAdjustment(hwnd, taskId, action);
            else if (action == ParkedActionCancel)
                HandleParkedCancel(hwnd, taskId);
            else if (action == ParkedActionResume)
                HandleParkedResume(hwnd, taskId);
        }

        static void HandleParkedRetentionAdjustment(IntPtr hwnd, string taskId, int action)
        {
            int delta = action == ParkedActionExtend
                ? ClickraSettings.ParkedRetentionStepDays
                : -ClickraSettings.ParkedRetentionStepDays;
            if (ClickraStorage.AdjustParkedRetention(taskId, delta) is null) return;

            RefreshHistoryData();
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        static void HandleParkedCancel(IntPtr hwnd, string taskId)
        {
            if (MessageBox(hwnd, GetText("task_parked_cancel_confirm"), AppTitle, 0x24) != 6) return;
            ClickraStorage.CancelParkedTask(taskId);
            RefreshHistoryData();
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        static void HandleParkedResume(IntPtr hwnd, string taskId)
        {
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    ProgressWindow.ShowResume(taskId);
                }
                catch (Exception ex)
                {
                    MessageBox(IntPtr.Zero, $"Execution failed: {ex.Message}", AppTitle, 0x10);
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();

            RefreshHistoryData();
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        /// <summary>True when the element is one of the tab-bar buttons (0-4).</summary>
        static bool IsTabBarElement(int element) => element >= 0 && element <= 4;

        /// <summary>True when the element is one of the settings-page controls.</summary>
        static bool IsSettingsElement(int element)
            => (element >= 1000 && element < 2000) ||
               element == 5 || element == 6 || element == 7 || element == 8 || element == 9 ||
               element == 20 || element == 32 || element == 33 || element == 34 || element == 40 ||
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
            int popupHeight = DashboardLayout.PdfPopupHeight(PdfLangs.Length);
            LayoutRect popup = DashboardLayout.DropdownPopupRect((int)contentX, _pdfLangDropdownY, popupHeight);
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
        static bool HandleLangDropdownClick(IntPtr hwnd, int adjMouseX, int adjMouseY, float logW)
        {
            var langPopup = DashboardLayout.DropdownPopupRect((int)GetContentX(logW), _langDropdownY, DashboardLayout.LanguagePopupHeight);
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

        /// <summary>Handles history tab row expansion and detail-field scrollbar clicks.</summary>
        static bool HandleHistoryClick(IntPtr hwnd, int mouseX, int adjMouseX, int adjMouseY, float logW, float contentX)
        {
            float virtLogW = Math.Max(760f, logW);
            if (adjMouseX < contentX || adjMouseX >= virtLogW - 40) return false;

            HistoryBlock history = GetHistoryBlock(HistoryBlockKind.History);
            FindClickedHistoryRow(history, adjMouseY, out int clickedIndex, out bool clickedDetails, out int detailFieldIndex);
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

        /// <summary>Finds the history row (and optional detail field) under the click point.
        /// 列的位置、列高與明細欄位的命中帶都來自傳入的區塊（Core 的版面表）。</summary>
        static void FindClickedHistoryRow(HistoryBlock history, int adjMouseY, out int clickedIndex, out bool clickedDetails, out int detailFieldIndex)
        {
            clickedIndex = -1;
            clickedDetails = false;
            detailFieldIndex = -1;

            int index = history.RowAt(adjMouseY);
            if (index < 0) return;

            clickedIndex = index;

            // 展開列的分隔線之下是明細區，點在那裡是點欄位而不是收合列。
            int relY = history.RowRelativeY(index, adjMouseY);
            if (index == _expandedHistoryIndex && relY >= DashboardLayout.DetailDividerY)
            {
                clickedDetails = true;
                detailFieldIndex = DashboardLayout.DetailScrollFieldAt(relY);
            }
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
            string textToScroll = GetHistoryDetailText(CompletedItems[rowIndex], fieldIndex);
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

        /// <summary>Returns the text of a history detail field, localizing the user-abort marker.
        /// 取一个項目而不是紀錄：項目已經知道自己是哪一種、是不是被取消（Core 認得新舊兩種
        /// 取消旗標），所以這裡不必再自行比對訊息字串，也保證與繪製端量到的是同一個字串。</summary>
        static string GetHistoryDetailText(HistoryItem item, int fieldIndex)
        {
            var entry = item.Entry;
            if (fieldIndex == 0)
            {
                return (entry.InputPaths ?? "").Replace(";", ", ");
            }
            if (fieldIndex == 1)
            {
                return entry.OutputPath ?? "";
            }
            if (item.IsSuccess)
            {
                return entry.ElapsedMs >= 0 ? $"{entry.ElapsedMs / 1000.0:F2} s ({entry.ElapsedMs} ms)" : "N/A";
            }
            if (item.IsCanceled) return GetText("error_user_aborted");
            return !string.IsNullOrEmpty(entry.ErrorMessage) ? entry.ErrorMessage : "";
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
            if (MessageBox(hwnd, GetText("history_clear_confirm"), AppTitle, 0x24) == 6)
            {
                ClickraStorage.ClearHistory();
                _expandedHistoryIndex = -1;
                RefreshHistoryData();
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
        }

        /// <summary>Handles settings-tab element clicks: toggles, output dirs and office engine selection.</summary>
        [SuppressMessage("SonarQube", "S4036", Justification = "StoreUri is an absolute ms-windows-store URI")]
        static void HandleSettingsClick(IntPtr hwnd, int element)
        {
            if (element >= 1000 && element < 2000)
            {
                HandleDynamicSettingClick(hwnd, element);
                return;
            }

            switch (element)
            {
                case 5: ToggleBoolSetting(hwnd, ClickraSettings.QuietMode); break;
                case 6: ToggleBoolSetting(hwnd, ClickraSettings.Notification); break;
                case 7: ApplySetting(hwnd, ClickraSettings.OutputDir, ClickraSettings.DefaultOutputDirSource); break;
                case 8: ApplySetting(hwnd, ClickraSettings.OutputDir, ClickraSettings.OutputDirDesktop); break;
                case 9: ApplySetting(hwnd, ClickraSettings.OutputDir, ClickraSettings.OutputDirDownloads); break;
                case 20: BrowseOutputDir(hwnd); break;
                case 32: ApplySetting(hwnd, ClickraSettings.OfficeEngine, ClickraSettings.DefaultOfficeEngineAuto); break;
                case 33: ApplySetting(hwnd, ClickraSettings.OfficeEngine, ClickraSettings.OfficeEngineMicrosoft); break;
                case 34: ClickraStorage.SaveSetting(ClickraSettings.OfficeEngine, ClickraSettings.OfficeEngineLibreOffice);
                         ApplySetting(hwnd, ClickraSettings.LibreOfficePath, ClickraSettings.DefaultEmpty); break;
                case 40:
                    if (Clickra.Core.FluentRuntimeHelper.SupportsStoreFluentAddon())
                        OpenStorePage(hwnd);
                    break;
                case 90: AdjustParkedRetention(hwnd, -1); break;
                case 91: AdjustParkedRetention(hwnd, 1); break;
                case 92: SetParkedRetention(hwnd, 0); break;
                case 93: SetParkedRetention(hwnd, 3); break;
                case 94: SetParkedRetention(hwnd, GetDefaultParkedRetentionDays()); break;
                case 95: SetParkedRetention(hwnd, 14); break;
                case 96: SetParkedRetention(hwnd, 30); break;
                default: break; // Unhandled settings element — ignore.
            }
        }

        static void HandleDynamicSettingClick(IntPtr hwnd, int element)
        {
            int descriptorIndex = (element - 1000) / 10;
            int subId = (element - 1000) % 10;

            if (descriptorIndex < 0 || descriptorIndex >= SettingPageRegistry.AllDescriptors.Count) return;
            var descriptor = SettingPageRegistry.AllDescriptors[descriptorIndex];

            switch (descriptor.EditorKind)
            {
                case SettingEditorKind.Toggle:
                    ToggleBoolSetting(hwnd, descriptor.Key);
                    break;

                case SettingEditorKind.Slider:
                {
                    var range = descriptor.GetEffectiveNumericRange();
                    if (!range.HasValue) break;
                    _dynamicSliderDescriptorIndex = descriptorIndex;
                    _isDraggingDynamicSlider = true;
                    SetCapture(hwnd);

                    if (GetCursorPos(out var pt))
                    {
                        ScreenToClient(hwnd, ref pt);
                        float mouseX = (pt.X / _dpiScale) + _contentScrollX;
                        float relX = mouseX - _dynamicSliderTrackX;
                        float fraction = Math.Max(0f, Math.Min(1f, relX / _dynamicSliderTrackW));
                        int span = range.Value.Max - range.Value.Min;
                        int newLevel = Math.Clamp(
                            (int)Math.Round(fraction * span, MidpointRounding.AwayFromZero) + range.Value.Min,
                            range.Value.Min,
                            range.Value.Max);
                        ClickraStorage.SaveSetting(descriptor.Key, newLevel.ToString());
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    break;
                }

                case SettingEditorKind.Number:
                {
                    var range = descriptor.GetEffectiveNumericRange() ?? new NumericSettingRange(0, 100, 0);
                    int current = ClickraStorage.GetSettingInt(descriptor.Key);
                    int delta = subId switch
                    {
                        1 => -1,
                        2 => 1,
                        _ => 0,
                    };
                    int updated = (int)Math.Clamp((long)current + delta, range.Min, range.Max);
                    ClickraStorage.SaveSetting(descriptor.Key, updated.ToString());
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                    break;
                }

                case SettingEditorKind.Choice:
                {
                    if (descriptor.Options != null && subId >= 0 && subId < descriptor.Options.Count)
                    {
                        ApplySetting(hwnd, descriptor.Key, descriptor.Options[subId].Value);
                    }
                    break;
                }

                default:
                    // Ignore unsupported future editor kinds until they have an interaction handler.
                    break;
            }
        }

        private static void AdjustParkedRetention(IntPtr hwnd, int delta)
        {
            int current = ClickraStorage.GetParkedRetentionDays();
            SetParkedRetention(hwnd, current + delta);
        }

        private static int GetDefaultParkedRetentionDays()
            => ClickraSettings.GetDefaultInt(ClickraSettings.ParkedTaskRetention);

        private static void SetParkedRetention(IntPtr hwnd, int days)
        {
            // Same range as the Fluent settings page, from one place: a second copy of the bound
            // would let the two settings pages drift apart.
            int clamped = Math.Clamp(days, ClickraSettings.MinParkedRetentionDays, ClickraSettings.MaxParkedRetentionDays);
            ClickraStorage.SaveSetting(ClickraSettings.ParkedTaskRetention, clamped.ToString());
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        private static void ToggleBoolSetting(IntPtr hwnd, string key)
        {
            bool current = ClickraStorage.GetSetting(key).Equals(ClickraSettings.ValueTrue, StringComparison.OrdinalIgnoreCase);
            ClickraStorage.SaveSetting(key, current ? ClickraSettings.ValueFalse : ClickraSettings.ValueTrue);
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        private static void ApplySetting(IntPtr hwnd, string key, string value)
        {
            ClickraStorage.SaveSetting(key, value);
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        private static void BrowseOutputDir(IntPtr hwnd)
        {
            string title = GetText("setting_output_browse_title");
            string folder = BrowseForFolder(hwnd, title);
            if (!string.IsNullOrEmpty(folder))
                ApplySetting(hwnd, ClickraSettings.OutputDir, folder);
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
            else if (element == 39)
            {
                HandleLibreOfficeAdopt(hwnd);
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
                    ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, candidate);
                    ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeRemovalPendingRestart, ClickraSettings.ValueFalse);
                    MessageBox(hwnd, string.Format(GetText("setting_libreoffice_validated"), Path.GetDirectoryName(candidate)), AppTitle, 0x40);
                }
                else
                {
                    MessageBox(hwnd, GetText("setting_libreoffice_validation_failed"), AppTitle, 0x30);
                }
            }
            else
            {
                MessageBox(hwnd, GetText("setting_libreoffice_invalid"), AppTitle, 0x30);
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
                    MessageBox(hwnd, GetText("setting_libreoffice_download_in_progress"), AppTitle, 0x40);
                    return;
                }
            }

            bool removalPendingRestart = ClickraStorage.GetSetting(ClickraSettings.LibreOfficeRemovalPendingRestart).Equals(ClickraSettings.ValueTrue, StringComparison.OrdinalIgnoreCase);
            var package = LibreOfficeEngineInstaller.RecommendedPackage;
            string installedVersion = LibreOfficeEngineInstaller.GetInstalledSystemVersion();
            if (!removalPendingRestart &&
                !string.IsNullOrWhiteSpace(installedVersion) &&
                LibreOfficeEngineInstaller.IsRecommendedVersionInstalled())
            {
                string resolvedPath = LibreOfficeEngineInstaller.ResolveSystemSofficePath();
                if (!string.IsNullOrWhiteSpace(resolvedPath))
                {
                    ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, resolvedPath);
                }

                MessageBox(
                    hwnd,
                    string.Format(GetText("setting_libreoffice_already_current"), installedVersion),
                    AppTitle,
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

            if (MessageBox(hwnd, prompt, AppTitle, 0x41) != 1) return;

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
                    ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, sofficePath);
                bool managementRecorded = LibreOfficeEngineInstaller.TryRecordManagedSystemInstallation(sofficePath);
                ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeRemovalPendingRestart, ClickraSettings.ValueFalse);

                PostDashboardAction(hwnd, () => ShowInstallResultMessage(
                    hwnd,
                    installResult.RestartRequired,
                    sofficePath,
                    managementRecorded));
            }
            catch (Exception ex)
            {
                ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, ClickraSettings.DefaultEmpty);
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
        private static void ShowInstallResultMessage(
            IntPtr hwnd,
            bool restartRequired,
            string sofficePath,
            bool managementRecorded)
        {
            MessageBox(
                hwnd,
                string.Format(
                    GetText(restartRequired
                        ? "setting_libreoffice_install_restart_required"
                        : "setting_libreoffice_download_ready"),
                    string.IsNullOrWhiteSpace(sofficePath) ? LibreOfficeEngineInstaller.GetDefaultInstallRoot() : sofficePath),
                AppTitle,
                0x40);
            if (!managementRecorded)
                MessageBox(hwnd, GetText("setting_libreoffice_management_unverified"), AppTitle, 0x30);
        }

        /// <summary>Shows the LibreOffice download/install failure message on the dashboard.</summary>
        private static void ShowDownloadFailureMessage(IntPtr hwnd, string errorMessage)
        {
            MessageBox(hwnd, string.Format(GetText("setting_libreoffice_download_failed"), errorMessage), AppTitle, 0x10);
        }

        /// <summary>Starts the LibreOffice uninstall flow after the confirmation prompt.</summary>
        static void HandleLibreOfficeUninstall(IntPtr hwnd)
        {
            lock (_libreOfficeDownloadLock)
            {
                if (_libreOfficeDownloadInProgress)
                {
                    MessageBox(hwnd, GetText("setting_libreoffice_download_in_progress"), AppTitle, 0x40);
                    return;
                }
            }

            if (ClickraStorage.GetSetting(ClickraSettings.LibreOfficeRemovalPendingRestart).Equals(ClickraSettings.ValueTrue, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox(hwnd, GetText("setting_libreoffice_removal_pending"), AppTitle, 0x40);
                return;
            }

            // Only a LibreOffice with a freshly verified Clickra management identity may be removed here.
            // The dashboard hides the button otherwise, but the action still has to refuse on its own.
            if (!LibreOfficeEngineInstaller.WasInstalledByClickra())
            {
                MessageBox(hwnd, GetText("setting_libreoffice_external_note"), AppTitle, 0x40);
                return;
            }

            if (MessageBox(hwnd, GetText("setting_libreoffice_uninstall_confirm"), AppTitle, 0x31) != 1) return;

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

                ClickraStorage.SaveSetting(ClickraSettings.LibreOfficePath, ClickraSettings.DefaultEmpty);
                LibreOfficeEngineInstaller.ReleaseManagement();
                ClickraStorage.SaveSetting(ClickraSettings.LibreOfficeRemovalPendingRestart, uninstallResult.RestartRequired ? ClickraSettings.ValueTrue : ClickraSettings.ValueFalse);
                ClickraStorage.SaveSetting(ClickraSettings.OfficeEngine, ClickraSettings.DefaultOfficeEngineAuto);

                PostDashboardAction(hwnd, () => MessageBox(
                    hwnd,
                    GetText(uninstallResult.RestartRequired
                        ? "setting_libreoffice_uninstall_restart_required"
                        : "setting_libreoffice_uninstall_ready"),
                    AppTitle,
                    0x40));
            }
            catch (Exception ex)
            {
                PostDashboardAction(hwnd, () => MessageBox(
                    hwnd,
                    string.Format(GetText("setting_libreoffice_uninstall_failed"), ex.Message),
                    AppTitle,
                    0x10));
            }
            finally
            {
                PostDashboardAction(hwnd, FinishLibreOfficeSetupStatus);
            }
        }

        /// <summary>Allows the user to explicitly adopt an existing system LibreOffice into Clickra's management.</summary>
        static void HandleLibreOfficeAdopt(IntPtr hwnd)
        {
            lock (_libreOfficeDownloadLock)
            {
                if (_libreOfficeDownloadInProgress)
                {
                    MessageBox(hwnd, GetText("setting_libreoffice_download_in_progress"), AppTitle, 0x40);
                    return;
                }
            }

            if (LibreOfficeEngineInstaller.WasInstalledByClickra())
            {
                return;
            }

            string resolvedPath = LibreOfficeHelper.GetResolvedExecutablePath();
            if (!LibreOfficeEngineInstaller.CanAdoptExistingInstallation(resolvedPath))
            {
                return;
            }

            if (MessageBox(hwnd, GetText("setting_libreoffice_adopt_confirm"), AppTitle, 0x31) != 1) return;

            try
            {
                LibreOfficeEngineInstaller.AdoptExistingInstallation();
            }
            catch (InvalidOperationException)
            {
                MessageBox(hwnd, GetText("setting_libreoffice_external_note"), AppTitle, 0x30);
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return;
            }
            InvalidateRect(hwnd, IntPtr.Zero, false);
            MessageBox(hwnd, GetText("setting_libreoffice_adopt_success"), AppTitle, 0x40);
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
                // PDF compress slider clicked — snap to nearest stop via equal-width segments + enable drag
                float relX = adjMouseX - _pdfSliderTrackX;
                float fraction = Math.Max(0f, Math.Min(1f, relX / _pdfSliderTrackW));
                int span = ClickraSettings.MaxPdfCompressLevel - ClickraSettings.MinPdfCompressLevel;
                int newLevel = ClickraSettings.ClampNumericSetting(
                    ClickraSettings.PdfCompressImageLevel,
                    (int)Math.Round(fraction * span, MidpointRounding.AwayFromZero) + ClickraSettings.MinPdfCompressLevel);
                ApplyPdfCompressLevel(hwnd, newLevel);
                _isDraggingPdfSlider = true;
                SetCapture(hwnd);
            }
            else if (element == 81)
            {
                bool current = ClickraStorage.GetSettingBool(ClickraSettings.PdfCompressStripFonts);
                ClickraStorage.SaveSetting(ClickraSettings.PdfCompressStripFonts, current ? ClickraSettings.ValueFalse : ClickraSettings.ValueTrue);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 82)
            {
                bool current = ClickraStorage.GetSettingBool(ClickraSettings.PdfCompressMinifyContent);
                ClickraStorage.SaveSetting(ClickraSettings.PdfCompressMinifyContent, current ? ClickraSettings.ValueFalse : ClickraSettings.ValueTrue);
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

        /// <summary>Formats a byte count as a human-readable size string.</summary>
        static string FormatBytes(long bytes)
        {
            const double mb = 1024d * 1024d;
            return $"{bytes / mb:F0} MB";
        }

        /// <summary>Applies a PDF compression level selection and refreshes the settings tab.</summary>
        static void ApplyPdfCompressLevel(IntPtr hwnd, int level)
        {
            int clamped = ClickraSettings.ClampNumericSetting(ClickraSettings.PdfCompressImageLevel, level);
            ClickraStorage.SaveSetting(ClickraSettings.PdfCompressImageLevel, clamped.ToString());
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

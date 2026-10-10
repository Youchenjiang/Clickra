using System;
using System.Drawing;
using Clickra.Core;
using Clickra.Core.Layout;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI;

    public static partial class DashboardWindow
    {
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
    }

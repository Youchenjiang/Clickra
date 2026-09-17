using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using Microsoft.Win32;
using Clickra.Core;
using Clickra.Core.Layout;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        /// <summary>待繼續任務即將被清理時使用的警示色（與 Fluent 的琥珀色一致）。</summary>
        private static readonly Color ParkedAlertColor = Color.FromArgb(255, 160, 40);

        /// <summary>Draws the history tab: header, filter chips and the scrollable entry list.</summary>
        static void DrawHistoryTab(Graphics g, float logW, float logH, float contentX)
        {
            float s = _dpiScale;
            // Title
            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_history"), _contentTitleFont, Brushes.White, contentX * s, 30 * s);

            // Clear button
            bool isClearHovered = _hoveredElement == 22;
            Color btnBg = isClearHovered ? Color.FromArgb(70, 70, 70) : Color.FromArgb(50, 50, 50);
            Color btnBorder = isClearHovered ? Color.FromArgb(90, 90, 90) : Color.FromArgb(70, 70, 70);
            LayoutRect clearRect = DashboardLayout.HistoryClearButtonRect((int)logW);
            using (var btnBgBrush = new SolidBrush(btnBg))
            using (var btnBorderPen = new Pen(btnBorder))
            using (var path = UIHelper.GetRoundedRectPath(new RectangleF(clearRect.X * s, clearRect.Y * s, clearRect.Width * s, clearRect.Height * s), 4 * s))
            {
                g.FillPath(btnBgBrush, path);
                g.DrawPath(btnBorderPen, path);
            }
            if (_subFont != null)
            {
                Color btnText = isClearHovered ? Color.White : Color.FromArgb(200, 200, 200);
                using var btnTextBrush = new SolidBrush(btnText);
                var size = g.MeasureString(GetText("history_clear"), _subFont);
                g.DrawString(GetText("history_clear"), _subFont, btnTextBrush,
                    (clearRect.X + (clearRect.Width - size.Width / s) / 2) * s,
                    (clearRect.Y + (clearRect.Height - size.Height / s) / 2) * s);
            }

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, contentX * s, 75 * s, (logW - 40) * s, 75 * s);
            }

            // 清單的三個切片（Core 的 HistoryFeed，與 Fluent 的 History 頁共用同一份）：進行中、
            // 待繼續、已完成。狀態文字、檔案描述與剩餘期限都在模型裡算好一次，這裡只排版與上色。
            var activeTasks = ActiveItems;

            // 三個區塊的列位置全部出自同一份堆疊（Core 的版面表），繪製不自己累加。
            IReadOnlyList<HistoryBlock> stack = GetHistoryStack();
            HistoryBlock activeBlock = HistoryLayout.Find(stack, HistoryBlockKind.ActiveQueue);
            HistoryBlock parkedBlock = HistoryLayout.Find(stack, HistoryBlockKind.Parked);
            HistoryBlock historyBlock = HistoryLayout.Find(stack, HistoryBlockKind.History);

            int rowW = (int)logW - (int)contentX - 40;

            // ——— 顯示進行中任務佇列（置頂）———
            for (int activeIndex = 0; activeIndex < activeTasks.Count; activeIndex++)
            {
                var item = activeTasks[activeIndex];
                int rowY = activeBlock.RowTop(activeIndex);
                int rowH = activeBlock.RowHeight(activeIndex);

                // 背景與邊框仍由狀態決定顏色（這是呈現層的判斷），文字則由模型提供。
                ConversionStatus fileStatus = item.Entry.Status;

                    // Background color & border color based on fileStatus
                    Color activeBgColor = fileStatus switch
                    {
                        ConversionStatus.Pending    => Color.FromArgb(38, 38, 48),
                        ConversionStatus.InProgress => Color.FromArgb(30, 42, 55),
                        ConversionStatus.Success    => Color.FromArgb(30, 44, 34),
                        ConversionStatus.Failed     => Color.FromArgb(50, 32, 32),
                        _                           => Color.FromArgb(36, 36, 36)
                    };
                    Color activeBorderColor = fileStatus switch
                    {
                        ConversionStatus.Pending    => Color.FromArgb(70, 70, 100),
                        ConversionStatus.InProgress => Color.FromArgb(0, 120, 212),
                        ConversionStatus.Success    => Color.FromArgb(50, 160, 80),
                        ConversionStatus.Failed     => Color.FromArgb(200, 60, 60),
                        _                           => Color.FromArgb(60, 60, 60)
                    };

                    using (var path = UIHelper.GetRoundedRectPath(new RectangleF(contentX * s, rowY * s, rowW * s, rowH * s), 6 * s))
                    using (var rowBg = new SolidBrush(activeBgColor))
                    {
                        g.FillPath(rowBg, path);
                        using (var borderPen = new Pen(activeBorderColor))
                        {
                            g.DrawPath(borderPen, path);
                        }
                    }

                    // Render Time
                    float timeW = 120;
                    if (_bodyFont != null)
                    {
                        using var timeBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                        g.DrawString(item.Time, _bodyFont, timeBrush, (contentX + 12) * s, (rowY + 13) * s);
                        timeW = g.MeasureString(item.Time, _bodyFont).Width / s;
                    }

                    // Command Tag
                    float tagX = contentX + 12 + timeW + 16;
                    float tagW = DrawCommandTag(g, item, tagX, rowY + 11);

                    // Status Label (量測實際寬度，靠右對齊)：文字由 Core 依狀態決定，
                    // 顏色留在這裡 —— 同一份文字在兩個介面可以有不同視覺權重。
                    string statusText = GetText(item.StatusKey);
                    Color statusColor = fileStatus switch
                    {
                        ConversionStatus.Pending    => Color.FromArgb(180, 180, 100),
                        ConversionStatus.InProgress => Color.FromArgb(80, 160, 240),
                        ConversionStatus.Success    => Color.FromArgb(100, 220, 100),
                        ConversionStatus.Failed     => Color.FromArgb(255, 90, 70),
                        _                           => Color.Gray
                    };
                    float activeStatusW = _tagFont != null ? g.MeasureString(statusText, _tagFont).Width / s : 50f;
                    float activeStatusX = contentX + rowW - 16 - activeStatusW;

                    // Filename (tag 之後到 status 之前的所有空間)
                    if (_bodyFont != null)
                    {
                        using var countBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                        float fileCountX = tagX + tagW + 16;

                        // 「檔名 + N 個檔案」的組成在 Core（HistoryItem.FileCountText），與 Fluent 同一份。
                        string displayText = item.FileCountText;

                        float maxW = activeStatusX - 16 - fileCountX;
                        if (maxW > 20)
                        {
                            displayText = UIHelper.TruncateFileName(g, displayText, _bodyFont, maxW, s);
                        }
                        g.DrawString(displayText, _bodyFont, countBrush, fileCountX * s, (rowY + 13) * s);
                    }

                    if (_tagFont != null)
                    {
                        using var statusBrush = new SolidBrush(statusColor);
                        g.DrawString(statusText, _tagFont, statusBrush, activeStatusX * s, (rowY + 13) * s);
                    }

            }

            // ——— 顯示待繼續（已暫存）任務：逐項顯示還剩幾天過期 ———
            DrawParkedQueue(g, contentX, rowW, parkedBlock);

            // ——— 顯示持久化歷史紀錄———
            if (CompletedItems.Count == 0)
            {
                if (activeTasks.Count == 0 && ParkedItems.Count == 0 && _tabFont != null)
                {
                    using var textBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
                    g.DrawString(GetText("history_empty"), _tabFont, textBrush, contentX * s, 100 * s);
                }
                return;
            }

            // 每一列的 Y 與高度都取自堆疊（與命中測試／hover／捲動高度同一份）。
            for (int i = 0; i < CompletedItems.Count; i++)
            {
                var item = CompletedItems[i];
                var entry = item.Entry;
                bool isExpanded = (i == _expandedHistoryIndex);
                int currentY = historyBlock.RowTop(i);
                int currentH = historyBlock.RowHeight(i);

                // Optimization: Skip rendering if item is completely outside viewport
                if (currentY + currentH < _contentScrollY || currentY > _contentScrollY + logH)
                {
                    continue;
                }

                using var path = UIHelper.GetRoundedRectPath(new RectangleF(contentX * s, currentY * s, rowW * s, currentH * s), 6 * s);
                using var rowBg = new SolidBrush(Color.FromArgb(36, 36, 36));
                g.FillPath(rowBg, path);

                using var borderPen = new Pen(Color.FromArgb(48, 48, 48));
                g.DrawPath(borderPen, path);

                // 時間與相對排版計算
                float timeW = 120;
                if (_bodyFont != null)
                {
                    using var timeBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    g.DrawString(item.Time, _bodyFont, timeBrush, (contentX + 12) * s, (currentY + 13) * s);
                    timeW = g.MeasureString(item.Time, _bodyFont).Width / s;
                }

                // 指令標籤 (動態相對起點)
                float tagX = contentX + 12 + timeW + 16;
                float tagW = DrawCommandTag(g, item, tagX, currentY + 11);

                // 狀態標籤與顏色計算：文字由 Core 決定（成功／失敗／已取消）。
                // 以前這裡自己比對 "User Aborted" 這個旗標，而 Core 認得新舊兩種取消旗標，
                // 所以用新旗標寫入的取消在本頁曾顯示成「錯誤」。
                Color statusColor = item.IsSuccess ? Color.FromArgb(100, 220, 100) : Color.FromArgb(255, 90, 70);
                string statusText = GetText(item.StatusKey);

                float statusW = _tagFont != null ? g.MeasureString(statusText, _tagFont).Width / s : 50f;
                float statusX = contentX + rowW - 16 - statusW;

                // 檔案名稱：tag 之後到 status 之前的所有空間
                if (_bodyFont != null)
                {
                    using var countBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                    float fileCountX = tagX + tagW + 16;
                    string displayText = item.FileCountText;
                    float maxW = statusX - 16 - fileCountX;
                    if (maxW > 20)
                    {
                        displayText = UIHelper.TruncateFileName(g, displayText, _bodyFont, maxW, s);
                    }
                    g.DrawString(displayText, _bodyFont, countBrush, fileCountX * s, (currentY + 13) * s);
                }

                // 繪製狀態標籤（靠右）
                if (_tagFont != null)
                {
                    using var statusBrush = new SolidBrush(statusColor);
                    g.DrawString(statusText, _tagFont, statusBrush, statusX * s, (currentY + 13) * s);
                }

                // Render Expanded Details
                if (isExpanded)
                {
                    // 明細欄位的 Y 與捲軸位置都來自版面表，與點擊／滾輪用的命中帶是同一份。
                    int inputsY = DashboardLayout.DetailFieldY(0);
                    int outputsY = DashboardLayout.DetailFieldY(1);
                    int timeY = DashboardLayout.DetailFieldY(2);
                    int resultY = DashboardLayout.DetailFieldY(3);

                    using (var cardDivPen = new Pen(Color.FromArgb(56, 56, 56)))
                    {
                        g.DrawLine(cardDivPen, (contentX + 12) * s, (currentY + DashboardLayout.DetailDividerY) * s, (contentX + rowW - 12) * s, (currentY + DashboardLayout.DetailDividerY) * s);
                    }

                    if (_subFont != null)
                    {
                        using var labelBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                        using var valBrush = new SolidBrush(Color.FromArgb(220, 220, 220));

                        // Measure label widths to draw values relatively
                        float w1 = g.MeasureString(GetText("history_detail_inputs") + ":", _subFont).Width / s;
                        float w2 = g.MeasureString(GetText("history_detail_outputs") + ":", _subFont).Width / s;
                        float w3 = g.MeasureString(GetText("history_detail_time") + ":", _subFont).Width / s;
                        float w4 = g.MeasureString(GetText(item.IsSuccess ? "history_detail_elapsed" : "history_detail_error") + ":", _subFont).Width / s;
                        float maxLabelW = Math.Max(w1, Math.Max(w2, Math.Max(w3, w4)));
                        float valX = contentX + 12 + maxLabelW + 16;
                        float maxValW = contentX + rowW - 12 - valX;

                        // 1. Files / Input Paths
                        g.DrawString(GetText("history_detail_inputs") + ":", _subFont, labelBrush, (contentX + 12) * s, (currentY + inputsY) * s);
                        string inputsText = entry.InputPaths;
                        if (string.IsNullOrEmpty(inputsText)) inputsText = "N/A";
                        else inputsText = inputsText.Replace(";", ", ");

                        float scrollOffset0 = 0;
                        DetailScrollOffsets.TryGetValue((i, 0), out scrollOffset0);
                        var state0 = g.Save();
                        g.IntersectClip(new RectangleF(valX * s, (currentY + inputsY) * s, maxValW * s, 20 * s));
                        g.DrawString(inputsText, _subFont, valBrush, (valX - scrollOffset0) * s, (currentY + inputsY) * s);
                        g.Restore(state0);

                        // Draw inputs scrollbar if scrollable
                        float textW0 = g.MeasureString(inputsText, _subFont).Width / s;
                        if (textW0 > maxValW)
                        {
                            float scrollbarY = currentY + DashboardLayout.DetailScrollbarY(0);
                            float thumbW = Math.Max(15f, (maxValW / textW0) * maxValW);
                            float thumbX = valX + (scrollOffset0 / textW0) * maxValW;
                            if (thumbX + thumbW > valX + maxValW) thumbX = valX + maxValW - thumbW;
                            UIHelper.DrawHorizontalScrollbar(g, valX, scrollbarY, maxValW, thumbX, thumbW, s);
                        }

                        // 2. Output Path
                        g.DrawString(GetText("history_detail_outputs") + ":", _subFont, labelBrush, (contentX + 12) * s, (currentY + outputsY) * s);
                        string outputsText = entry.OutputPath;
                        if (string.IsNullOrEmpty(outputsText)) outputsText = "N/A";

                        float scrollOffset1 = 0;
                        DetailScrollOffsets.TryGetValue((i, 1), out scrollOffset1);
                        var state1 = g.Save();
                        g.IntersectClip(new RectangleF(valX * s, (currentY + outputsY) * s, maxValW * s, 20 * s));
                        g.DrawString(outputsText, _subFont, valBrush, (valX - scrollOffset1) * s, (currentY + outputsY) * s);
                        g.Restore(state1);

                        // Draw outputs scrollbar if scrollable
                        float textW1 = g.MeasureString(outputsText, _subFont).Width / s;
                        if (textW1 > maxValW)
                        {
                            float scrollbarY = currentY + DashboardLayout.DetailScrollbarY(1);
                            float thumbW = Math.Max(15f, (maxValW / textW1) * maxValW);
                            float thumbX = valX + (scrollOffset1 / textW1) * maxValW;
                            if (thumbX + thumbW > valX + maxValW) thumbX = valX + maxValW - thumbW;
                            UIHelper.DrawHorizontalScrollbar(g, valX, scrollbarY, maxValW, thumbX, thumbW, s);
                        }

                        // 3. Time Details
                        g.DrawString(GetText("history_detail_time") + ":", _subFont, labelBrush, (contentX + 12) * s, (currentY + timeY) * s);
                        string timeText = $"{entry.Time}  →  {(string.IsNullOrEmpty(entry.EndTime) ? entry.Time : entry.EndTime)}";
                        timeText = UIHelper.TruncateText(g, timeText, _subFont, maxValW, s);
                        g.DrawString(timeText, _subFont, valBrush, valX * s, (currentY + timeY) * s);

                        // 4. Elapsed Time or Error Message
                        if (item.IsSuccess)
                        {
                            g.DrawString(GetText("history_detail_elapsed") + ":", _subFont, labelBrush, (contentX + 12) * s, (currentY + resultY) * s);
                            string elapsedText = entry.ElapsedMs >= 0 ? $"{(entry.ElapsedMs / 1000.0):F2} s ({entry.ElapsedMs} ms)" : "N/A";
                            elapsedText = UIHelper.TruncateText(g, elapsedText, _subFont, maxValW, s);
                            g.DrawString(elapsedText, _subFont, valBrush, valX * s, (currentY + resultY) * s);
                        }
                        else
                        {
                            g.DrawString(GetText("history_detail_error") + ":", _subFont, labelBrush, (contentX + 12) * s, (currentY + resultY) * s);
                            // 取消用的旗標有新舊兩種，Core 都認得；這裡只問「是不是取消」。
                            string errorText = item.IsCanceled
                                ? GetText("error_user_aborted")
                                : (!string.IsNullOrEmpty(entry.ErrorMessage) ? entry.ErrorMessage : "N/A");

                            float scrollOffset2 = 0;
                            DetailScrollOffsets.TryGetValue((i, 2), out scrollOffset2);
                            var state2 = g.Save();
                            g.IntersectClip(new RectangleF(valX * s, (currentY + resultY) * s, maxValW * s, 20 * s));
                            g.DrawString(errorText, _subFont, valBrush, (valX - scrollOffset2) * s, (currentY + resultY) * s);
                            g.Restore(state2);

                            // Draw error scrollbar if scrollable
                            float textW2 = g.MeasureString(errorText, _subFont).Width / s;
                            if (textW2 > maxValW)
                            {
                                float scrollbarY = currentY + DashboardLayout.DetailScrollbarY(2);
                                float thumbW = Math.Max(15f, (maxValW / textW2) * maxValW);
                                float thumbX = valX + (scrollOffset2 / textW2) * maxValW;
                                if (thumbX + thumbW > valX + maxValW) thumbX = valX + maxValW - thumbW;
                                UIHelper.DrawHorizontalScrollbar(g, valX, scrollbarY, maxValW, thumbX, thumbW, s);
                            }
                        }
                    }
                }

            }
        }

        /// <summary>畫出待繼續（已暫存）任務，每列帶自己的剩餘保留期限，讓保留期限是逐項可見的，
        /// 而不只是設定頁上那句話。區塊標題畫在區塊的 Top，每一列的位置由區塊自己提供。</summary>
        static void DrawParkedQueue(Graphics g, float contentX, int rowW, HistoryBlock block)
        {
            if (block.IsEmpty) return;

            int startY = block.Top;

            float s = _dpiScale;
            // 待繼續任務與它們的剩餘期限都來自同一份清單，即將過期的數量也在模型裡算好 ——
            // 不必（也不該）逐列重算期限，那是每張畫格一次的檔案查詢。
            var parkedItems = ParkedItems;
            int expiringSoonCount = _historyFeed.ExpiringSoonCount;

            // 區塊標題；有任務即將被清理時，右側補一句聚合警示；否則顯示說明文字。
            if (_tabFont != null)
            {
                using var titleBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                g.DrawString(GetText("task_parked_title"), _tabFont, titleBrush, contentX * s, startY * s);

                if (expiringSoonCount > 0)
                {
                    string warning = string.Format(GetText("task_parked_expiring_warning"), expiringSoonCount);
                    using var warningBrush = new SolidBrush(ParkedAlertColor);
                    var warningSize = g.MeasureString(warning, _tabFont);
                    g.DrawString(warning, _tabFont, warningBrush,
                        (contentX + rowW - warningSize.Width / s) * s, startY * s);
                }
                else
                {
                    string desc = GetText("task_parked_desc");
                    using var descBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    var fontToUse = _subFont ?? _tabFont;
                    var descSize = g.MeasureString(desc, fontToUse);
                    g.DrawString(desc, fontToUse, descBrush,
                        (contentX + rowW - descSize.Width / s) * s, (startY + 2) * s);
                }
            }

            for (int i = 0; i < parkedItems.Count; i++)
            {
                var item = parkedItems[i];
                var info = item.Retention!.Value;
                int currentY = block.RowTop(i);
                int rowH = block.RowHeight(i);
                bool needsAttention = item.NeedsAttention;

                Color rowBg = needsAttention ? Color.FromArgb(48, 40, 30) : Color.FromArgb(34, 34, 40);
                Color rowBorder = info.HasExpired
                    ? Color.FromArgb(200, 60, 60)
                    : info.IsExpiringSoon ? ParkedAlertColor : Color.FromArgb(70, 70, 100);

                using (var path = UIHelper.GetRoundedRectPath(new RectangleF(contentX * s, currentY * s, rowW * s, rowH * s), 6 * s))
                using (var rowBgBrush = new SolidBrush(rowBg))
                {
                    g.FillPath(rowBgBrush, path);
                    using var borderPen = new Pen(rowBorder);
                    g.DrawPath(borderPen, path);
                }

                // 時間
                float timeW = 120;
                if (_bodyFont != null)
                {
                    using var timeBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    g.DrawString(item.Time, _bodyFont, timeBrush, (contentX + 12) * s, (currentY + 13) * s);
                    timeW = g.MeasureString(item.Time, _bodyFont).Width / s;
                }

                // 指令標籤
                float tagX = contentX + 12 + timeW + 16;
                float tagW = DrawCommandTag(g, item, tagX, currentY + 11);

                // 即將過期徽章（與 Fluent 對齊）：若有即將過期或已過期任務，顯示醒目標籤
                float nextContentX = tagX + tagW + 12;
                if (needsAttention)
                {
                    string badgeText = GetText("task_parked_badge_expiring");
                    float badgeTextW = _tagFont != null ? g.MeasureString(badgeText, _tagFont).Width / s : 48f;
                    float badgeW = badgeTextW + 14f;
                    float badgeH = 22f;

                    using var badgePath = UIHelper.GetRoundedRectPath(new RectangleF(nextContentX * s, (currentY + 11) * s, badgeW * s, badgeH * s), 4 * s);
                    using var badgeBgBrush = new SolidBrush(Color.FromArgb(50, 40, 20));
                    using var badgeBorderPen = new Pen(ParkedAlertColor);
                    g.FillPath(badgeBgBrush, badgePath);
                    g.DrawPath(badgeBorderPen, badgePath);

                    if (_tagFont != null)
                    {
                        using var badgeTextBrush = new SolidBrush(ParkedAlertColor);
                        g.DrawString(badgeText, _tagFont, badgeTextBrush, (nextContentX + 7) * s, (currentY + 14) * s);
                    }
                    nextContentX += badgeW + 12;
                }

                // 剩餘保留期限（靠右，但要讓開微調鈕）：一列一期限，就是這個區塊存在的理由。
                // 右端位置由版面表提供，與命中判定同一個算式。
                float ttlRight = DashboardLayout.ParkedRowTtlRight((int)contentX, rowW);
                string ttlText = item.RetentionText;
                float ttlW = _tagFont != null ? g.MeasureString(ttlText, _tagFont).Width / s : 60f;
                if (_tagFont != null && ttlW > ttlRight - contentX - 12)
                {
                    ttlText = UIHelper.TruncateText(g, ttlText, _tagFont, ttlRight - contentX - 12, s);
                    ttlW = g.MeasureString(ttlText, _tagFont).Width / s;
                }
                float ttlX = Math.Max(contentX + 12, ttlRight - ttlW);

                if (_bodyFont != null)
                {
                    using var fileBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                    // 「檔案 · 第 i/n 檔 · 原因」的組合在 Core（HistoryItem.Subtitle），
                    // 與 Fluent 的待繼續列是同一份文字。
                    string displayText = item.Subtitle;

                    float fileX = nextContentX;
                    float maxW = ttlX - 16 - fileX;
                    if (maxW > 20)
                    {
                        displayText = UIHelper.TruncateFileName(g, displayText, _bodyFont, maxW, s);
                    }
                    g.DrawString(displayText, _bodyFont, fileBrush, fileX * s, (currentY + 13) * s);
                }

                if (_tagFont != null)
                {
                    Color ttlColor = info.HasExpired
                        ? Color.FromArgb(255, 90, 70)
                        : needsAttention ? ParkedAlertColor : Color.FromArgb(150, 150, 160);
                    using var ttlBrush = new SolidBrush(ttlColor);
                    g.DrawString(ttlText, _tagFont, ttlBrush, ttlX * s, (currentY + 13) * s);
                }

                // 這一列自己的期限微調鈕（縮短、延長）。標籤用「符號 + 天數」，天數來自
                // Core 的共用常數，所以兩個介面移動的天數永遠一致。
                DrawParkedActionButtons(g, contentX, rowW, currentY, rowH, i, info.IsUnlimited);
            }
        }

        /// <summary>畫一列的動作按鈕：縮短（−）、延長（＋）、繼續與取消。位置來自版面表，
        /// hover 與點擊也用它 —— 座標與尺寸永遠是同一個矩形。已經是無限期（永久保留）的任務，
        /// 「延長」沒有意義，所以畫成停用狀態（按下也確實是 no-op）。</summary>
        static void DrawParkedActionButtons(Graphics g, float contentX, int rowW, int rowY, int rowH, int rowIndex, bool isUnlimited)
        {
            float s = _dpiScale;
            for (int action = 0; action < ParkedActionCount; action++)
            {
                bool isExtend = action == ParkedActionExtend;
                bool isResume = action == ParkedActionResume;
                bool isCancel = action == ParkedActionCancel;
                bool enabled = !(isExtend && isUnlimited);
                bool isHovered = enabled && _hoveredElement == ParkedActionElement(rowIndex, action);

                LayoutRect rect = DashboardLayout.ParkedRowActionRect((int)contentX, rowW, rowY, rowH, action);
                Color bg = isHovered
                    ? (isResume ? Color.FromArgb(0, 100, 180) : isCancel ? Color.FromArgb(120, 50, 50) : Color.FromArgb(70, 70, 70))
                    : Color.FromArgb(46, 46, 54);
                Color border = isHovered
                    ? (isResume ? Color.FromArgb(40, 140, 230) : isCancel ? Color.FromArgb(180, 70, 70) : Color.FromArgb(110, 110, 110))
                    : Color.FromArgb(70, 70, 80);
                Color label = enabled ? Color.FromArgb(210, 210, 210) : Color.FromArgb(110, 110, 110);

                using (var path = UIHelper.GetRoundedRectPath(new RectangleF(rect.X * s, rect.Y * s, rect.Width * s, rect.Height * s), 4 * s))
                using (var bgBrush = new SolidBrush(bg))
                {
                    g.FillPath(bgBrush, path);
                    using var borderPen = new Pen(border);
                    g.DrawPath(borderPen, path);
                }

                if (_tagFont != null)
                {
                    string text = action switch
                    {
                        ParkedActionShorten => "-" + ClickraSettings.ParkedRetentionStepDays,
                        ParkedActionExtend  => "+" + ClickraSettings.ParkedRetentionStepDays,
                        ParkedActionResume  => GetText("fluent_task_resume"),
                        ParkedActionCancel  => GetText("dialog_cancel"),
                        _                   => ""
                    };
                    var size = g.MeasureString(text, _tagFont);
                    using var labelBrush = new SolidBrush(label);
                    g.DrawString(text, _tagFont, labelBrush,
                        (rect.X + (rect.Width - size.Width / s) / 2) * s,
                        (rect.Y + (rect.Height - size.Height / s) / 2) * s);
                }
            }
        }

        /// <summary>Draws a colored command tag for a history item and returns its width.
        /// 標籤文字來自項目的 CommandLabelKey（Core 的 ConvertCommandRegistry 決定），
        /// 顏色來自指令註冊表；兩者都不再從指令字串自行推導。</summary>
        static float DrawCommandTag(Graphics g, HistoryItem item, float x, float y)
        {
            float s = _dpiScale;
            Color tagBg = Color.FromArgb(100, 100, 100);
            string text = GetText(item.CommandLabelKey);
            if (ConvertCommandByKey.TryGetValue(item.Entry.Command ?? string.Empty, out var def))
            {
                tagBg = def.TagColor;
            }

            float textW = 0;
            if (_tagFont != null)
            {
                textW = g.MeasureString(text, _tagFont).Width / s;
            }
            float w = Math.Max(82f, textW + 16f);
            int h = 22;
            using var path = UIHelper.GetRoundedRectPath(new RectangleF(x * s, y * s, w * s, h * s), 4 * s);
            using var brush = new SolidBrush(tagBg);
            g.FillPath(brush, path);

            if (_tagFont != null)
            {
                var size = g.MeasureString(text, _tagFont);
                g.DrawString(text, _tagFont, Brushes.White, (x + (w - size.Width / s) / 2) * s, (y + (h - size.Height / s) / 2) * s);
            }
            return w;
        }
    }
}

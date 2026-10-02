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
            DrawHistoryHeader(g, logW, contentX, s);

            // 清單的三個切片（Core 的 HistoryFeed，與 Fluent 的 History 頁共用同一份）：進行中、
            // 待繼續、已完成。狀態文字、檔案描述與剩餘期限都在模型裡算好一次，這裡只排版與上色。
            var activeTasks = ActiveItems;

            // 三個區塊的列位置全部出自同一份堆疊（Core 的版面表），繪製不自己累加。
            IReadOnlyList<HistoryBlock> stack = GetHistoryStack();
            HistoryBlock activeBlock = HistoryLayout.Find(stack, HistoryBlockKind.ActiveQueue);
            HistoryBlock parkedBlock = HistoryLayout.Find(stack, HistoryBlockKind.Parked);
            HistoryBlock historyBlock = HistoryLayout.Find(stack, HistoryBlockKind.History);

            int rowW = (int)logW - (int)contentX - 40;
            DrawActiveHistoryRows(g, contentX, rowW, activeBlock, activeTasks, s);

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

            DrawCompletedHistoryRows(g, contentX, rowW, logH, historyBlock, s);
        }

        static void DrawHistoryHeader(Graphics g, float logW, float contentX, float s)
        {
            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_history"), _contentTitleFont, Brushes.White, contentX * s, 30 * s);

            bool isClearHovered = _hoveredElement == 22;
            Color btnBg = isClearHovered ? Color.FromArgb(70, 70, 70) : Color.FromArgb(50, 50, 50);
            Color btnBorder = isClearHovered ? Color.FromArgb(90, 90, 90) : Color.FromArgb(70, 70, 70);
            LayoutRect clearRect = DashboardLayout.HistoryClearButtonRect((int)logW);
            using var btnBgBrush = new SolidBrush(btnBg);
            using var btnBorderPen = new Pen(btnBorder);
            using var path = UIHelper.GetRoundedRectPath(new RectangleF(clearRect.X * s, clearRect.Y * s, clearRect.Width * s, clearRect.Height * s), 4 * s);
            g.FillPath(btnBgBrush, path);
            g.DrawPath(btnBorderPen, path);

            if (_subFont != null)
            {
                Color btnText = isClearHovered ? Color.White : Color.FromArgb(200, 200, 200);
                using var btnTextBrush = new SolidBrush(btnText);
                var size = g.MeasureString(GetText("history_clear"), _subFont);
                g.DrawString(GetText("history_clear"), _subFont, btnTextBrush,
                    (clearRect.X + (clearRect.Width - size.Width / s) / 2) * s,
                    (clearRect.Y + (clearRect.Height - size.Height / s) / 2) * s);
            }

            using var divPen = new Pen(Color.FromArgb(48, 48, 48));
            g.DrawLine(divPen, contentX * s, 75 * s, (logW - 40) * s, 75 * s);
        }

        static void DrawActiveHistoryRows(Graphics g, float contentX, int rowW, HistoryBlock block,
            IReadOnlyList<HistoryItem> activeTasks, float s)
        {
            for (int activeIndex = 0; activeIndex < activeTasks.Count; activeIndex++)
                DrawActiveHistoryRow(g, contentX, rowW, block, activeTasks[activeIndex], activeIndex, s);
        }

        static void DrawActiveHistoryRow(Graphics g, float contentX, int rowW, HistoryBlock block,
            HistoryItem item, int activeIndex, float s)
        {
            int rowY = block.RowTop(activeIndex);
            int rowH = block.RowHeight(activeIndex);
            ConversionStatus fileStatus = item.Entry.Status;

            using var path = UIHelper.GetRoundedRectPath(new RectangleF(contentX * s, rowY * s, rowW * s, rowH * s), 6 * s);
            using var rowBg = new SolidBrush(ActiveRowBackground(fileStatus));
            using var borderPen = new Pen(ActiveRowBorder(fileStatus));
            g.FillPath(rowBg, path);
            g.DrawPath(borderPen, path);

            float timeW = 120;
            if (_bodyFont != null)
            {
                using var timeBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(item.Time, _bodyFont, timeBrush, (contentX + 12) * s, (rowY + 13) * s);
                timeW = g.MeasureString(item.Time, _bodyFont).Width / s;
            }

            float tagX = contentX + 12 + timeW + 16;
            float tagW = DrawCommandTag(g, item, tagX, rowY + 11);
            string statusText = GetText(item.StatusKey);
            float statusW = _tagFont != null ? g.MeasureString(statusText, _tagFont).Width / s : 50f;
            float statusX = contentX + rowW - 16 - statusW;

            if (_bodyFont != null)
            {
                using var countBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                float fileCountX = tagX + tagW + 16;
                string displayText = item.FileCountText;
                float maxW = statusX - 16 - fileCountX;
                if (maxW > 20)
                    displayText = UIHelper.TruncateFileName(g, displayText, _bodyFont, maxW, s);
                g.DrawString(displayText, _bodyFont, countBrush, fileCountX * s, (rowY + 13) * s);
            }

            if (_tagFont != null)
            {
                using var statusBrush = new SolidBrush(ActiveStatusColor(fileStatus));
                g.DrawString(statusText, _tagFont, statusBrush, statusX * s, (rowY + 13) * s);
            }
        }

        static Color ActiveRowBackground(ConversionStatus status) => status switch
        {
            ConversionStatus.Pending => Color.FromArgb(38, 38, 48),
            ConversionStatus.InProgress => Color.FromArgb(30, 42, 55),
            ConversionStatus.Success => Color.FromArgb(30, 44, 34),
            ConversionStatus.Failed => Color.FromArgb(50, 32, 32),
            _ => Color.FromArgb(36, 36, 36)
        };

        static Color ActiveRowBorder(ConversionStatus status) => status switch
        {
            ConversionStatus.Pending => Color.FromArgb(70, 70, 100),
            ConversionStatus.InProgress => Color.FromArgb(0, 120, 212),
            ConversionStatus.Success => Color.FromArgb(50, 160, 80),
            ConversionStatus.Failed => Color.FromArgb(200, 60, 60),
            _ => Color.FromArgb(60, 60, 60)
        };

        static Color ActiveStatusColor(ConversionStatus status) => status switch
        {
            ConversionStatus.Pending => Color.FromArgb(180, 180, 100),
            ConversionStatus.InProgress => Color.FromArgb(80, 160, 240),
            ConversionStatus.Success => Color.FromArgb(100, 220, 100),
            ConversionStatus.Failed => Color.FromArgb(255, 90, 70),
            _ => Color.Gray
        };

        static void DrawCompletedHistoryRows(Graphics g, float contentX, int rowW, float logH, HistoryBlock block, float s)
        {
            for (int i = 0; i < CompletedItems.Count; i++)
                DrawCompletedHistoryRow(g, contentX, rowW, logH, block, CompletedItems[i], i, s);
        }

        static void DrawCompletedHistoryRow(Graphics g, float contentX, int rowW, float logH, HistoryBlock block,
            HistoryItem item, int index, float s)
        {
            int currentY = block.RowTop(index);
            int currentH = block.RowHeight(index);
            if (currentY + currentH < _contentScrollY || currentY > _contentScrollY + logH)
                return;

            using var path = UIHelper.GetRoundedRectPath(new RectangleF(contentX * s, currentY * s, rowW * s, currentH * s), 6 * s);
            using var rowBg = new SolidBrush(Color.FromArgb(36, 36, 36));
            using var borderPen = new Pen(Color.FromArgb(48, 48, 48));
            g.FillPath(rowBg, path);
            g.DrawPath(borderPen, path);

            float timeW = 120;
            if (_bodyFont != null)
            {
                using var timeBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(item.Time, _bodyFont, timeBrush, (contentX + 12) * s, (currentY + 13) * s);
                timeW = g.MeasureString(item.Time, _bodyFont).Width / s;
            }

            float tagX = contentX + 12 + timeW + 16;
            float tagW = DrawCommandTag(g, item, tagX, currentY + 11);
            string statusText = GetText(item.StatusKey);
            float statusW = _tagFont != null ? g.MeasureString(statusText, _tagFont).Width / s : 50f;
            float statusX = contentX + rowW - 16 - statusW;

            if (_bodyFont != null)
            {
                using var countBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                float fileCountX = tagX + tagW + 16;
                string displayText = item.FileCountText;
                float maxW = statusX - 16 - fileCountX;
                if (maxW > 20)
                    displayText = UIHelper.TruncateFileName(g, displayText, _bodyFont, maxW, s);
                g.DrawString(displayText, _bodyFont, countBrush, fileCountX * s, (currentY + 13) * s);
            }

            if (_tagFont != null)
            {
                Color statusColor = item.IsSuccess ? Color.FromArgb(100, 220, 100) : Color.FromArgb(255, 90, 70);
                using var statusBrush = new SolidBrush(statusColor);
                g.DrawString(statusText, _tagFont, statusBrush, statusX * s, (currentY + 13) * s);
            }

            if (index == _expandedHistoryIndex)
                DrawExpandedHistoryDetails(g, contentX, rowW, currentY, item, index, s);
        }

        static void DrawExpandedHistoryDetails(Graphics g, float contentX, int rowW, int currentY,
            HistoryItem item, int index, float s)
        {
            using var cardDivPen = new Pen(Color.FromArgb(56, 56, 56));
            g.DrawLine(cardDivPen, (contentX + 12) * s, (currentY + DashboardLayout.DetailDividerY) * s,
                (contentX + rowW - 12) * s, (currentY + DashboardLayout.DetailDividerY) * s);
            if (_subFont == null)
                return;

            int inputsY = DashboardLayout.DetailFieldY(0);
            int outputsY = DashboardLayout.DetailFieldY(1);
            int timeY = DashboardLayout.DetailFieldY(2);
            int resultY = DashboardLayout.DetailFieldY(3);
            using var labelBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
            using var valBrush = new SolidBrush(Color.FromArgb(220, 220, 220));

            string resultKey = item.IsSuccess ? "history_detail_elapsed" : "history_detail_error";
            float w1 = g.MeasureString(GetText("history_detail_inputs") + ":", _subFont).Width / s;
            float w2 = g.MeasureString(GetText("history_detail_outputs") + ":", _subFont).Width / s;
            float w3 = g.MeasureString(GetText("history_detail_time") + ":", _subFont).Width / s;
            float w4 = g.MeasureString(GetText(resultKey) + ":", _subFont).Width / s;
            float maxLabelW = Math.Max(w1, Math.Max(w2, Math.Max(w3, w4)));
            float valX = contentX + 12 + maxLabelW + 16;
            float maxValW = contentX + rowW - 12 - valX;
            var entry = item.Entry;

            g.DrawString(GetText("history_detail_inputs") + ":", _subFont, labelBrush,
                (contentX + 12) * s, (currentY + inputsY) * s);
            string inputsText = string.IsNullOrEmpty(entry.InputPaths) ? "N/A" : entry.InputPaths.Replace(";", ", ");
            DrawScrollableHistoryDetail(g, inputsText, index, 0, currentY, inputsY, valX, maxValW, valBrush, s);

            g.DrawString(GetText("history_detail_outputs") + ":", _subFont, labelBrush,
                (contentX + 12) * s, (currentY + outputsY) * s);
            string outputsText = string.IsNullOrEmpty(entry.OutputPath) ? "N/A" : entry.OutputPath;
            DrawScrollableHistoryDetail(g, outputsText, index, 1, currentY, outputsY, valX, maxValW, valBrush, s);

            g.DrawString(GetText("history_detail_time") + ":", _subFont, labelBrush,
                (contentX + 12) * s, (currentY + timeY) * s);
            string endTime = string.IsNullOrEmpty(entry.EndTime) ? entry.Time : entry.EndTime;
            string timeText = UIHelper.TruncateText(g, $"{entry.Time}  →  {endTime}", _subFont, maxValW, s);
            g.DrawString(timeText, _subFont, valBrush, valX * s, (currentY + timeY) * s);

            g.DrawString(GetText(resultKey) + ":", _subFont, labelBrush,
                (contentX + 12) * s, (currentY + resultY) * s);
            if (item.IsSuccess)
            {
                string elapsedText = entry.ElapsedMs >= 0 ? $"{(entry.ElapsedMs / 1000.0):F2} s ({entry.ElapsedMs} ms)" : "N/A";
                elapsedText = UIHelper.TruncateText(g, elapsedText, _subFont, maxValW, s);
                g.DrawString(elapsedText, _subFont, valBrush, valX * s, (currentY + resultY) * s);
                return;
            }

            string errorText;
            if (item.IsCanceled)
                errorText = GetText("error_user_aborted");
            else
                errorText = string.IsNullOrEmpty(entry.ErrorMessage) ? "N/A" : entry.ErrorMessage;
            DrawScrollableHistoryDetail(g, errorText, index, 2, currentY, resultY, valX, maxValW, valBrush, s);
        }

        static void DrawScrollableHistoryDetail(Graphics g, string text, int rowIndex, int fieldIndex,
            int currentY, int fieldY, float valX, float maxValW, Brush brush, float s)
        {
            DetailScrollOffsets.TryGetValue((rowIndex, fieldIndex), out float scrollOffset);
            var state = g.Save();
            g.IntersectClip(new RectangleF(valX * s, (currentY + fieldY) * s, maxValW * s, 20 * s));
            g.DrawString(text, _subFont!, brush, (valX - scrollOffset) * s, (currentY + fieldY) * s);
            g.Restore(state);

            float textW = g.MeasureString(text, _subFont!).Width / s;
            if (textW <= maxValW)
                return;

            float scrollbarY = currentY + DashboardLayout.DetailScrollbarY(fieldIndex);
            float thumbW = Math.Max(15f, (maxValW / textW) * maxValW);
            float thumbX = Math.Min(valX + (scrollOffset / textW) * maxValW, valX + maxValW - thumbW);
            UIHelper.DrawHorizontalScrollbar(g, valX, scrollbarY, maxValW, thumbX, thumbW, s);
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
                Color rowBorder = ParkedRowBorder(info);

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
                    Color ttlColor = ParkedTtlColor(info, needsAttention);
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
                Color bg = ParkedActionBackground(isHovered, isResume, isCancel);
                Color border = ParkedActionBorder(isHovered, isResume, isCancel);
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

        static Color ParkedRowBorder(ClickraStorage.ParkedRetentionInfo info)
        {
            if (info.HasExpired) return Color.FromArgb(200, 60, 60);
            if (info.IsExpiringSoon) return ParkedAlertColor;
            return Color.FromArgb(70, 70, 100);
        }

        static Color ParkedTtlColor(ClickraStorage.ParkedRetentionInfo info, bool needsAttention)
        {
            if (info.HasExpired) return Color.FromArgb(255, 90, 70);
            if (needsAttention) return ParkedAlertColor;
            return Color.FromArgb(150, 150, 160);
        }

        static Color ParkedActionBackground(bool isHovered, bool isResume, bool isCancel)
        {
            if (!isHovered) return Color.FromArgb(46, 46, 54);
            if (isResume) return Color.FromArgb(0, 100, 180);
            if (isCancel) return Color.FromArgb(120, 50, 50);
            return Color.FromArgb(70, 70, 70);
        }

        static Color ParkedActionBorder(bool isHovered, bool isResume, bool isCancel)
        {
            if (!isHovered) return Color.FromArgb(70, 70, 80);
            if (isResume) return Color.FromArgb(40, 140, 230);
            if (isCancel) return Color.FromArgb(180, 70, 70);
            return Color.FromArgb(110, 110, 110);
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

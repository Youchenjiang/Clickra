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
        private const string LabelFilesKey = "label_files";

        /// <summary>待繼續任務即將被清理時使用的警示色（與 Fluent 的琥珀色一致）。</summary>
        private static readonly Color ParkedAlertColor = Color.FromArgb(255, 160, 40);

        private sealed class HistoryDetailRenderContext
        {
            public required Graphics Graphics { get; init; }
            public required Brush LabelBrush { get; init; }
            public required Brush ValueBrush { get; init; }
            public int HistoryIndex { get; init; }
            public int CurrentY { get; init; }
            public float ContentX { get; init; }
            public float ValueX { get; init; }
            public float MaxValueWidth { get; init; }
            public float Scale { get; init; }
        }

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
            float clearX = logW - 130;
            using (var btnBgBrush = new SolidBrush(btnBg))
            using (var btnBorderPen = new Pen(btnBorder))
            using (var path = UIHelper.GetRoundedRectPath(new RectangleF(clearX * s, 38 * s, 90 * s, 28 * s), 4 * s))
            {
                g.FillPath(btnBgBrush, path);
                g.DrawPath(btnBorderPen, path);
            }
            if (_subFont != null)
            {
                Color btnText = isClearHovered ? Color.White : Color.FromArgb(200, 200, 200);
                using var btnTextBrush = new SolidBrush(btnText);
                var size = g.MeasureString(GetText("history_clear"), _subFont);
                g.DrawString(GetText("history_clear"), _subFont, btnTextBrush, (clearX + (90 - size.Width / s) / 2) * s, (38 + (28 - size.Height / s) / 2) * s);
            }

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, contentX * s, 75 * s, (logW - 40) * s, 75 * s);
            }

            // 取得目前進行中的任務佇列（每個任務一列；並行任務各自獨立，不會互搶）
            var activeTasks = ClickraStorage.GetActiveTasks();
            int rowW = (int)logW - (int)contentX - 40;
            DrawActiveHistoryRows(g, contentX, rowW, s, activeTasks);

            DrawParkedQueue(g, contentX, rowW, 90 + activeTasks.Count * HistoryRowStride);

            // ——— 顯示持久化歷史紀錄———
            if (_historyEntries == null || _historyEntries.Count == 0)
            {
                if (activeTasks.Count == 0 && _parkedEntries.Count == 0 && _tabFont != null)
                {
                    using var textBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
                    g.DrawString(GetText("history_empty"), _tabFont, textBrush, contentX * s, 100 * s);
                }
                return;
            }

            // 起點與命中測試／hover／捲動高度共用 GetHistoryListStartY()，不是各自累加出來的。
            DrawPersistedHistoryRows(g, logH, contentX, rowW, s);
        }


        private static void DrawActiveHistoryRows(Graphics g, float contentX, int rowW, float s, List<ClickraStorage.HistoryEntry> activeTasks)
        {
            const int rowH = 44;
            for (int i = 0; i < activeTasks.Count; i++)
            {
                int rowY = 90 + i * HistoryRowStride;
                DrawActiveHistoryRow(g, contentX, rowW, rowH, rowY, s, activeTasks[i]);
            }
        }

        private static void DrawActiveHistoryRow(
            Graphics g,
            float contentX,
            int rowW,
            int rowH,
            int rowY,
            float s,
            ClickraStorage.HistoryEntry task)
        {
            string[] activeFiles = SplitHistoryInputPaths(task.InputPaths);
            ConversionStatus fileStatus = task.Status;
            Color activeBgColor = GetActiveHistoryBackgroundColor(fileStatus);
            Color activeBorderColor = GetActiveHistoryBorderColor(fileStatus);

            using (var path = UIHelper.GetRoundedRectPath(
                       new RectangleF(contentX * s, rowY * s, rowW * s, rowH * s), 6 * s))
            using (var rowBg = new SolidBrush(activeBgColor))
            {
                g.FillPath(rowBg, path);
                using var borderPen = new Pen(activeBorderColor);
                g.DrawPath(borderPen, path);
            }

            float timeW = 120;
            if (_bodyFont != null)
            {
                using var timeBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(task.Time, _bodyFont, timeBrush, (contentX + 12) * s, (rowY + 13) * s);
                timeW = g.MeasureString(task.Time, _bodyFont).Width / s;
            }

            float tagX = contentX + 12 + timeW + 16;
            float tagW = DrawCommandTag(g, task.Command, tagX, rowY + 11);
            string statusText = GetActiveHistoryStatusText(fileStatus);
            Color statusColor = GetActiveHistoryStatusColor(fileStatus);
            float activeStatusW = _tagFont != null ? g.MeasureString(statusText, _tagFont).Width / s : 50f;
            float activeStatusX = contentX + rowW - 16 - activeStatusW;

            float fileCountX = tagX + tagW + 16;
            string displayText = FormatFileCountText(activeFiles, task.FileCount);
            DrawHistoryRowText(g, displayText, fileCountX, activeStatusX, rowY, s, suppressWhenNarrow: false);

            if (_tagFont != null)
            {
                using var statusBrush = new SolidBrush(statusColor);
                g.DrawString(statusText, _tagFont, statusBrush, activeStatusX * s, (rowY + 13) * s);
            }
        }

        private static string[] SplitHistoryInputPaths(string inputPaths) =>
            !string.IsNullOrEmpty(inputPaths)
                ? inputPaths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                : Array.Empty<string>();

        private static Color GetActiveHistoryBackgroundColor(ConversionStatus status) => status switch
        {
            ConversionStatus.Pending => Color.FromArgb(38, 38, 48),
            ConversionStatus.InProgress => Color.FromArgb(30, 42, 55),
            ConversionStatus.Success => Color.FromArgb(30, 44, 34),
            ConversionStatus.Failed => Color.FromArgb(50, 32, 32),
            _ => Color.FromArgb(36, 36, 36)
        };

        private static Color GetActiveHistoryBorderColor(ConversionStatus status) => status switch
        {
            ConversionStatus.Pending => Color.FromArgb(70, 70, 100),
            ConversionStatus.InProgress => Color.FromArgb(0, 120, 212),
            ConversionStatus.Success => Color.FromArgb(50, 160, 80),
            ConversionStatus.Failed => Color.FromArgb(200, 60, 60),
            _ => Color.FromArgb(60, 60, 60)
        };

        private static string GetActiveHistoryStatusText(ConversionStatus status) => status switch
        {
            ConversionStatus.Pending => GetText("status_pending"),
            ConversionStatus.InProgress => GetText("status_converting"),
            ConversionStatus.Success => GetText("status_success"),
            ConversionStatus.Failed => GetText("status_failed"),
            _ => ""
        };

        private static Color GetActiveHistoryStatusColor(ConversionStatus status) => status switch
        {
            ConversionStatus.Pending => Color.FromArgb(180, 180, 100),
            ConversionStatus.InProgress => Color.FromArgb(80, 160, 240),
            ConversionStatus.Success => Color.FromArgb(100, 220, 100),
            ConversionStatus.Failed => Color.FromArgb(255, 90, 70),
            _ => Color.Gray
        };
        private static void DrawPersistedHistoryRows(Graphics g, float logH, float contentX, int rowW, float s)
        {
            int currentY = GetHistoryListStartY();
            for (int i = 0; i < _historyEntries.Count; i++)
            {
                currentY = DrawPersistedHistoryRow(g, i, currentY, logH, contentX, rowW, s);
            }
        }

        private static int DrawPersistedHistoryRow(
            Graphics g,
            int index,
            int currentY,
            float logH,
            float contentX,
            int rowW,
            float s)
        {
            var entry = _historyEntries[index];
            bool isExpanded = index == _expandedHistoryIndex;
            int currentH = isExpanded ? 160 : 44;
            int nextY = currentY + currentH + 8;

            if (currentY + currentH < _contentScrollY || currentY > _contentScrollY + logH)
            {
                return nextY;
            }

            using var path = UIHelper.GetRoundedRectPath(
                new RectangleF(contentX * s, currentY * s, rowW * s, currentH * s), 6 * s);
            using var rowBg = new SolidBrush(Color.FromArgb(36, 36, 36));
            g.FillPath(rowBg, path);

            using var borderPen = new Pen(Color.FromArgb(48, 48, 48));
            g.DrawPath(borderPen, path);

            DrawPersistedHistorySummary(g, entry, currentY, contentX, rowW, s);
            if (isExpanded)
            {
                DrawExpandedHistoryDetails(g, entry, index, currentY, contentX, rowW, s);
            }

            return nextY;
        }

        private static void DrawPersistedHistorySummary(
            Graphics g,
            ClickraStorage.HistoryEntry entry,
            int currentY,
            float contentX,
            int rowW,
            float s)
        {
            float timeW = 120;
            if (_bodyFont != null)
            {
                using var timeBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(entry.Time, _bodyFont, timeBrush, (contentX + 12) * s, (currentY + 13) * s);
                timeW = g.MeasureString(entry.Time, _bodyFont).Width / s;
            }

            float tagX = contentX + 12 + timeW + 16;
            float tagW = DrawCommandTag(g, entry.Command, tagX, currentY + 11);
            string statusText = GetPersistedHistoryStatusText(entry);
            Color statusColor = entry.IsSuccess ? Color.FromArgb(100, 220, 100) : Color.FromArgb(255, 90, 70);
            float statusW = _tagFont != null ? g.MeasureString(statusText, _tagFont).Width / s : 50f;
            float statusX = contentX + rowW - 16 - statusW;

            float fileCountX = tagX + tagW + 16;
            string[] paths = SplitHistoryInputPaths(entry.InputPaths);
            string displayText = FormatFileCountText(paths, entry.FileCount);
            DrawHistoryRowText(g, displayText, fileCountX, statusX, currentY, s, suppressWhenNarrow: false);

            if (_tagFont != null)
            {
                using var statusBrush = new SolidBrush(statusColor);
                g.DrawString(statusText, _tagFont, statusBrush, statusX * s, (currentY + 13) * s);
            }
        }

        private static string GetPersistedHistoryStatusText(ClickraStorage.HistoryEntry entry)
        {
            if (entry.IsSuccess) return GetText("status_success");
            if (entry.ErrorMessage?.Equals("User Aborted", StringComparison.OrdinalIgnoreCase) == true)
                return GetText("error_user_aborted");
            return GetText("status_error");
        }

        private static void DrawExpandedHistoryDetails(
            Graphics g,
            ClickraStorage.HistoryEntry entry,
            int index,
            int currentY,
            float contentX,
            int rowW,
            float s)
        {
            using (var cardDivPen = new Pen(Color.FromArgb(56, 56, 56)))
            {
                g.DrawLine(
                    cardDivPen,
                    (contentX + 12) * s,
                    (currentY + 44) * s,
                    (contentX + rowW - 12) * s,
                    (currentY + 44) * s);
            }

            if (_subFont == null) return;

            using var labelBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
            using var valBrush = new SolidBrush(Color.FromArgb(220, 220, 220));

            string resultLabelKey = entry.IsSuccess ? "history_detail_elapsed" : "history_detail_error";
            float w1 = g.MeasureString(GetText("history_detail_inputs") + ":", _subFont).Width / s;
            float w2 = g.MeasureString(GetText("history_detail_outputs") + ":", _subFont).Width / s;
            float w3 = g.MeasureString(GetText("history_detail_time") + ":", _subFont).Width / s;
            float w4 = g.MeasureString(GetText(resultLabelKey) + ":", _subFont).Width / s;
            float maxLabelW = Math.Max(w1, Math.Max(w2, Math.Max(w3, w4)));
            float valX = contentX + 12 + maxLabelW + 16;
            float maxValW = contentX + rowW - 12 - valX;
            var detailContext = new HistoryDetailRenderContext
            {
                Graphics = g,
                LabelBrush = labelBrush,
                ValueBrush = valBrush,
                HistoryIndex = index,
                CurrentY = currentY,
                ContentX = contentX,
                ValueX = valX,
                MaxValueWidth = maxValW,
                Scale = s
            };

            g.DrawString(GetText("history_detail_inputs") + ":", _subFont, labelBrush,
                (contentX + 12) * s, (currentY + 54) * s);
            string inputsText = string.IsNullOrEmpty(entry.InputPaths) ? "N/A" : entry.InputPaths.Replace(";", ", ");
            DrawScrollableHistoryDetail(detailContext, inputsText, 0, currentY + 54, currentY + 71);

            g.DrawString(GetText("history_detail_outputs") + ":", _subFont, labelBrush,
                (contentX + 12) * s, (currentY + 80) * s);
            string outputsText = string.IsNullOrEmpty(entry.OutputPath) ? "N/A" : entry.OutputPath;
            DrawScrollableHistoryDetail(detailContext, outputsText, 1, currentY + 80, currentY + 97);

            g.DrawString(GetText("history_detail_time") + ":", _subFont, labelBrush,
                (contentX + 12) * s, (currentY + 106) * s);
            string endTime = string.IsNullOrEmpty(entry.EndTime) ? entry.Time : entry.EndTime;
            string timeText = $"{entry.Time}  →  {endTime}";
            timeText = UIHelper.TruncateText(g, timeText, _subFont, maxValW, s);
            g.DrawString(timeText, _subFont, valBrush, valX * s, (currentY + 106) * s);

            DrawExpandedHistoryResult(detailContext, entry);
        }

        private static void DrawExpandedHistoryResult(
            HistoryDetailRenderContext context,
            ClickraStorage.HistoryEntry entry)
        {
            Graphics g = context.Graphics;
            int currentY = context.CurrentY;
            float contentX = context.ContentX;
            float valX = context.ValueX;
            float maxValW = context.MaxValueWidth;
            Brush labelBrush = context.LabelBrush;
            Brush valBrush = context.ValueBrush;
            float s = context.Scale;

            if (entry.IsSuccess)
            {
                g.DrawString(GetText("history_detail_elapsed") + ":", _subFont!, labelBrush,
                    (contentX + 12) * s, (currentY + 132) * s);
                string elapsedText = entry.ElapsedMs >= 0
                    ? $"{(entry.ElapsedMs / 1000.0):F2} s ({entry.ElapsedMs} ms)"
                    : "N/A";
                elapsedText = UIHelper.TruncateText(g, elapsedText, _subFont!, maxValW, s);
                g.DrawString(elapsedText, _subFont!, valBrush, valX * s, (currentY + 132) * s);
                return;
            }

            g.DrawString(GetText("history_detail_error") + ":", _subFont!, labelBrush,
                (contentX + 12) * s, (currentY + 132) * s);
            string errorText = string.IsNullOrEmpty(entry.ErrorMessage) ? "N/A" : entry.ErrorMessage;
            if (errorText.Equals("User Aborted", StringComparison.OrdinalIgnoreCase))
            {
                errorText = GetText("error_user_aborted");
            }

            DrawScrollableHistoryDetail(context, errorText, 2, currentY + 132, currentY + 149);
        }

        private static void DrawScrollableHistoryDetail(
            HistoryDetailRenderContext context,
            string text,
            int detailIndex,
            float valueY,
            float scrollbarY)
        {
            Graphics g = context.Graphics;
            float valX = context.ValueX;
            float maxValW = context.MaxValueWidth;
            float s = context.Scale;

            DetailScrollOffsets.TryGetValue((context.HistoryIndex, detailIndex), out float scrollOffset);
            var state = g.Save();
            g.IntersectClip(new RectangleF(valX * s, valueY * s, maxValW * s, 20 * s));
            g.DrawString(text, _subFont!, context.ValueBrush, (valX - scrollOffset) * s, valueY * s);
            g.Restore(state);

            float textW = g.MeasureString(text, _subFont!).Width / s;
            if (textW <= maxValW) return;

            float thumbW = Math.Max(15f, (maxValW / textW) * maxValW);
            float thumbX = valX + (scrollOffset / textW) * maxValW;
            if (thumbX + thumbW > valX + maxValW)
            {
                thumbX = valX + maxValW - thumbW;
            }

            UIHelper.DrawHorizontalScrollbar(g, valX, scrollbarY, maxValW, thumbX, thumbW, s);
        }
        /// <summary>Formats the file-count display text for a history row.</summary>
        private static string FormatFileCountText(string[] activeFiles, int fileCount)
        {
            if (activeFiles.Length == 0)
                return $"{fileCount} {GetText(LabelFilesKey)}";
            if (activeFiles.Length == 1)
                return Path.GetFileName(activeFiles[0]);
            return $"{Path.GetFileName(activeFiles[0])} + {activeFiles.Length - 1} {GetText(LabelFilesKey)}";
        }

        /// <summary>畫出待繼續（已暫存）任務，每列帶自己的剩餘保留期限，讓保留期限是逐項可見的，
        /// 而不只是設定頁上那句話。回傳持久化歷史紀錄的起始 Y。</summary>
        private static Color GetParkedRowBorderColor(ClickraStorage.ParkedRetentionInfo info)
        {
            if (info.HasExpired) return Color.FromArgb(200, 60, 60);
            if (info.IsExpiringSoon) return ParkedAlertColor;
            return Color.FromArgb(70, 70, 100);
        }

        private static Color GetParkedRetentionTextColor(ClickraStorage.ParkedRetentionInfo info)
        {
            if (info.HasExpired) return Color.FromArgb(255, 90, 70);
            if (info.IsExpiringSoon) return ParkedAlertColor;
            return Color.FromArgb(150, 150, 160);
        }

        static void DrawParkedQueue(Graphics g, float contentX, int rowW, int startY)
        {
            if (_parkedEntries.Count == 0) return;

            float s = _dpiScale;
            var infos = new ClickraStorage.ParkedRetentionInfo[_parkedEntries.Count];
            int expiringSoonCount = 0;
            for (int i = 0; i < _parkedEntries.Count; i++)
            {
                infos[i] = ClickraStorage.GetParkedRetentionInfo(_parkedEntries[i].Id);
                if (infos[i].IsExpiringSoon || infos[i].HasExpired) expiringSoonCount++;
            }

            DrawParkedQueueHeader(g, contentX, rowW, startY, expiringSoonCount, s);

            int currentY = startY + ParkedBlockHeaderHeight;
            for (int i = 0; i < _parkedEntries.Count; i++)
            {
                DrawParkedQueueRow(g, _parkedEntries[i], infos[i], contentX, rowW, currentY, s);
                currentY += HistoryRowStride;
            }
        }

        private static void DrawParkedQueueHeader(
            Graphics g,
            float contentX,
            int rowW,
            int startY,
            int expiringSoonCount,
            float s)
        {
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
        }

        private static void DrawParkedQueueRow(
            Graphics g,
            ClickraStorage.HistoryEntry task,
            ClickraStorage.ParkedRetentionInfo info,
            float contentX,
            int rowW,
            int currentY,
            float s)
        {
            bool needsAttention = info.IsExpiringSoon || info.HasExpired;

            Color rowBg = needsAttention ? Color.FromArgb(48, 40, 30) : Color.FromArgb(34, 34, 40);
            Color rowBorder = GetParkedRowBorderColor(info);

            using (var path = UIHelper.GetRoundedRectPath(new RectangleF(contentX * s, currentY * s, rowW * s, 44 * s), 6 * s))
            using (var rowBgBrush = new SolidBrush(rowBg))
            {
                g.FillPath(rowBgBrush, path);
                using var borderPen = new Pen(rowBorder);
                g.DrawPath(borderPen, path);
            }

            float timeW = 120;
            if (_bodyFont != null)
            {
                using var timeBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(task.Time, _bodyFont, timeBrush, (contentX + 12) * s, (currentY + 13) * s);
                timeW = g.MeasureString(task.Time, _bodyFont).Width / s;
            }

            float tagX = contentX + 12 + timeW + 16;
            float tagW = DrawCommandTag(g, task.Command, tagX, currentY + 11);

            float nextContentX = DrawParkedRetentionBadge(g, tagX + tagW + 12, currentY, s, needsAttention);

            string ttlText = ClickraStorage.DescribeParkedRetention(info);
            float ttlW = _tagFont != null ? g.MeasureString(ttlText, _tagFont).Width / s : 60f;
            float ttlX = contentX + rowW - 16 - ttlW;

            string[] parkedFiles = SplitHistoryInputPaths(task.InputPaths);
            DrawParkedTaskDetails(g, task, parkedFiles, nextContentX, ttlX, currentY, s);

            if (_tagFont != null)
            {
                Color ttlColor = GetParkedRetentionTextColor(info);
                using var ttlBrush = new SolidBrush(ttlColor);
                g.DrawString(ttlText, _tagFont, ttlBrush, ttlX * s, (currentY + 13) * s);
            }
        }

        private static float DrawParkedRetentionBadge(
            Graphics g,
            float nextContentX,
            int currentY,
            float s,
            bool needsAttention)
        {
            if (!needsAttention) return nextContentX;

            string badgeText = GetText("task_parked_badge_expiring");
            float badgeTextW = _tagFont != null ? g.MeasureString(badgeText, _tagFont).Width / s : 48f;
            float badgeW = badgeTextW + 14f;
            const float badgeH = 22f;

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

            return nextContentX + badgeW + 12;
        }

        private static void DrawParkedTaskDetails(
            Graphics g,
            ClickraStorage.HistoryEntry task,
            string[] parkedFiles,
            float fileX,
            float ttlX,
            int currentY,
            float s)
        {
            if (_bodyFont == null) return;

            string firstFile = parkedFiles.Length > 0
                ? Path.GetFileName(parkedFiles[0])
                : $"{task.FileCount} {GetText(LabelFilesKey)}";
            string stoppedOn = task.FileCount > 1
                ? string.Format(GetText("fluent_task_file_index"), Math.Clamp(task.CurrentIndex + 1, 1, task.FileCount), task.FileCount)
                : "";
            string reason = !string.IsNullOrWhiteSpace(task.ErrorMessage) ? task.ErrorMessage : "";
            string displayText = string.Join(" · ", new[] { firstFile, stoppedOn, reason }.Where(part => !string.IsNullOrWhiteSpace(part)));

            DrawHistoryRowText(g, displayText, fileX, ttlX, currentY, s, suppressWhenNarrow: true);
        }

        private static void DrawHistoryRowText(
            Graphics g,
            string displayText,
            float textX,
            float rightTextX,
            int rowY,
            float s,
            bool suppressWhenNarrow)
        {
            if (_bodyFont == null) return;

            float maxW = rightTextX - 16 - textX;
            if (suppressWhenNarrow && maxW <= 20) return;
            if (maxW > 20)
            {
                displayText = UIHelper.TruncateFileName(g, displayText, _bodyFont, maxW, s);
            }

            using var textBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
            g.DrawString(displayText, _bodyFont, textBrush, textX * s, (rowY + 13) * s);
        }

        /// <summary>Draws a colored command tag at the given position and returns its width.</summary>
        static float DrawCommandTag(Graphics g, string command, float x, float y)
        {
            float s = _dpiScale;
            Color tagBg = Color.FromArgb(100, 100, 100);
            string text = command;
            if (ConvertCommandByKey.TryGetValue(command, out var def))
            {
                tagBg = def.TagColor;
                text = GetText(def.TextKey);
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

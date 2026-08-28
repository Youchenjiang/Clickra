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

            int startY = 90;
            int rowW = (int)logW - (int)contentX - 40;
            int rowH = 44;

            // ——— 顯示進行中任務佇列（置頂）———
            foreach (var task in activeTasks)
            {
                int rowY = startY;
                var activeFiles = !string.IsNullOrEmpty(task.InputPaths)
                    ? task.InputPaths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    : Array.Empty<string>();

                ConversionStatus fileStatus = task.Status;

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
                        g.DrawString(task.Time, _bodyFont, timeBrush, (contentX + 12) * s, (rowY + 13) * s);
                        timeW = g.MeasureString(task.Time, _bodyFont).Width / s;
                    }

                    // Command Tag
                    float tagX = contentX + 12 + timeW + 16;
                    float tagW = DrawCommandTag(g, task.Command, tagX, rowY + 11);

                    // Status Label (量測實際寬度，靠右對齊)
                    string statusText = fileStatus switch
                    {
                        ConversionStatus.Pending    => GetText("status_pending"),
                        ConversionStatus.InProgress => GetText("status_converting"),
                        ConversionStatus.Success    => GetText("status_success"),
                        ConversionStatus.Failed     => GetText("status_failed"),
                        _                           => ""
                    };
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
                        
                        string displayText = FormatFileCountText(activeFiles, task.FileCount);

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


                    startY += 52;
            }

            // ——— 顯示持久化歷史紀錄———
            if (_historyEntries == null || _historyEntries.Count == 0)
            {
                if (activeTasks.Count == 0 && _tabFont != null)
                {
                    using var textBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
                    g.DrawString(GetText("history_empty"), _tabFont, textBrush, contentX * s, 100 * s);
                }
                return;
            }

            int currentY = startY;
            for (int i = 0; i < _historyEntries.Count; i++)
            {
                var entry = _historyEntries[i];
                bool isExpanded = (i == _expandedHistoryIndex);
                int currentH = isExpanded ? 160 : 44;

                // Optimization: Skip rendering if item is completely outside viewport
                if (currentY + currentH < _contentScrollY || currentY > _contentScrollY + logH)
                {
                    currentY += currentH + 8;
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
                    g.DrawString(entry.Time, _bodyFont, timeBrush, (contentX + 12) * s, (currentY + 13) * s);
                    timeW = g.MeasureString(entry.Time, _bodyFont).Width / s;
                }

                // 指令標籤 (動態相對起點)
                float tagX = contentX + 12 + timeW + 16;
                float tagW = DrawCommandTag(g, entry.Command, tagX, currentY + 11);

                // 狀態標籤與顏色計算
                Color statusColor = entry.IsSuccess ? Color.FromArgb(100, 220, 100) : Color.FromArgb(255, 90, 70);
                string statusText = entry.IsSuccess 
                    ? GetText("status_success") 
                    : (entry.ErrorMessage?.Equals("User Aborted", StringComparison.OrdinalIgnoreCase) == true 
                        ? GetText("error_user_aborted") 
                        : GetText("status_error"));

                float statusW = _tagFont != null ? g.MeasureString(statusText, _tagFont).Width / s : 50f;
                float statusX = contentX + rowW - 16 - statusW;

                // 檔案名稱：tag 之後到 status 之前的所有空間
                if (_bodyFont != null)
                {
                    using var countBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                    float fileCountX = tagX + tagW + 16;
                    string displayText = $"{entry.FileCount} {GetText(LabelFilesKey)}";
                    if (!string.IsNullOrEmpty(entry.InputPaths))
                    {
                        var paths = entry.InputPaths.Split(';', StringSplitOptions.RemoveEmptyEntries);
                        if (paths.Length > 1)
                        {
                            displayText = $"{Path.GetFileName(paths[0])} + {paths.Length - 1} {GetText(LabelFilesKey)}";
                        }
                        else if (paths.Length == 1)
                        {
                            displayText = Path.GetFileName(paths[0]);
                        }
                    }
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
                    using (var cardDivPen = new Pen(Color.FromArgb(56, 56, 56)))
                    {
                        g.DrawLine(cardDivPen, (contentX + 12) * s, (currentY + 44) * s, (contentX + rowW - 12) * s, (currentY + 44) * s);
                    }

                    if (_subFont != null)
                    {
                        using var labelBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                        using var valBrush = new SolidBrush(Color.FromArgb(220, 220, 220));

                        // Measure label widths to draw values relatively
                        float w1 = g.MeasureString(GetText("history_detail_inputs") + ":", _subFont).Width / s;
                        float w2 = g.MeasureString(GetText("history_detail_outputs") + ":", _subFont).Width / s;
                        float w3 = g.MeasureString(GetText("history_detail_time") + ":", _subFont).Width / s;
                        float w4 = g.MeasureString(GetText(entry.IsSuccess ? "history_detail_elapsed" : "history_detail_error") + ":", _subFont).Width / s;
                        float maxLabelW = Math.Max(w1, Math.Max(w2, Math.Max(w3, w4)));
                        float valX = contentX + 12 + maxLabelW + 16;
                        float maxValW = contentX + rowW - 12 - valX;

                        // 1. Files / Input Paths
                        g.DrawString(GetText("history_detail_inputs") + ":", _subFont, labelBrush, (contentX + 12) * s, (currentY + 54) * s);
                        string inputsText = entry.InputPaths;
                        if (string.IsNullOrEmpty(inputsText)) inputsText = "N/A";
                        else inputsText = inputsText.Replace(";", ", ");
                        
                        float scrollOffset0 = 0;
                        DetailScrollOffsets.TryGetValue((i, 0), out scrollOffset0);
                        var state0 = g.Save();
                        g.IntersectClip(new RectangleF(valX * s, (currentY + 54) * s, maxValW * s, 20 * s));
                        g.DrawString(inputsText, _subFont, valBrush, (valX - scrollOffset0) * s, (currentY + 54) * s);
                        g.Restore(state0);

                        // Draw inputs scrollbar if scrollable
                        float textW0 = g.MeasureString(inputsText, _subFont).Width / s;
                        if (textW0 > maxValW)
                        {
                            float scrollbarY = currentY + 71;
                            float thumbW = Math.Max(15f, (maxValW / textW0) * maxValW);
                            float thumbX = valX + (scrollOffset0 / textW0) * maxValW;
                            if (thumbX + thumbW > valX + maxValW) thumbX = valX + maxValW - thumbW;
                            UIHelper.DrawHorizontalScrollbar(g, valX, scrollbarY, maxValW, thumbX, thumbW, s);
                        }

                        // 2. Output Path
                        g.DrawString(GetText("history_detail_outputs") + ":", _subFont, labelBrush, (contentX + 12) * s, (currentY + 80) * s);
                        string outputsText = entry.OutputPath;
                        if (string.IsNullOrEmpty(outputsText)) outputsText = "N/A";
                        
                        float scrollOffset1 = 0;
                        DetailScrollOffsets.TryGetValue((i, 1), out scrollOffset1);
                        var state1 = g.Save();
                        g.IntersectClip(new RectangleF(valX * s, (currentY + 80) * s, maxValW * s, 20 * s));
                        g.DrawString(outputsText, _subFont, valBrush, (valX - scrollOffset1) * s, (currentY + 80) * s);
                        g.Restore(state1);

                        // Draw outputs scrollbar if scrollable
                        float textW1 = g.MeasureString(outputsText, _subFont).Width / s;
                        if (textW1 > maxValW)
                        {
                            float scrollbarY = currentY + 97;
                            float thumbW = Math.Max(15f, (maxValW / textW1) * maxValW);
                            float thumbX = valX + (scrollOffset1 / textW1) * maxValW;
                            if (thumbX + thumbW > valX + maxValW) thumbX = valX + maxValW - thumbW;
                            UIHelper.DrawHorizontalScrollbar(g, valX, scrollbarY, maxValW, thumbX, thumbW, s);
                        }

                        // 3. Time Details
                        g.DrawString(GetText("history_detail_time") + ":", _subFont, labelBrush, (contentX + 12) * s, (currentY + 106) * s);
                        string timeText = $"{entry.Time}  →  {(string.IsNullOrEmpty(entry.EndTime) ? entry.Time : entry.EndTime)}";
                        timeText = UIHelper.TruncateText(g, timeText, _subFont, maxValW, s);
                        g.DrawString(timeText, _subFont, valBrush, valX * s, (currentY + 106) * s);

                        // 4. Elapsed Time or Error Message
                        if (entry.IsSuccess)
                        {
                            g.DrawString(GetText("history_detail_elapsed") + ":", _subFont, labelBrush, (contentX + 12) * s, (currentY + 132) * s);
                            string elapsedText = entry.ElapsedMs >= 0 ? $"{(entry.ElapsedMs / 1000.0):F2} s ({entry.ElapsedMs} ms)" : "N/A";
                            elapsedText = UIHelper.TruncateText(g, elapsedText, _subFont, maxValW, s);
                            g.DrawString(elapsedText, _subFont, valBrush, valX * s, (currentY + 132) * s);
                        }
                        else
                        {
                            g.DrawString(GetText("history_detail_error") + ":", _subFont, labelBrush, (contentX + 12) * s, (currentY + 132) * s);
                            string errorText = !string.IsNullOrEmpty(entry.ErrorMessage) ? entry.ErrorMessage : "N/A";
                            if (errorText.Equals("User Aborted", StringComparison.OrdinalIgnoreCase))
                            {
                                errorText = GetText("error_user_aborted");
                            }
                            
                            float scrollOffset2 = 0;
                            DetailScrollOffsets.TryGetValue((i, 2), out scrollOffset2);
                            var state2 = g.Save();
                            g.IntersectClip(new RectangleF(valX * s, (currentY + 132) * s, maxValW * s, 20 * s));
                            g.DrawString(errorText, _subFont, valBrush, (valX - scrollOffset2) * s, (currentY + 132) * s);
                            g.Restore(state2);

                            // Draw error scrollbar if scrollable
                            float textW2 = g.MeasureString(errorText, _subFont).Width / s;
                            if (textW2 > maxValW)
                            {
                                float scrollbarY = currentY + 149;
                                float thumbW = Math.Max(15f, (maxValW / textW2) * maxValW);
                                float thumbX = valX + (scrollOffset2 / textW2) * maxValW;
                                if (thumbX + thumbW > valX + maxValW) thumbX = valX + maxValW - thumbW;
                                UIHelper.DrawHorizontalScrollbar(g, valX, scrollbarY, maxValW, thumbX, thumbW, s);
                            }
                        }
                    }
                }

                currentY += currentH + 8;
            }
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

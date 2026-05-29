using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using Microsoft.Win32;
using Clickra.Core;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        static void Paint(IntPtr hwnd, IntPtr hdc)
        {
            if (_bufferBmp == null || _bufferGraphics == null) return;
            var g = _bufferGraphics;
            g.Clear(Color.FromArgb(32, 32, 32));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float logW = LogicalWidth(hwnd);
            float logH = LogicalHeight(hwnd);
            float s = _dpiScale;

            float sidebarW = GetSidebarWidth(logW);
            float contentX = GetContentX(logW);

            // 1. Draw Sidebar
            using (var sidebarBrush = new SolidBrush(Color.FromArgb(24, 24, 24)))
            {
                g.FillRectangle(sidebarBrush, 0, 0, sidebarW * s, logH * s);
            }
            using (var dividerPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(dividerPen, sidebarW * s, 0, sidebarW * s, logH * s);
            }

            // Draw Brand Title (Centered in sidebar)
            if (_titleFont != null)
            {
                var size = g.MeasureString("Clickra", _titleFont);
                float titleW = size.Width / s;
                float titleX = (sidebarW - titleW) / 2;
                g.DrawString("Clickra", _titleFont, Brushes.White, titleX * s, 30 * s);
            }

            // Brand Divider (Aligned with right side content divider)
            using (var brandDivPen = new Pen(Color.FromArgb(45, 45, 45)))
            {
                g.DrawLine(brandDivPen, 20 * s, 75 * s, (sidebarW - 20) * s, 75 * s);
            }

            // Draw Version Number in bottom-left of sidebar
            if (_subFont != null)
            {
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string verStr = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "Unknown";
                using var verBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
                g.DrawString($"v{verStr}", _subFont, verBrush, 24 * s, (logH - 24) * s);
            }

            // Draw Tabs
            DrawTabButton(g, "\uE80F", GetText("tab_status"), 0, 120, sidebarW);
            DrawTabButton(g, "\uEC7E", GetText("tab_convert"), 1, 168, sidebarW);
            DrawTabButton(g, "\uE81C", GetText("tab_history"), 2, 216, sidebarW);
            DrawTabButton(g, "\uE713", GetText("tab_settings"), 3, 264, sidebarW);
            DrawTabButton(g, "\uE897", GetText("tab_about"), 4, 312, sidebarW);

            // 2. Draw Content Area (with Clip & Translation)
            var state = g.Save();
            float clipW = Math.Max(0f, logW - sidebarW) * s;
            g.SetClip(new RectangleF(sidebarW * s, 0, clipW, logH * s));
            g.TranslateTransform(-_contentScrollX * s, -_contentScrollY * s);

            float virtLogW = Math.Max(760f, logW);
            float virtLogH = Math.Max(460f, logH);

            if (_activeTab == 0)
            {
                DrawOverviewTab(g, virtLogW, virtLogH, contentX);
            }
            else if (_activeTab == 1)
            {
                DrawConvertTab(g, virtLogW, virtLogH, contentX);
            }
            else if (_activeTab == 2)
            {
                DrawHistoryTab(g, virtLogW, virtLogH, contentX);
            }
            else if (_activeTab == 3)
            {
                DrawSettingsTab(g, virtLogW, virtLogH, contentX);
            }
            else if (_activeTab == 4)
            {
                DrawAboutTab(g, virtLogW, virtLogH, contentX);
            }

            g.Restore(state);

            // 3. Draw Viewport Scrollbars (fixed on screen, not translated)
            float contentH = GetContentHeight(hwnd);
            bool showV = logH < contentH;
            bool showH = logW < 760;

            if (showV)
            {
                float trackY = 4;
                float trackH = logH - 8;
                if (showH) trackH = logH - 16;
                float thumbH = Math.Max(20f, (logH / contentH) * trackH);
                float thumbY = trackY + (_contentScrollY / (contentH - logH)) * (trackH - thumbH);
                float trackX = logW - 8;
                float trackW = 5;

                using (var sbTrackBrush = new SolidBrush(Color.FromArgb(20, 20, 20)))
                {
                    g.FillRectangle(sbTrackBrush, trackX * s, trackY * s, trackW * s, trackH * s);
                }
                using (var sbThumbBrush = new SolidBrush(Color.FromArgb(100, 100, 100)))
                using (var thumbPath = GetRoundedRectPath(new RectangleF(trackX * s, thumbY * s, trackW * s, thumbH * s), 2.5f * s))
                {
                    g.FillPath(sbThumbBrush, thumbPath);
                }
            }

            if (showH)
            {
                float trackX = sidebarW + 4;
                float trackW_sb = (logW - sidebarW) - 8;
                if (showV) trackW_sb = (logW - sidebarW) - 16;
                if (trackW_sb > 0)
                {
                    float thumbW = Math.Max(20f, ((logW - sidebarW) / (760f - sidebarW)) * trackW_sb);
                    float thumbX = trackX + (_contentScrollX / (760f - logW)) * (trackW_sb - thumbW);
                    float trackY = logH - 8;
                    float trackH = 5;

                    using (var sbTrackBrush = new SolidBrush(Color.FromArgb(20, 20, 20)))
                    {
                        g.FillRectangle(sbTrackBrush, trackX * s, trackY * s, trackW_sb * s, trackH * s);
                    }
                    using (var sbThumbBrush = new SolidBrush(Color.FromArgb(100, 100, 100)))
                    using (var thumbPath = GetRoundedRectPath(new RectangleF(thumbX * s, trackY * s, thumbW * s, trackH * s), 2.5f * s))
                    {
                        g.FillPath(sbThumbBrush, thumbPath);
                    }
                }
            }

            // Draw double buffer to screen
            using var targetG = Graphics.FromHdc(hdc);
            if (_bufferBmp != null)
            {
                targetG.DrawImage(_bufferBmp, 0, 0, _bufferBmp.Width, _bufferBmp.Height);
            }
        }

        static void DrawTabButton(Graphics g, string icon, string label, int tabIndex, int y, float sidebarW)
        {
            float s = _dpiScale;
            bool isActive = _activeTab == tabIndex;
            bool isHovered = _hoveredElement == tabIndex;

            float scaledY = y * s;
            float scaledH = 40 * s;
            float width = sidebarW - 4;

            if (isActive)
            {
                // Accent left border
                using var accentBrush = new SolidBrush(GetSystemColorizationColor());
                g.FillRectangle(accentBrush, 0, scaledY + 4 * s, 4 * s, 32 * s);

                // Subtle background for active tab
                using var activeBg = new SolidBrush(Color.FromArgb(36, 36, 36));
                g.FillRectangle(activeBg, 4 * s, scaledY, width * s, scaledH);
            }
            else if (isHovered)
            {
                // Hover background
                using var hoverBg = new SolidBrush(Color.FromArgb(30, 30, 30));
                g.FillRectangle(hoverBg, 4 * s, scaledY, width * s, scaledH);
            }

            Color textColor = isActive ? Color.White : (isHovered ? Color.FromArgb(220, 220, 220) : Color.FromArgb(150, 150, 150));
            using var textBrush = new SolidBrush(textColor);

            // Draw Icon (Segoe MDL2 Assets)
            if (_iconFont != null)
            {
                g.DrawString(icon, _iconFont, textBrush, 24 * s, scaledY + 12 * s);
            }

            // Draw Label (Segoe UI)
            if (_tabFont != null)
            {
                g.DrawString(label, _tabFont, textBrush, 52 * s, scaledY + 10 * s);
            }
        }

        static void DrawOverviewTab(Graphics g, float logW, float logH, float contentX)
        {
            float s = _dpiScale;
            // Title
            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_status"), _contentTitleFont, Brushes.White, contentX * s, 30 * s);

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, contentX * s, 75 * s, (logW - 40) * s, 75 * s);
            }

            // Engine status
            if (_sectionFont != null)
                g.DrawString(GetText("overview_engine_status"), _sectionFont, Brushes.White, contentX * s, 95 * s);

            DrawEngineRow(g, GetText("engine_pdf"), true, (int)contentX, 125);
            DrawEngineRow(g, GetText("engine_ppt"), IsOfficeInstalled("PowerPoint"), (int)contentX, 165);
            DrawEngineRow(g, GetText("engine_word"), IsOfficeInstalled("Word"), (int)contentX, 205);

            // Statistics
            if (_sectionFont != null)
                g.DrawString(GetText("overview_stats"), _sectionFont, Brushes.White, contentX * s, 260 * s);

            // Draw Cards
            DrawStatCard(g, GetText("overview_stat_total"), _statTotal.ToString(), Color.FromArgb(200, 200, 200), (int)contentX, 290, 140);
            DrawStatCard(g, GetText("overview_stat_success"), _statSuccess.ToString(), Color.FromArgb(100, 220, 100), (int)contentX + 160, 290, 140);
            DrawStatCard(g, GetText("overview_stat_failed"), _statFailed.ToString(), Color.FromArgb(255, 90, 70), (int)contentX + 320, 290, 140);
            
            if (_subFont != null)
            {
                using var tipBrush = new SolidBrush(Color.FromArgb(100, 100, 100));
                var rect = new RectangleF(contentX * s, 380 * s, (logW - contentX - 40) * s, 45 * s);
                g.DrawString(GetText("overview_tip"), _subFont, tipBrush, rect);
            }
        }

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
            using (var path = GetRoundedRectPath(new RectangleF(clearX * s, 38 * s, 90 * s, 28 * s), 4 * s))
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

            // 取得目前進行中作業（若有）
            var activeEntry = ClickraStorage.GetActiveEntry();

            int startY = 90;
            int rowW = (int)logW - (int)contentX - 40;
            int rowH = 44;

            // ——— 顯示進行中作業（置頂）———
            if (activeEntry.HasValue)
            {
                var ae = activeEntry.Value;
                var activeFiles = !string.IsNullOrEmpty(ae.InputPaths)
                    ? ae.InputPaths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    : Array.Empty<string>();

                int activeCount = activeFiles.Length > 0 ? activeFiles.Length : 1;

                for (int idxActive = 0; idxActive < activeCount; idxActive++)
                {
                    int rowY = startY;

                    // Determine status for this specific file
                    ConversionStatus fileStatus = ae.Status;
                    if (ae.Status == ConversionStatus.InProgress && activeFiles.Length > 1)
                    {
                        if (idxActive < ae.CurrentIndex) fileStatus = ConversionStatus.Success;
                        else if (idxActive == ae.CurrentIndex) fileStatus = ConversionStatus.InProgress;
                        else fileStatus = ConversionStatus.Pending;
                    }
                    else if (ae.Status == ConversionStatus.Pending && activeFiles.Length > 1)
                    {
                        fileStatus = ConversionStatus.Pending;
                    }

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

                    using (var path = GetRoundedRectPath(new RectangleF(contentX * s, rowY * s, rowW * s, rowH * s), 6 * s))
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
                        g.DrawString(ae.Time, _bodyFont, timeBrush, (contentX + 12) * s, (rowY + 13) * s);
                        timeW = g.MeasureString(ae.Time, _bodyFont).Width / s;
                    }

                    // Command Tag
                    float tagX = contentX + 12 + timeW + 16;
                    float tagW = DrawCommandTag(g, ae.Command, tagX, rowY + 11);

                    // Filename / Display Text
                    if (_bodyFont != null)
                    {
                        using var countBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                        float fileCountX = tagX + tagW + 16;
                        
                        string displayText = activeFiles.Length > 0
                            ? Path.GetFileName(activeFiles[idxActive])
                            : $"{ae.FileCount} {GetText("label_files")}";

                        float maxW = (contentX + rowW - 160) - fileCountX;
                        if (maxW > 20)
                        {
                            displayText = TruncateFileName(g, displayText, _bodyFont, maxW, s);
                        }
                        g.DrawString(displayText, _bodyFont, countBrush, fileCountX * s, (rowY + 13) * s);
                    }

                    // Status Label
                    string statusText = fileStatus switch
                    {
                        ConversionStatus.Pending => GetText("status_pending"),
                        ConversionStatus.InProgress => GetText("status_converting"),
                        ConversionStatus.Success => GetText("status_success"),
                        ConversionStatus.Failed => GetText("status_failed"),
                        _ => ""
                    };
                    Color statusColor = fileStatus switch
                    {
                        ConversionStatus.Pending => Color.FromArgb(180, 180, 100),
                        ConversionStatus.InProgress => Color.FromArgb(80, 160, 240),
                        ConversionStatus.Success => Color.FromArgb(100, 220, 100),
                        ConversionStatus.Failed => Color.FromArgb(255, 90, 70),
                        _ => Color.Gray
                    };

                    if (_tagFont != null)
                    {
                        using var statusBrush = new SolidBrush(statusColor);
                        g.DrawString(statusText, _tagFont, statusBrush, (contentX + rowW - 150) * s, (rowY + 13) * s);
                    }


                    startY += 52;
                }
            }

            // ——— 顯示持久化歷史紀錄———
            if (_historyEntries == null || _historyEntries.Count == 0)
            {
                if (!activeEntry.HasValue && _tabFont != null)
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

                using var path = GetRoundedRectPath(new RectangleF(contentX * s, currentY * s, rowW * s, currentH * s), 6 * s);
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

                // 檔案數 (動態相對起點)
                if (_bodyFont != null)
                {
                    using var countBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                    float fileCountX = tagX + tagW + 16;
                    g.DrawString($"{entry.FileCount} {GetText("label_files")}", _bodyFont, countBrush, fileCountX * s, (currentY + 13) * s);
                }

                // 狀態標籤
                Color statusColor = entry.IsSuccess ? Color.FromArgb(100, 220, 100) : Color.FromArgb(255, 90, 70);
                string statusText = entry.IsSuccess ? GetText("status_success") : GetText("status_failed");
                if (_tagFont != null)
                {
                    using var statusBrush = new SolidBrush(statusColor);
                    g.DrawString(statusText, _tagFont, statusBrush, (contentX + rowW - 150) * s, (currentY + 13) * s);
                }

                // 錯誤訊息（失敗時）
                if (!entry.IsSuccess && !string.IsNullOrEmpty(entry.ErrorMessage) && !isExpanded)
                {
                    if (_subFont != null)
                    {
                        using var errBrush = new SolidBrush(Color.FromArgb(230, 90, 70));
                        string errText = entry.ErrorMessage.Length > 14 ? entry.ErrorMessage.Substring(0, 14) + "..." : entry.ErrorMessage;
                        g.DrawString(errText, _subFont, errBrush, (contentX + rowW - 90) * s, (currentY + 14) * s);
                    }
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
                        float w4 = g.MeasureString(GetText("history_detail_elapsed") + ":", _subFont).Width / s;
                        float maxLabelW = Math.Max(w1, Math.Max(w2, Math.Max(w3, w4)));
                        float valX = contentX + 12 + maxLabelW + 16;

                        // 1. Files / Input Paths
                        g.DrawString(GetText("history_detail_inputs") + ":", _subFont, labelBrush, (contentX + 12) * s, (currentY + 54) * s);
                        string inputsText = entry.InputPaths;
                        if (string.IsNullOrEmpty(inputsText)) inputsText = "N/A";
                        else inputsText = inputsText.Replace(";", ", ");
                        if (inputsText.Length > 60) inputsText = inputsText.Substring(0, 57) + "...";
                        g.DrawString(inputsText, _subFont, valBrush, valX * s, (currentY + 54) * s);

                        // 2. Output Path
                        g.DrawString(GetText("history_detail_outputs") + ":", _subFont, labelBrush, (contentX + 12) * s, (currentY + 80) * s);
                        string outputsText = entry.OutputPath;
                        if (string.IsNullOrEmpty(outputsText)) outputsText = "N/A";
                        if (outputsText.Length > 60) outputsText = outputsText.Substring(0, 57) + "...";
                        g.DrawString(outputsText, _subFont, valBrush, valX * s, (currentY + 80) * s);

                        // 3. Time Details
                        g.DrawString(GetText("history_detail_time") + ":", _subFont, labelBrush, (contentX + 12) * s, (currentY + 106) * s);
                        string timeText = $"{entry.Time}  →  {(string.IsNullOrEmpty(entry.EndTime) ? entry.Time : entry.EndTime)}";
                        g.DrawString(timeText, _subFont, valBrush, valX * s, (currentY + 106) * s);

                        // 4. Elapsed Time
                        g.DrawString(GetText("history_detail_elapsed") + ":", _subFont, labelBrush, (contentX + 12) * s, (currentY + 132) * s);
                        string elapsedText = entry.ElapsedMs >= 0 ? $"{(entry.ElapsedMs / 1000.0):F2} s ({entry.ElapsedMs} ms)" : "N/A";
                        g.DrawString(elapsedText, _subFont, valBrush, valX * s, (currentY + 132) * s);
                    }
                }

                currentY += currentH + 8;
            }
        }

        static float DrawCommandTag(Graphics g, string command, float x, float y)
        {
            float s = _dpiScale;
            Color tagBg;
            string text = command;
            switch (command.ToLowerInvariant())
            {
                case "word2pdf":
                    tagBg = Color.FromArgb(0, 120, 212);
                    text = GetText("cmd_word_to_pdf");
                    break;
                case "ppt2pdf":
                    tagBg = Color.FromArgb(180, 50, 30);
                    text = GetText("cmd_ppt_to_pdf");
                    break;
                case "merge-pdf":
                    tagBg = Color.FromArgb(16, 124, 65);
                    text = GetText("cmd_merge_pdf");
                    break;
                case "img2pdf":
                    tagBg = Color.FromArgb(100, 60, 180);
                    text = GetText("cmd_img_to_pdf");
                    break;
                case "img-merge":
                    tagBg = Color.FromArgb(0, 130, 135);
                    text = GetText("cmd_merge_img");
                    break;
                case "img-stitch":
                    tagBg = Color.FromArgb(216, 59, 1);
                    text = GetText("cmd_stitch_img");
                    break;
                default:
                    tagBg = Color.FromArgb(100, 100, 100);
                    break;
            }

            float textW = 0;
            if (_tagFont != null)
            {
                textW = g.MeasureString(text, _tagFont).Width / s;
            }
            float w = Math.Max(82f, textW + 16f);
            int h = 22;
            using var path = GetRoundedRectPath(new RectangleF(x * s, y * s, w * s, h * s), 4 * s);
            using var brush = new SolidBrush(tagBg);
            g.FillPath(brush, path);

            if (_tagFont != null)
            {
                var size = g.MeasureString(text, _tagFont);
                g.DrawString(text, _tagFont, Brushes.White, (x + (w - size.Width / s) / 2) * s, (y + (h - size.Height / s) / 2) * s);
            }
            return w;
        }

        static void DrawSettingsTab(Graphics g, float logW, float logH, float contentX)
        {
            float s = _dpiScale;
            // Title
            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_settings"), _contentTitleFont, Brushes.White, contentX * s, 30 * s);

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, contentX * s, 75 * s, (logW - 40) * s, 75 * s);
            }

            // Quiet mode setting
            bool quietMode = ClickraStorage.GetSetting("QuietMode").Equals("true", StringComparison.OrdinalIgnoreCase);
            bool isQuietHovered = _hoveredElement == 5;
            if (_tabFont != null)
                g.DrawString(GetText("setting_silent_title"), _tabFont, Brushes.White, contentX * s, 100 * s);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(GetText("setting_silent_desc"), _subFont, subBrush, contentX * s, 122 * s);
            }
            DrawToggleSwitch(g, quietMode, isQuietHovered, (int)logW - 100, 105, 44, 22);

            // Notification setting
            bool notification = ClickraStorage.GetSetting("Notification").Equals("true", StringComparison.OrdinalIgnoreCase);
            bool isNotifHovered = _hoveredElement == 6;
            if (_tabFont != null)
                g.DrawString(GetText("setting_notify_title"), _tabFont, Brushes.White, contentX * s, 170 * s);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(GetText("setting_notify_desc"), _subFont, subBrush, contentX * s, 192 * s);
            }
            DrawToggleSwitch(g, notification, isNotifHovered, (int)logW - 100, 175, 44, 22);

            // Output path setting
            if (_tabFont != null)
                g.DrawString(GetText("setting_output_title"), _tabFont, Brushes.White, contentX * s, 240 * s);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(GetText("setting_output_desc"), _subFont, subBrush, contentX * s, 262 * s);
            }

            string outputDirMode = ClickraStorage.GetSetting("OutputDir");
            bool isSource = outputDirMode.Equals("source", StringComparison.OrdinalIgnoreCase);
            bool isDesktop = outputDirMode.Equals("desktop", StringComparison.OrdinalIgnoreCase);
            bool isDownloads = outputDirMode.Equals("downloads", StringComparison.OrdinalIgnoreCase);
            bool isCustom = !isSource && !isDesktop && !isDownloads;

            string textSource = GetText("setting_output_same_as_source");
            string textDesktop = GetText("setting_output_desktop");
            string textDownloads = GetText("setting_output_downloads");
            string textCustom = GetText("setting_output_custom");

            float wSource = _wSource;
            float wDesktop = _wDesktop;
            float wDownloads = _wDownloads;
            float wCustom = _wCustom;

            float margin = 10f;
            float xSource = contentX;
            float xDesktop = xSource + wSource + margin;
            float xDownloads = xDesktop + wDesktop + margin;
            float xCustom = xDownloads + wDownloads + margin;

            DrawOutputDirButton(g, textSource, isSource, 7, (int)xSource, 290, (int)wSource);
            DrawOutputDirButton(g, textDesktop, isDesktop, 8, (int)xDesktop, 290, (int)wDesktop);
            DrawOutputDirButton(g, textDownloads, isDownloads, 9, (int)xDownloads, 290, (int)wDownloads);
            DrawOutputDirButton(g, textCustom, isCustom, 20, (int)xCustom, 290, (int)wCustom);

            float langY = 340;
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
                    g.DrawString($"{GetText("setting_output_selected_path")}: {displayText}", _subFont, pathBrush, contentX * s, 328 * s);
                }
                langY = 365;
            }

            _langDropdownY = (int)(langY + 50);

            // Language setting UI block
            if (_tabFont != null)
                g.DrawString(GetText("setting_lang_title"), _tabFont, Brushes.White, contentX * s, langY * s);
            if (_subFont != null)
            {
                using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                g.DrawString(GetText("setting_lang_desc"), _subFont, subBrush, contentX * s, (langY + 22) * s);
            }

            // Draw Dropdown Selector
            DrawLanguageDropdown(g, _langDropdownY, contentX);
        }

        static void DrawToggleSwitch(Graphics g, bool state, bool hovered, int x, int y, int w, int h)
        {
            float s = _dpiScale;
            // Track
            Color trackColor = state ? GetSystemColorizationColor() : Color.FromArgb(60, 60, 60);
            if (hovered)
            {
                trackColor = state ? Lighten(trackColor, 0.15f) : Color.FromArgb(80, 80, 80);
            }
            using var trackBrush = new SolidBrush(trackColor);
            using var path = GetRoundedRectPath(new RectangleF(x * s, y * s, w * s, h * s), (h / 2f) * s);
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
                btnBg = GetSystemColorizationColor();
                if (isHovered) btnBg = Lighten(btnBg, 0.15f);
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
            using var path = GetRoundedRectPath(new RectangleF(x * s, y * s, w * s, h * s), 4 * s);
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
            using var path = GetRoundedRectPath(new RectangleF(x * s, y * s, w * s, h * s), 6 * s);
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

        static void DrawAboutTab(Graphics g, float logW, float logH, float contentX)
        {
            float s = _dpiScale;
            // Title
            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_about"), _contentTitleFont, Brushes.White, contentX * s, 30 * s);

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, contentX * s, 75 * s, (logW - 40) * s, 75 * s);
            }

            // 1. Project Description
            float currentY = 95;
            if (_sectionFont != null)
            {
                g.DrawString(GetText("about_desc_title"), _sectionFont, Brushes.White, contentX * s, currentY * s);
            }

            currentY += 25;
            if (_bodyFont != null)
            {
                using var descBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                string descText = GetText("about_desc_body");
                float maxTextW = logW - contentX - 40;
                var size = g.MeasureString(descText, _bodyFont, (int)(maxTextW * s));
                g.DrawString(descText, _bodyFont, descBrush, new RectangleF(contentX * s, currentY * s, maxTextW * s, size.Height));
                currentY += size.Height / s;
            }
            else
            {
                currentY += 40;
            }

            // 2. Collaboration Guide
            currentY += 20;
            if (_sectionFont != null)
            {
                g.DrawString(GetText("about_collab_title"), _sectionFont, Brushes.White, contentX * s, currentY * s);
            }

            currentY += 25;
            if (_bodyFont != null)
            {
                using var collabBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                string collabText = GetText("about_collab_body");
                float maxTextW = logW - contentX - 40;
                var size = g.MeasureString(collabText, _bodyFont, (int)(maxTextW * s));
                g.DrawString(collabText, _bodyFont, collabBrush, new RectangleF(contentX * s, currentY * s, maxTextW * s, size.Height));
                currentY += size.Height / s;
            }
            else
            {
                currentY += 40;
            }

            string textGit = GetText("about_btn_github");
            string textGmail = GetText("about_btn_gmail");

            float wGit = _wGit;
            float wGmail = _wGmail;

            // Button to open GitHub repository
            _githubBtnY = currentY + 12;
            bool isGitBtnHovered = _hoveredElement == 23;
            Color gitBtnBg = isGitBtnHovered ? Color.FromArgb(70, 70, 70) : Color.FromArgb(50, 50, 50);
            Color gitBtnBorder = isGitBtnHovered ? Color.FromArgb(90, 90, 90) : Color.FromArgb(70, 70, 70);
            using (var btnBgBrush = new SolidBrush(gitBtnBg))
            using (var btnBorderPen = new Pen(gitBtnBorder))
            using (var path = GetRoundedRectPath(new RectangleF(contentX * s, _githubBtnY * s, wGit * s, 32 * s), 4 * s))
            {
                g.FillPath(btnBgBrush, path);
                g.DrawPath(btnBorderPen, path);
            }
            if (_subFont != null)
            {
                Color btnText = isGitBtnHovered ? Color.White : Color.FromArgb(200, 200, 200);
                using var btnTextBrush = new SolidBrush(btnText);
                if (_iconFont != null)
                {
                    string iconGit = "\uE71B";
                    var iconSize = g.MeasureString(iconGit, _iconFont);
                    var textSize = g.MeasureString(textGit, _subFont);
                    float iconW = iconSize.Width / s;
                    float iconH = iconSize.Height / s;
                    float textW = textSize.Width / s;
                    float textH = textSize.Height / s;
                    float spacing = 6f;
                    float totalContentW = iconW + spacing + textW;
                    float startX = contentX + (wGit - totalContentW) / 2;

                    g.DrawString(iconGit, _iconFont, btnTextBrush, startX * s, (_githubBtnY + (32 - iconH) / 2) * s);
                    g.DrawString(textGit, _subFont, btnTextBrush, (startX + iconW + spacing) * s, (_githubBtnY + (32 - textH) / 2) * s);
                }
                else
                {
                    var size = g.MeasureString(textGit, _subFont);
                    g.DrawString(textGit, _subFont, btnTextBrush, (contentX + (wGit - size.Width / s) / 2) * s, (_githubBtnY + (32 - size.Height / s) / 2) * s);
                }
            }
            currentY += 12 + 32;

            // 3. Diagnostics Info
            currentY += 20;
            if (_sectionFont != null)
            {
                g.DrawString(GetText("about_diag_title"), _sectionFont, Brushes.White, contentX * s, currentY * s);
            }

            currentY += 25;
            if (_bodyFont != null)
            {
                using var diagBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                string diagText = GetText("about_diag_body");
                float maxTextW = logW - contentX - 40;
                var size = g.MeasureString(diagText, _bodyFont, (int)(maxTextW * s));
                g.DrawString(diagText, _bodyFont, diagBrush, new RectangleF(contentX * s, currentY * s, maxTextW * s, size.Height));
                currentY += size.Height / s;
            }
            else
            {
                currentY += 40;
            }

            // Buttons for diagnostics
            _aboutBtnY = currentY + 16;

            // One-click Gmail feedback button (ID 24)
            bool isGmailBtnHovered = _hoveredElement == 24;
            Color gmailBtnBg = isGmailBtnHovered ? Color.FromArgb(70, 70, 70) : Color.FromArgb(50, 50, 50);
            Color gmailBtnBorder = isGmailBtnHovered ? Color.FromArgb(90, 90, 90) : Color.FromArgb(70, 70, 70);
            using (var btnBgBrush = new SolidBrush(gmailBtnBg))
            using (var btnBorderPen = new Pen(gmailBtnBorder))
            using (var path = GetRoundedRectPath(new RectangleF(contentX * s, _aboutBtnY * s, wGmail * s, 32 * s), 4 * s))
            {
                g.FillPath(btnBgBrush, path);
                g.DrawPath(btnBorderPen, path);
            }
            if (_subFont != null)
            {
                Color btnText = isGmailBtnHovered ? Color.White : Color.FromArgb(200, 200, 200);
                using var btnTextBrush = new SolidBrush(btnText);
                if (_iconFont != null)
                {
                    string iconGmail = "\uE715";
                    var iconSize = g.MeasureString(iconGmail, _iconFont);
                    var textSize = g.MeasureString(textGmail, _subFont);
                    float iconW = iconSize.Width / s;
                    float iconH = iconSize.Height / s;
                    float textW = textSize.Width / s;
                    float textH = textSize.Height / s;
                    float spacing = 6f;
                    float totalContentW = iconW + spacing + textW;
                    float startX = contentX + (wGmail - totalContentW) / 2;

                    g.DrawString(iconGmail, _iconFont, btnTextBrush, startX * s, (_aboutBtnY + (32 - iconH) / 2) * s);
                    g.DrawString(textGmail, _subFont, btnTextBrush, (startX + iconW + spacing) * s, (_aboutBtnY + (32 - textH) / 2) * s);
                }
                else
                {
                    var size = g.MeasureString(textGmail, _subFont);
                    g.DrawString(textGmail, _subFont, btnTextBrush, (contentX + (wGmail - size.Width / s) / 2) * s, (_aboutBtnY + (32 - size.Height / s) / 2) * s);
                }
            }
        }

        static GraphicsPath GetRoundedRectPath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        static Color GetSystemColorizationColor()
        {
            if (_hasCachedColorizationColor) return _cachedColorizationColor;
            try
            {
                DwmGetColorizationColor(out uint color, out bool _);
                _cachedColorizationColor = Color.FromArgb(255, Color.FromArgb((int)color));
                _hasCachedColorizationColor = true;
                return _cachedColorizationColor;
            }
            catch
            {
                _cachedColorizationColor = Color.FromArgb(255, 0, 120, 212); // Microsoft Blue
                _hasCachedColorizationColor = true;
                return _cachedColorizationColor;
            }
        }

        static Color Lighten(Color c, float amount)
        {
            int r = (int)(c.R + (255 - c.R) * amount);
            int g = (int)(c.G + (255 - c.G) * amount);
            int b = (int)(c.B + (255 - c.B) * amount);
            return Color.FromArgb(255, Math.Min(255, r), Math.Min(255, g), Math.Min(255, b));
        }

        static bool IsOfficeInstalled(string app)
        {
            string progId = app == "PowerPoint" ? "PowerPoint.Application" : "Word.Application";
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Classes\{progId}");
                return key != null;
            }
            catch { return false; }
        }

        static void DrawLanguageDropdown(Graphics g, int y, float contentX)
        {
            float s = _dpiScale;
            string currentLangCode = ClickraStorage.GetSetting("Language");
            currentLangCode = Clickra.Core.Localization.NormalizeLanguageCode(currentLangCode);
            
            var currentLang = SupportedLanguages.FirstOrDefault(l => l.Code.Equals(currentLangCode, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(currentLang.Code))
            {
                currentLang = SupportedLanguages[0]; // Default to Traditional Chinese
            }

            string displayText = $"{currentLang.NativeName} ({currentLang.EnglishName})";
            bool isHovered = _hoveredElement == 10;

            int x = (int)contentX, w = 240, h = 30;

            Color btnBg = isHovered ? Color.FromArgb(55, 55, 55) : Color.FromArgb(40, 40, 40);
            Color btnBorder = _langDropdownOpen ? GetSystemColorizationColor() : (isHovered ? Color.FromArgb(80, 80, 80) : Color.FromArgb(60, 60, 60));
            Color textColor = Color.FromArgb(220, 220, 220);

            // Draw button base
            using (var path = GetRoundedRectPath(new RectangleF(x * s, y * s, w * s, h * s), 4 * s))
            using (var bgBrush = new SolidBrush(btnBg))
            using (var borderPen = new Pen(btnBorder, _langDropdownOpen ? 1.5f * s : 1f * s))
            {
                g.FillPath(bgBrush, path);
                g.DrawPath(borderPen, path);
            }

            // Draw selected language text
            if (_subFont != null)
            {
                using var textBrush = new SolidBrush(textColor);
                g.DrawString(displayText, _subFont, textBrush, (x + 10) * s, (y + 7) * s);
            }

            // Draw Chevron Down icon
            if (_iconFont != null)
            {
                using var iconBrush = new SolidBrush(Color.FromArgb(160, 160, 160));
                g.DrawString("\uE70D", _iconFont, iconBrush, (x + w - 24) * s, (y + 9) * s);
            }

            // Draw overlay popup list if open
            if (_langDropdownOpen)
            {
                int popupH = 180;
                int popupY = y - popupH; // 210

                // Container path
                using (var path = GetRoundedRectPath(new RectangleF(x * s, popupY * s, w * s, popupH * s), 4 * s))
                using (var bgBrush = new SolidBrush(Color.FromArgb(28, 28, 28)))
                using (var borderPen = new Pen(Color.FromArgb(60, 60, 60)))
                {
                    g.FillPath(bgBrush, path);
                    g.DrawPath(borderPen, path);
                }

                // Search input box: y = 216
                int searchX = x + 6, searchY = popupY + 6, searchW = w - 12, searchH = 26;
                using (var searchPath = GetRoundedRectPath(new RectangleF(searchX * s, searchY * s, searchW * s, searchH * s), 4 * s))
                using (var searchBg = new SolidBrush(Color.FromArgb(45, 45, 45)))
                using (var searchBorder = new Pen(Color.FromArgb(75, 75, 75)))
                {
                    g.FillPath(searchBg, searchPath);
                    g.DrawPath(searchBorder, searchPath);
                }

                // Draw Search Icon
                if (_iconFont != null)
                {
                    using var searchIconBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    g.DrawString("\uE721", _iconFont, searchIconBrush, (searchX + 8) * s, (searchY + 7) * s);
                }

                // Draw Search Text or Placeholder
                if (_subFont != null)
                {
                    if (string.IsNullOrEmpty(_langSearchQuery))
                    {
                        using var placeholderBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
                        g.DrawString(GetText("search_lang_placeholder"), _subFont, placeholderBrush, (searchX + 26) * s, (searchY + 6) * s);
                    }
                    else
                    {
                        using var queryBrush = new SolidBrush(Color.White);
                        g.DrawString(_langSearchQuery, _subFont, queryBrush, (searchX + 26) * s, (searchY + 6) * s);
                    }

                    // Draw flashing cursor (caret)
                    if ((DateTime.Now.Millisecond / 500) % 2 == 0)
                    {
                        var size = g.MeasureString(_langSearchQuery, _subFont);
                        using var cursorBrush = new SolidBrush(Color.White);
                        g.FillRectangle(cursorBrush, (searchX + 26) * s + size.Width, (searchY + 6) * s, 1.5f * s, 13 * s);
                    }
                }

                // Draw filtered list
                var filtered = GetFilteredLanguages();
                int listStartY = searchY + searchH + 6; // 248
                int maxVisible = 5;

                if (_langScrollOffset < 0) _langScrollOffset = 0;
                if (_langScrollOffset > 0 && _langScrollOffset > filtered.Count - maxVisible)
                {
                    _langScrollOffset = Math.Max(0, filtered.Count - maxVisible);
                }

                int drawCount = Math.Min(maxVisible, filtered.Count - _langScrollOffset);
                for (int i = 0; i < drawCount; i++)
                {
                    int itemIdx = _langScrollOffset + i;
                    var item = filtered[itemIdx];
                    int itemY = listStartY + i * 26;
                    int itemH = 24;

                    bool isItemHovered = _langHoveredIndex == itemIdx;
                    Color itemBg = isItemHovered ? GetSystemColorizationColor() : Color.Transparent;
                    Color itemTextCol = isItemHovered ? Color.White : Color.FromArgb(200, 200, 200);

                    if (isItemHovered)
                    {
                        using (var itemPath = GetRoundedRectPath(new RectangleF((x + 4) * s, itemY * s, (w - 8) * s, itemH * s), 3 * s))
                        using (var itemBgBrush = new SolidBrush(itemBg))
                        {
                            g.FillPath(itemBgBrush, itemPath);
                        }
                    }

                    if (_subFont != null)
                    {
                        using var itemTextBrush = new SolidBrush(itemTextCol);
                        g.DrawString($"{item.NativeName} ({item.EnglishName})", _subFont, itemTextBrush, (x + 10) * s, (itemY + 5) * s);
                    }
                }

                // Draw scrollbar for Language Dropdown
                if (filtered.Count > maxVisible)
                {
                    float trackX = x + w - 8;
                    float trackY = listStartY;
                    float trackW = 4;
                    float trackH = maxVisible * 26 - 2; // 128
                    using (var sbTrackBrush = new SolidBrush(Color.FromArgb(40, 40, 40)))
                    {
                        g.FillRectangle(sbTrackBrush, trackX * s, trackY * s, trackW * s, trackH * s);
                    }

                    float thumbH = Math.Max(15f, ((float)maxVisible / filtered.Count) * trackH);
                    float thumbY = trackY + ((float)_langScrollOffset / (filtered.Count - maxVisible)) * (trackH - thumbH);
                    using (var sbThumbBrush = new SolidBrush(Color.FromArgb(100, 100, 100)))
                    {
                        using (var thumbPath = GetRoundedRectPath(new RectangleF(trackX * s, thumbY * s, trackW * s, thumbH * s), 2 * s))
                        {
                            g.FillPath(sbThumbBrush, thumbPath);
                        }
                    }
                }
            }
        }

        static List<(string Code, string NativeName, string EnglishName)> GetFilteredLanguages()
        {
            if (string.IsNullOrEmpty(_langSearchQuery))
            {
                return SupportedLanguages;
            }
            return SupportedLanguages.Where(l =>
                l.NativeName.Contains(_langSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                l.EnglishName.Contains(_langSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                l.Code.Contains(_langSearchQuery, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        static void SelectLanguage(string code)
        {
            ClickraStorage.SaveSetting("Language", code);
            RecreateScaledFonts();
        }

        static string GetText(string key)
        {
            return Clickra.Core.Localization.T(key, ClickraStorage.GetSetting("Language"));
        }
    }
}

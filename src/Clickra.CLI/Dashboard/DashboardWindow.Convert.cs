using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Clickra.Core;
using Clickra.Core.Processors;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
        // File filters and command metadata live in the ConvertCommandDefs
        // registry (see DashboardWindow.ConvertRegistry.cs).
        /// <summary>Shows the Win32 file-open dialog and returns the selected paths.</summary>
        static List<string> OpenFiles(IntPtr hwndOwner, string filter, string title)
        {
            var files = new List<string>();
            var ofn = new OPENFILENAME();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.hwndOwner = hwndOwner;
            ofn.lpstrFilter = filter;
            
            int maxFile = 65536;
            IntPtr fileBuffer = Marshal.AllocHGlobal(maxFile * 2);
            byte[] zeros = new byte[maxFile * 2];
            Marshal.Copy(zeros, 0, fileBuffer, zeros.Length);
            
            ofn.lpstrFile = fileBuffer;
            ofn.nMaxFile = maxFile;
            ofn.lpstrTitle = title;
            ofn.Flags = 0x00080000 | 0x00000200 | 0x00001000 | 0x00000004;

            if (GetOpenFileName(ref ofn))
            {
                var paths = new List<string>();
                IntPtr currentPtr = fileBuffer;
                while (true)
                {
                    string? s = Marshal.PtrToStringUni(currentPtr);
                    if (string.IsNullOrEmpty(s)) break;
                    paths.Add(s);
                    currentPtr += (s.Length + 1) * 2;
                }

                if (paths.Count > 0)
                {
                    if (paths.Count == 1)
                    {
                        files.Add(paths[0]);
                    }
                    else
                    {
                        string dir = paths[0];
                        for (int i = 1; i < paths.Count; i++)
                        {
                            files.Add(Path.Combine(dir, paths[i]));
                        }
                    }
                }
            }
            Marshal.FreeHGlobal(fileBuffer);
            return files;
        }

        /// <summary>Maps a command key to its index in ConvertCommands (-1 when unknown).</summary>
        static int GetCommandIndex(string cmd)
        {
            return ConvertCommandByKey.TryGetValue(cmd, out var command) ? Array.IndexOf(ConvertCommands, command) : -1;
        }

        /// <summary>Whether the currently selected convert command accepts the given files;
        /// used to keep the user's explicit choice when importing or dropping files.</summary>
        static bool CurrentSelectionAcceptsFiles(List<string> files)
        {
            return _convertCommandIndex >= 0 && _convertCommandIndex < ConvertCommands.Length
                && ConvertCommands[_convertCommandIndex].ValidateFiles(files, out _);
        }

        /// <summary>Queues a conversion action for files dropped onto the dashboard window.</summary>
        static void HandleDroppedFiles(List<string> files)
        {
            var extensions = files.Select(f => Path.GetExtension(f).ToLowerInvariant()).Distinct().ToList();
            if (extensions.Count == 0) return;

            if (CurrentSelectionAcceptsFiles(files))
            {
                // Keep the user's explicit command when it accepts the dropped files
                // (e.g. 分割 PDF stays selected after dropping a PDF).
                _selectedFiles = files;
                return;
            }

            if (extensions.All(ext => ext == ".ppt" || ext == ".pptx"))
            {
                ConvertCommand.Select(ConvertCommands[GetCommandIndex("ppt2pdf")]);
            }
            else if (extensions.All(ext => ext == ".doc" || ext == ".docx"))
            {
                ConvertCommand.Select(ConvertCommands[GetCommandIndex("word2pdf")]);
            }
            else if (extensions.All(ext => ext == ".xlsx" || ext == ".xls"))
            {
                ConvertCommand.Select(ConvertCommands[GetCommandIndex("excel2pdf")]);
            }
            else if (extensions.All(ext => ext == ".pdf"))
            {
                ConvertCommand.Select(ConvertCommands[GetCommandIndex(files.Count == 1 ? "compress-pdf" : "merge-pdf")]);
            }
            else if (extensions.All(ext => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" }.Contains(ext)))
            {
                ConvertCommand.Select(ConvertCommands[GetCommandIndex(files.Count > 1 ? "img-merge" : "img2pdf")]);
            }

            _selectedFiles = files;
        }

        /// <summary>Runs the currently selected convert command for the selected files.</summary>
        static void RunConversion(IntPtr hwnd)
        {
            if (_convertCommandIndex < 0 || _convertCommandIndex >= ConvertCommands.Length) return;
            ConvertCommand.Run(ConvertCommands[_convertCommandIndex], hwnd);
        }

        static string GetCommandGroupKey(int groupIndex)
        {
            return groupIndex switch
            {
                0 => "convert_group_office",
                1 => "convert_group_pdf",
                2 => "convert_group_image",
                _ => ""
            };
        }

        static void DrawConvertTab(Graphics g, float logW, float logH, float contentX)
        {
            float s = _dpiScale;
            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_convert"), _contentTitleFont, Brushes.White, contentX * s, 30 * s);

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, contentX * s, 75 * s, (logW - 40) * s, 75 * s);
            }

            int zoneX = (int)contentX, zoneY = 95, zoneW = (int)logW - (int)contentX - 50, zoneH = 120;
            bool isZoneHovered = _hoveredElement == 18;

            Color zoneBg = isZoneHovered ? Color.FromArgb(42, 42, 42) : Color.FromArgb(34, 34, 34);
            Color zoneBorder = isZoneHovered ? UIHelper.GetSystemColorizationColor() : Color.FromArgb(60, 60, 60);

            using (var path = UIHelper.GetRoundedRectPath(new RectangleF(zoneX * s, zoneY * s, zoneW * s, zoneH * s), 6 * s))
            using (var bgBrush = new SolidBrush(zoneBg))
            using (var borderPen = new Pen(zoneBorder, 1.5f * s))
            {
                borderPen.DashStyle = DashStyle.Dash;
                g.FillPath(bgBrush, path);
                g.DrawPath(borderPen, path);
            }

            if (_selectedFiles.Count == 0)
            {
                if (_iconFont != null)
                {
                    using var iconBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    g.DrawString("\uE118", _iconFont, iconBrush, (zoneX + (zoneW - 20) / 2) * s, (zoneY + 25) * s);
                }

                if (_tabFont != null)
                {
                    string hint = GetText("convert_drag_drop_hint");
                    using var textBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
                    var size = g.MeasureString(hint, _tabFont);
                    g.DrawString(hint, _tabFont, textBrush, (zoneX + (zoneW - size.Width / s) / 2) * s, (zoneY + 55) * s);
                }

                if (_subFont != null)
                {
                    string subHint = GetText("convert_drag_drop_sub");
                    using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    var size = g.MeasureString(subHint, _subFont);
                    g.DrawString(subHint, _subFont, subBrush, (zoneX + (zoneW - size.Width / s) / 2) * s, (zoneY + 80) * s);
                }
            }
            else
            {
                if (_tabFont != null)
                {
                    string summary = string.Format(GetText("convert_selected_count"), _selectedFiles.Count);
                    using var textBrush = new SolidBrush(Color.FromArgb(100, 220, 100));
                    g.DrawString(summary, _tabFont, textBrush, (zoneX + 20) * s, (zoneY + 20) * s);
                }

                if (_subFont != null)
                {
                    using var listBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
                    string joinedNames = string.Join(", ", _selectedFiles.Select(Path.GetFileName));
                    if (joinedNames.Length > 85)
                    {
                        joinedNames = joinedNames.Substring(0, 82) + "...";
                    }
                    g.DrawString(joinedNames, _subFont, listBrush, (zoneX + 20) * s, (zoneY + 50) * s);

                    string outDirMode = ClickraStorage.GetSetting("OutputDir");
                    string outPathDesc = outDirMode.ToLowerInvariant() switch
                    {
                        "desktop" => GetText("setting_output_desktop"),
                        "downloads" => GetText("setting_output_downloads"),
                        _ => outDirMode.Equals("source", StringComparison.OrdinalIgnoreCase) ? GetText("setting_output_same_as_source") : GetText("setting_output_custom")
                    };
                    using var descBrush = new SolidBrush(Color.FromArgb(130, 130, 130));
                    g.DrawString($"{GetText("setting_output_title")}: {outPathDesc}", _subFont, descBrush, (zoneX + 20) * s, (zoneY + 85) * s);
                }

                int clearX = (int)logW - 110;
                bool isClearHovered = _hoveredElement == 25;
                Color clearBtnBg = isClearHovered ? Color.FromArgb(60, 60, 60) : Color.FromArgb(45, 45, 45);
                Color clearBtnBorder = isClearHovered ? Color.FromArgb(80, 80, 80) : Color.FromArgb(55, 55, 55);
                using (var path = UIHelper.GetRoundedRectPath(new RectangleF(clearX * s, (zoneY + 12) * s, 48 * s, 22 * s), 3 * s))
                using (var bgBrush = new SolidBrush(clearBtnBg))
                using (var borderPen = new Pen(clearBtnBorder))
                {
                    g.FillPath(bgBrush, path);
                    g.DrawPath(borderPen, path);
                }
                if (_subFont != null)
                {
                    Color btnText = isClearHovered ? Color.White : Color.FromArgb(180, 180, 180);
                    using var textBrush = new SolidBrush(btnText);
                    string clearText = GetText("convert_clear");
                    var size = g.MeasureString(clearText, _subFont);
                    g.DrawString(clearText, _subFont, textBrush, (clearX + (48 - size.Width / s) / 2) * s, (zoneY + 12 + (22 - size.Height / s) / 2) * s);
                }
            }

            int groupGap = 14;
            int groupW = (zoneW - 2 * groupGap) / 3;
            int groupTop = 230;
            int headerH = 24;
            int cardH = 38;
            int cardGap = 8;
            for (int group = 0; group < 3; group++)
            {
                int groupX = zoneX + group * (groupW + groupGap);
                if (_subFont != null)
                {
                    using var headerBrush = new SolidBrush(Color.FromArgb(170, 170, 170));
                    g.DrawString(GetText(GetCommandGroupKey(group)), _subFont, headerBrush, groupX * s, groupTop * s);
                }

                int commandStart = 0;
                for (int before = 0; before < group; before++)
                    commandStart += ConvertCommandGroupSizes[before];

                for (int local = 0; local < ConvertCommandGroupSizes[group]; local++)
                {
                    int i = commandStart + local;
                    int cardX = groupX;
                    int cardY = groupTop + headerH + local * (cardH + cardGap);
                    int cardW = groupW;

                    bool isSelected = _convertCommandIndex == i;
                    bool isHovered = _hoveredElement == (50 + i);
                    bool isEnabled = ConvertCommands[i].ValidateFiles(_selectedFiles, out _);

                    Color cardBg;
                    Color cardBorder;
                    Color textColor;

                    if (!isEnabled)
                    {
                        cardBg = Color.FromArgb(28, 28, 28);
                        cardBorder = Color.FromArgb(36, 36, 36);
                        textColor = Color.FromArgb(80, 80, 80);
                    }
                    else if (isSelected)
                    {
                        cardBg = Color.FromArgb(45, 45, 55);
                        cardBorder = UIHelper.GetSystemColorizationColor();
                        textColor = Color.White;
                    }
                    else
                    {
                        cardBg = isHovered ? Color.FromArgb(50, 50, 50) : Color.FromArgb(36, 36, 36);
                        cardBorder = isHovered ? Color.FromArgb(80, 80, 80) : Color.FromArgb(48, 48, 48);
                        textColor = isHovered ? Color.White : Color.FromArgb(200, 200, 200);
                    }

                    using var path = UIHelper.GetRoundedRectPath(new RectangleF(cardX * s, cardY * s, cardW * s, cardH * s), 5 * s);
                    using var bgBrush = new SolidBrush(cardBg);
                    using var borderPen = new Pen(cardBorder, isSelected ? 1.5f * s : 1f * s);
                    g.FillPath(bgBrush, path);
                    g.DrawPath(borderPen, path);

                    string cmdText = ConvertCommands[i].DisplayName;

                    if (_tabFont != null)
                    {
                        using var textBrush = new SolidBrush(textColor);
                        var size = g.MeasureString(cmdText, _tabFont);
                        g.DrawString(cmdText, _tabFont, textBrush, (cardX + (cardW - size.Width / s) / 2) * s, (cardY + (cardH - size.Height / s) / 2) * s);
                    }
                }
            }

            int maxCommandRows = ConvertCommandGroupSizes.Max();
            int buttonY = groupTop + headerH + maxCommandRows * (cardH + cardGap) + 16;
            if (_selectedFiles.Count > 0 && _convertCommandIndex != -1)
            {
                bool isBtnHovered = _hoveredElement == 19;
                Color btnBg = UIHelper.GetSystemColorizationColor();
                if (isBtnHovered) btnBg = UIHelper.Lighten(btnBg, 0.15f);

                using (var path = UIHelper.GetRoundedRectPath(new RectangleF(zoneX * s, buttonY * s, zoneW * s, 36 * s), 5 * s))
                using (var bgBrush = new SolidBrush(btnBg))
                {
                    g.FillPath(bgBrush, path);
                }

                if (_tabFont != null)
                {
                    string btnText = GetText("convert_start");
                    using var textBrush = new SolidBrush(Color.White);
                    var size = g.MeasureString(btnText, _tabFont);
                    g.DrawString(btnText, _tabFont, textBrush, (zoneX + (zoneW - size.Width / s) / 2) * s, (buttonY + (36 - size.Height / s) / 2) * s);
                }
            }
        }
    }
}

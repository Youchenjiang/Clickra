using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Clickra.Core;
using Clickra.Core.Layout;
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

        /// <summary>Builds the shared file-picker filter from the command registry so every
        /// supported input type remains selectable when commands are added or changed.</summary>
        static string GetSupportedFilesFilter()
        {
            string patterns = string.Join(";", ConvertCommands
                .SelectMany(command => command.Extensions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(extension => $"*{extension}"));
            return $"Supported Files ({patterns})\0{patterns}\0All Files (*.*)\0*.*\0\0";
        }

        /// <summary>Imports files from any dashboard entry point while preserving an explicit
        /// compatible command and otherwise applying the shared default routing.</summary>
        static void ImportFiles(List<string> files)
        {
            if (files.Count == 0) return;

            if (!CurrentSelectionAcceptsFiles(files))
            {
                _convertCommandIndex = -1;
                string? defaultCommand = ConvertCommandRegistry.GetDefaultCommandForFiles(files);
                int defaultIndex = defaultCommand is null ? -1 : GetCommandIndex(defaultCommand);
                if (defaultIndex >= 0)
                {
                    ConvertCommand.Select(ConvertCommands[defaultIndex]);
                }
            }

            _selectedFiles = files;
        }

        /// <summary>Queues a conversion action for files dropped onto the dashboard window.</summary>
        static void HandleDroppedFiles(List<string> files) => ImportFiles(files);

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

        // skipcq: CS-R1140 — conversion painting intentionally coordinates multiple UI states in one pass.
        static void DrawConvertTab(Graphics g, float logW, float logH, float contentX)
        {
            float s = _dpiScale;
            if (_contentTitleFont != null)
                g.DrawString(GetText("tab_convert"), _contentTitleFont, Brushes.White, contentX * s, 30 * s);

            using (var divPen = new Pen(Color.FromArgb(48, 48, 48)))
            {
                g.DrawLine(divPen, contentX * s, 75 * s, (logW - 40) * s, 75 * s);
            }

            LayoutRect zone = DashboardLayout.ConvertZoneRect((int)contentX, (int)logW);
            int zoneX = zone.X, zoneY = zone.Y, zoneW = zone.Width, zoneH = zone.Height;
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
                    g.DrawString("\uE118", _iconFont, iconBrush, (zoneX + (zoneW - 20) / 2) * s,
                        (zoneY + DashboardLayout.ConvertZoneIconOffset) * s);
                }

                if (_tabFont != null)
                {
                    string hint = GetText("convert_drag_drop_hint");
                    using var textBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
                    var size = g.MeasureString(hint, _tabFont);
                    g.DrawString(hint, _tabFont, textBrush, (zoneX + (zoneW - size.Width / s) / 2) * s,
                        (zoneY + DashboardLayout.ConvertZoneHintOffset) * s);
                }

                if (_subFont != null)
                {
                    string subHint = GetText("convert_drag_drop_sub");
                    using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
                    var size = g.MeasureString(subHint, _subFont);
                    g.DrawString(subHint, _subFont, subBrush, (zoneX + (zoneW - size.Width / s) / 2) * s,
                        (zoneY + DashboardLayout.ConvertZoneSubHintOffset) * s);
                }
            }
            else
            {
                if (_tabFont != null)
                {
                    string summary = string.Format(GetText("convert_selected_count"), _selectedFiles.Count);
                    using var textBrush = new SolidBrush(Color.FromArgb(100, 220, 100));
                    g.DrawString(summary, _tabFont, textBrush, (zoneX + 20) * s,
                        (zoneY + DashboardLayout.ConvertZoneSummaryOffset) * s);
                }

                if (_subFont != null)
                {
                    using var listBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
                    string joinedNames = string.Join(", ", _selectedFiles.Select(Path.GetFileName));
                    if (joinedNames.Length > 85)
                    {
                        joinedNames = joinedNames.Substring(0, 82) + "...";
                    }
                    g.DrawString(joinedNames, _subFont, listBrush, (zoneX + 20) * s,
                        (zoneY + DashboardLayout.ConvertZoneFilesOffset) * s);

                    string outDirMode = ClickraStorage.GetSetting(ClickraSettings.OutputDir);
                    string outPathDesc = outDirMode.ToLowerInvariant() switch
                    {
                        "desktop" => GetText("setting_output_desktop"),
                        "downloads" => GetText("setting_output_downloads"),
                        _ => outDirMode.Equals("source", StringComparison.OrdinalIgnoreCase) ? GetText("setting_output_same_as_source") : GetText("setting_output_custom")
                    };
                    using var descBrush = new SolidBrush(Color.FromArgb(130, 130, 130));
                    g.DrawString($"{GetText("setting_output_title")}: {outPathDesc}", _subFont, descBrush, (zoneX + 20) * s,
                        (zoneY + DashboardLayout.ConvertZoneOutputOffset) * s);
                }

                LayoutRect clearButton = DashboardLayout.ConvertClearButtonRect((int)logW);
                bool isClearHovered = _hoveredElement == 25;
                Color clearBtnBg = isClearHovered ? Color.FromArgb(60, 60, 60) : Color.FromArgb(45, 45, 45);
                Color clearBtnBorder = isClearHovered ? Color.FromArgb(80, 80, 80) : Color.FromArgb(55, 55, 55);
                using (var path = UIHelper.GetRoundedRectPath(new RectangleF(clearButton.X * s, clearButton.Y * s, clearButton.Width * s, clearButton.Height * s), 3 * s))
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
                    g.DrawString(clearText, _subFont, textBrush,
                        (clearButton.X + (clearButton.Width - size.Width / s) / 2) * s,
                        (clearButton.Y + (clearButton.Height - size.Height / s) / 2) * s);
                }
            }

            for (int group = 0; group < DashboardLayout.ConvertGroupCount; group++)
            {
                int groupX = DashboardLayout.ConvertGroupX(group, zoneX, zoneW, ConvertCommandGroupSizes);
                if (_subFont != null)
                {
                    using var headerBrush = new SolidBrush(Color.FromArgb(170, 170, 170));
                    g.DrawString(GetText(GetCommandGroupKey(group)), _subFont, headerBrush, groupX * s,
                        DashboardLayout.ConvertGroupTop(group, ConvertCommandGroupSizes) * s);
                }

                int commandStart = 0;
                for (int before = 0; before < group; before++)
                    commandStart += ConvertCommandGroupSizes[before];

                for (int local = 0; local < ConvertCommandGroupSizes[group]; local++)
                {
                    int i = commandStart + local;
                    LayoutRect card = DashboardLayout.ConvertCardRect(group, local, zoneX, zoneW, ConvertCommandGroupSizes);

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

                    using var path = UIHelper.GetRoundedRectPath(new RectangleF(card.X * s, card.Y * s, card.Width * s, card.Height * s), 5 * s);
                    using var bgBrush = new SolidBrush(cardBg);
                    using var borderPen = new Pen(cardBorder, isSelected ? 1.5f * s : 1f * s);
                    g.FillPath(bgBrush, path);
                    g.DrawPath(borderPen, path);

                    string cmdText = ConvertCommands[i].DisplayName;

                    if (_tabFont != null)
                    {
                        using var textBrush = new SolidBrush(textColor);
                        var size = g.MeasureString(cmdText, _tabFont);
                        g.DrawString(cmdText, _tabFont, textBrush,
                            (card.X + (card.Width - size.Width / s) / 2) * s,
                            (card.Y + (card.Height - size.Height / s) / 2) * s);
                    }
                }
            }

        }

        static void DrawConvertStickyAction(Graphics g, float logW, float logH, float contentX)
        {
            if (_selectedFiles.Count == 0 || _convertCommandIndex == -1) return;

            float s = _dpiScale;
            int zoneW = Math.Max(0, (int)logW - (int)contentX - DashboardLayout.ConvertZoneRightMargin);
            LayoutRect startButton = DashboardLayout.ConvertStickyStartButtonRect(
                (int)contentX, zoneW, (int)logH);
            LayoutRect footer = DashboardLayout.ConvertStickyFooterRect(
                (int)contentX, Math.Max(0, (int)logW - (int)contentX), (int)logH);

            using var footerBrush = new SolidBrush(Color.FromArgb(32, 32, 32));
            g.FillRectangle(footerBrush,
                footer.X * s,
                footer.Y * s,
                footer.Width * s,
                footer.Height * s);

            bool isBtnHovered = _hoveredElement == 19;
            Color btnBg = UIHelper.GetSystemColorizationColor();
            if (isBtnHovered) btnBg = UIHelper.Lighten(btnBg, 0.15f);

            using (var path = UIHelper.GetRoundedRectPath(
                       new RectangleF(startButton.X * s, startButton.Y * s, startButton.Width * s, startButton.Height * s),
                       5 * s))
            using (var bgBrush = new SolidBrush(btnBg))
            {
                g.FillPath(bgBrush, path);
            }

            if (_tabFont != null)
            {
                string btnText = GetText("convert_start");
                using var textBrush = new SolidBrush(Color.White);
                var size = g.MeasureString(btnText, _tabFont);
                g.DrawString(btnText, _tabFont, textBrush,
                    (startButton.X + (startButton.Width - size.Width / s) / 2) * s,
                    (startButton.Y + (startButton.Height - size.Height / s) / 2) * s);
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Clickra.Core;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {
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

        static bool ValidateConvertFiles(string cmd, List<string> files, out string errorMsg)
        {
            errorMsg = "";
            if (files.Count == 0) return true;

            string[] allowed = cmd switch
            {
                "ppt2pdf" => new[] { ".ppt", ".pptx" },
                "word2pdf" => new[] { ".doc", ".docx" },
                "excel2pdf" => new[] { ".xlsx", ".xls" },
                "merge-pdf" => new[] { ".pdf" },
                "translate-pdf" => new[] { ".pdf" },
                "decrypt-pdf" => new[] { ".pdf" },
                "img2pdf" or "img-merge" or "img-stitch" => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" },
                _ => Array.Empty<string>()
            };

            var invalid = files.Where(f => !allowed.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
            if (invalid.Count > 0)
            {
                errorMsg = GetText("convert_err_invalid_ext");
                return false;
            }

            int minFiles = cmd switch
            {
                "merge-pdf" or "img-merge" or "img-stitch" => 2,
                _ => 1
            };

            if (files.Count < minFiles)
            {
                errorMsg = string.Format(GetText("convert_err_min_files"), minFiles);
                return false;
            }

            return true;
        }

        static void HandleDroppedFiles(List<string> files)
        {
            var extensions = files.Select(f => Path.GetExtension(f).ToLowerInvariant()).Distinct().ToList();
            if (extensions.Count == 0) return;

            if (extensions.All(ext => ext == ".ppt" || ext == ".pptx"))
            {
                ChangeConvertCommand(0);
            }
            else if (extensions.All(ext => ext == ".doc" || ext == ".docx"))
            {
                ChangeConvertCommand(1);
            }
            else if (extensions.All(ext => ext == ".xlsx" || ext == ".xls"))
            {
                ChangeConvertCommand(2);
            }
            else if (extensions.All(ext => ext == ".pdf"))
            {
                ChangeConvertCommand(files.Count == 1 ? 6 : 2);
            }
            else if (extensions.All(ext => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" }.Contains(ext)))
            {
                ChangeConvertCommand(files.Count > 1 ? 4 : 3);
            }

            _selectedFiles = files;
        }

        static void ChangeConvertCommand(int index)
        {
            _convertCommandIndex = index;
            string cmd = ConvertCommands[index];
            if (_selectedFiles.Count > 0)
            {
                if (!ValidateConvertFiles(cmd, _selectedFiles, out _))
                {
                    _selectedFiles.Clear();
                }
            }
        }

        static void RunConversion(IntPtr hwnd)
        {
            string cmd = ConvertCommands[_convertCommandIndex];
            if (_selectedFiles.Count == 0) return;

            if (!ValidateConvertFiles(cmd, _selectedFiles, out string error))
            {
                MessageBox(hwnd, error, "Clickra", 0x30);
                return;
            }

            var filesCopy = new List<string>(_selectedFiles);
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    ProgressWindow.Show(cmd, filesCopy);
                }
                catch (Exception ex)
                {
                    MessageBox(IntPtr.Zero, $"Execution failed: {ex.Message}", "Clickra", 0x10);
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();

            _selectedFiles.Clear();

            _activeTab = 2; // Switch to History
            RefreshHistoryData();
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        static string GetFilterForCommand(string cmd)
        {
            return cmd switch
            {
                "ppt2pdf" => "PowerPoint Files (*.ppt; *.pptx)\0*.ppt;*.pptx\0All Files (*.*)\0*.*\0\0",
                "word2pdf" => "Word Files (*.doc; *.docx)\0*.doc;*.docx\0All Files (*.*)\0*.*\0\0",
                "excel2pdf" => "Excel Files (*.xlsx; *.xls)\0*.xlsx;*.xls\0All Files (*.*)\0*.*\0\0",
                "merge-pdf" => "PDF Files (*.pdf)\0*.pdf\0All Files (*.*)\0*.*\0\0",
                "translate-pdf" => "PDF Files (*.pdf)\0*.pdf\0All Files (*.*)\0*.*\0\0",
                "decrypt-pdf" => "PDF Files (*.pdf)\0*.pdf\0All Files (*.*)\0*.*\0\0",
                _ => "Image Files (*.jpg; *.jpeg; *.png; *.bmp; *.gif; *.tiff; *.webp)\0*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp\0All Files (*.*)\0*.*\0\0"
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

            int cardW = (zoneW - 2 * 12) / 3;
            for (int i = 0; i < 8; i++)
            {
                int col = i % 3;
                int row = i / 3;
                int cardX = (int)contentX + col * (cardW + 12);
                int cardY = 230 + row * 50;
                int cardH = 40;

                bool isSelected = _convertCommandIndex == i;
                bool isHovered = _hoveredElement == (50 + i);
                bool isEnabled = ValidateConvertFiles(ConvertCommands[i], _selectedFiles, out _);

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

                string cmdKey = i switch
                {
                    0 => "cmd_ppt_to_pdf",
                    1 => "cmd_word_to_pdf",
                    2 => "cmd_excel_to_pdf",
                    3 => "cmd_merge_pdf",
                    4 => "cmd_img_to_pdf",
                    5 => "cmd_merge_img",
                    6 => "cmd_stitch_img",
                    7 => "cmd_translate_pdf",
                    8 => "cmd_decrypt_pdf",
                    _ => ""
                };
                string cmdText = GetText(cmdKey);
                
                if (_tabFont != null)
                {
                    using var textBrush = new SolidBrush(textColor);
                    var size = g.MeasureString(cmdText, _tabFont);
                    g.DrawString(cmdText, _tabFont, textBrush, (cardX + (cardW - size.Width / s) / 2) * s, (cardY + (cardH - size.Height / s) / 2) * s);
                }
            }

            int buttonY = 390;
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

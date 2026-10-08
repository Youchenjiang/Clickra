using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Clickra.Core.Processors;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI;

    public partial class ProgressWindow
    {
        private const float MarkdownOptionLeft = 36f;
        private const float MarkdownControlLeft = 154f;
        private const float MarkdownControlWidth = 330f;
        private const float MarkdownOptionButtonHeight = 30f;

        private int _markdownStyleIndex = 0;
        private int _markdownPaperIndex = 0;
        private int _markdownTextSizeIndex = 1;
        private int _markdownCodeThemeIndex = 0;
        private int _markdownLayoutSourceIndex = 0;
        private string? _markdownTemplatePath;

        private int GetMarkdownOptionsClientHeight() => 442;

        private void PaintMarkdownOptions(Graphics g, float s)
        {
            if (_msgFont is null || _tipFont is null) return;

            using var bodyBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
            using var mutedBrush = new SolidBrush(Color.FromArgb(145, 145, 145));
            g.DrawString(Loc("md_options_hint"), _tipFont, mutedBrush,
                new RectangleF(36 * s, 126 * s, 448 * s, 36 * s));

            PaintCompactMarkdownOptionRow(g, s, Loc("md_options_layout_source"), 168,
                new[] { Loc("md_options_layout_clickra"), Loc("md_options_layout_word") }, _markdownLayoutSourceIndex);

            if (_markdownLayoutSourceIndex == 0)
            {
                PaintCompactMarkdownOptionRow(g, s, Loc("md_options_style"), 210,
                    new[] { Loc("md_options_style_default"), Loc("md_options_style_minimal"), Loc("md_options_style_academic") },
                    _markdownStyleIndex);
            }
            else
            {
                DrawMarkdownRowLabel(g, s, Loc("md_options_template"), 210, bodyBrush);
                DrawMarkdownButton(g, s, new RectangleF(MarkdownControlLeft, 210, 132, 30), Loc("md_options_template_browse"), false);
                using var statusFormat = new StringFormat
                {
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                };
                g.DrawString(_markdownTemplateStatus, _tipFont, mutedBrush,
                    new RectangleF(296 * s, 210 * s, 188 * s, 30 * s), statusFormat);
            }

            PaintCompactMarkdownOptionRow(g, s, Loc("md_options_paper"), 252,
                new[] { "A4", "Letter" }, _markdownPaperIndex);
            PaintCompactMarkdownOptionRow(g, s, Loc("md_options_text_size"), 294,
                new[] { Loc("md_options_text_small"), Loc("md_options_text_standard"), Loc("md_options_text_large") },
                _markdownTextSizeIndex);
            PaintCompactMarkdownOptionRow(g, s, Loc("md_options_code_theme"), 336,
                new[] { Loc("md_options_code_dark"), Loc("md_options_code_light") }, _markdownCodeThemeIndex);

            using var separatorPen = new Pen(Color.FromArgb(58, 58, 58), 1f * s);
            g.DrawLine(separatorPen, 36 * s, 384 * s, 484 * s, 384 * s);
            DrawMarkdownButton(g, s, new RectangleF(254, 398, 120, 32), Loc("md_options_convert"), true);
            DrawMarkdownButton(g, s, new RectangleF(386, 398, 98, 32), Loc("dialog_cancel"), false);
        }

        private void PaintCompactMarkdownOptionRow(Graphics g, float s, string label, float y, string[] values, int selectedIndex)
        {
            if (_msgFont is null) return;
            using var labelBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
            DrawMarkdownRowLabel(g, s, label, y, labelBrush);

            float gap = 6;
            float width = (MarkdownControlWidth - gap * (values.Length - 1)) / values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                var rect = new RectangleF(MarkdownControlLeft + i * (width + gap), y, width, MarkdownOptionButtonHeight);
                DrawMarkdownButton(g, s, rect, values[i], i == selectedIndex);
            }
        }

        private void DrawMarkdownRowLabel(Graphics g, float s, string label, float y, Brush brush)
        {
            if (_msgFont is null) return;
            using var format = new StringFormat { LineAlignment = StringAlignment.Center };
            g.DrawString(label, _msgFont, brush,
                new RectangleF(MarkdownOptionLeft * s, y * s, (MarkdownControlLeft - MarkdownOptionLeft - 12) * s,
                    MarkdownOptionButtonHeight * s), format);
        }

        private void DrawMarkdownButton(Graphics g, float s, RectangleF logicalRect, string text, bool selected)
        {
            var rect = new RectangleF(logicalRect.X * s, logicalRect.Y * s, logicalRect.Width * s, logicalRect.Height * s);
            using var path = UIHelper.GetRoundedRectPath(rect, 5 * s);
            Color accent = UIHelper.GetSystemColorizationColor();
            using var fill = new SolidBrush(selected ? accent : Color.FromArgb(45, 45, 45));
            using var pen = new Pen(selected ? UIHelper.Lighten(accent, 0.18f) : Color.FromArgb(74, 74, 74), 1f * s);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);

            Font font = _tipFont ?? _msgFont!;
            using var textBrush = new SolidBrush(selected ? Color.White : Color.FromArgb(225, 225, 225));
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, textBrush, rect, format);
        }

        private bool HandleMarkdownOptionsClick(IntPtr hwnd, int mouseX, int mouseY)
        {
            if (TryHitCompactMarkdownOptionRow(mouseX, mouseY, 168, 2, out int source))
            {
                _markdownLayoutSourceIndex = source;
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return true;
            }

            if (_markdownLayoutSourceIndex == 0)
            {
                if (TryHitCompactMarkdownOptionRow(mouseX, mouseY, 210, 3, out int style))
                {
                    _markdownStyleIndex = style;
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                    return true;
                }
            }
            else if (ContainsMarkdownRect(mouseX, mouseY, new RectangleF(MarkdownControlLeft, 210, 132, 30)))
            {
                BrowseMarkdownTemplate(hwnd);
                return true;
            }

            if (TryHitCompactMarkdownOptionRow(mouseX, mouseY, 252, 2, out int paper))
            {
                _markdownPaperIndex = paper;
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return true;
            }
            if (TryHitCompactMarkdownOptionRow(mouseX, mouseY, 294, 3, out int textSize))
            {
                _markdownTextSizeIndex = textSize;
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return true;
            }
            if (TryHitCompactMarkdownOptionRow(mouseX, mouseY, 336, 2, out int codeTheme))
            {
                _markdownCodeThemeIndex = codeTheme;
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return true;
            }

            if (ContainsMarkdownRect(mouseX, mouseY, new RectangleF(254, 398, 120, 32)))
                return TryConfirmMarkdownConversion(hwnd);
            if (ContainsMarkdownRect(mouseX, mouseY, new RectangleF(386, 398, 98, 32)))
            {
                ResolveMarkdownDecision(false);
                DestroyWindow(hwnd);
                return true;
            }
            return true;
        }

        private bool TryConfirmMarkdownConversion(IntPtr hwnd)
        {
            if (_markdownLayoutSourceIndex == 1 && string.IsNullOrWhiteSpace(_markdownTemplatePath))
            {
                _markdownTemplateStatus = Loc("md_options_template_required");
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return true;
            }

            if (_markdownLayoutSourceIndex == 1)
            {
                try
                {
                    _ = MarkdownTemplateSource.Load(_markdownTemplatePath!);
                }
                catch
                {
                    _markdownTemplatePath = null;
                    _markdownTemplateStatus = Loc("md_options_template_invalid");
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                    return true;
                }
            }

            StartMarkdownConversion(hwnd, _markdownLayoutSourceIndex == 1 ? _markdownTemplatePath : null);
            return true;
        }

        private void StartMarkdownConversion(IntPtr hwnd, string? templatePath)
        {
            _commandOptions = MarkdownPdfOptions.Create(
                _markdownStyleIndex switch
                {
                    1 => MarkdownPdfOptions.ThemeMinimal,
                    2 => MarkdownPdfOptions.ThemeAcademic,
                    _ => MarkdownPdfOptions.ThemeDefault
                },
                _markdownPaperIndex == 1 ? MarkdownPdfOptions.PaperLetter : MarkdownPdfOptions.PaperA4,
                _markdownTextSizeIndex switch
                {
                    0 => MarkdownPdfOptions.TextSmall,
                    2 => MarkdownPdfOptions.TextLarge,
                    _ => MarkdownPdfOptions.TextStandard
                },
                _markdownCodeThemeIndex == 1 ? MarkdownPdfOptions.CodeLight : MarkdownPdfOptions.CodeDark,
                templatePath);
            ResolveMarkdownDecision(true);
            _isPromptingMarkdownOptions = false;
            ResizeWindowForMarkdownOptions(hwnd, false);
            StartProcessingThread(hwnd);
        }

        private static bool TryHitCompactMarkdownOptionRow(int mouseX, int mouseY, float y, int count, out int index)
        {
            index = -1;
            if (mouseY < y || mouseY > y + MarkdownOptionButtonHeight) return false;
            const float gap = 6;
            float width = (MarkdownControlWidth - gap * (count - 1)) / count;
            for (int i = 0; i < count; i++)
            {
                float x = MarkdownControlLeft + i * (width + gap);
                if (mouseX >= x && mouseX <= x + width)
                {
                    index = i;
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsMarkdownRect(int x, int y, RectangleF rect) =>
            x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;

        private void BrowseMarkdownTemplate(IntPtr owner)
        {
            const int maxFile = 32768;
            IntPtr fileBuffer = Marshal.AllocHGlobal(maxFile * 2);
            try
            {
                Marshal.Copy(new byte[maxFile * 2], 0, fileBuffer, maxFile * 2);
                var ofn = new OPENFILENAME
                {
                    lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                    hwndOwner = owner,
                    lpstrFilter = "Word Template (*.docx)\0*.docx\0\0",
                    lpstrFile = fileBuffer,
                    nMaxFile = maxFile,
                    lpstrTitle = Loc("md_options_template_picker"),
                    lpstrDefExt = "docx",
                    Flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000004
                };
                if (!GetOpenFileName(ref ofn)) return;

                string? selectedPath = Marshal.PtrToStringUni(fileBuffer);
                if (string.IsNullOrWhiteSpace(selectedPath)) return;
                try
                {
                    _ = MarkdownTemplateSource.Load(selectedPath);
                    _markdownTemplatePath = selectedPath;
                    _markdownTemplateStatus = string.Format(Loc("md_options_template_selected"), Path.GetFileName(selectedPath));
                }
                catch
                {
                    _markdownTemplatePath = null;
                    _markdownTemplateStatus = Loc("md_options_template_invalid");
                }
                InvalidateRect(owner, IntPtr.Zero, false);
            }
            finally
            {
                Marshal.FreeHGlobal(fileBuffer);
            }
        }

        private void ResizeWindowForMarkdownOptions(IntPtr hwnd, bool expand)
        {
            float s = _dpiScale;
            int clientW = (int)(520 * s);
            int clientH = (int)((expand ? GetMarkdownOptionsClientHeight() : 280) * s);

            _bufferGraphics?.Dispose();
            _bufferBmp?.Dispose();
            _bufferBmp = new Bitmap(clientW, clientH);
            _bufferGraphics = Graphics.FromImage(_bufferBmp);
            _bufferGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            _bufferGraphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var rect = new RECT { left = 0, top = 0, right = clientW, bottom = clientH };
            AdjustWindowRectEx(ref rect, WS_OVERLAPPED_FIXED, false, 0);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, rect.right - rect.left, rect.bottom - rect.top, 0x0002 | 0x0004);
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }
    }

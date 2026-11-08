using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using Clickra.UI.Layout;
using Clickra.UI.Native;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI;

/// <summary>
/// Encapsulates the common graphics context, DPI scale, and accent color for modern UI controls.
/// </summary>
public readonly record struct ModernPaintContext(Graphics Graphics, float Scale, Color Accent);

/// <summary>
/// Reusable Win32 Fluent / Modern dark theme UI control primitives.
/// Provides pixel-perfect scaled rendering for text inputs, collapsible drawers,
/// checkboxes, segmented pill toggles, action buttons, dialogs, and text compaction.
/// </summary>
public static class ModernControls
{
    public static RectangleF ScaleRect(LayoutRect rect, float scale) =>
        new(rect.X * scale, rect.Y * scale, rect.Width * scale, rect.Height * scale);

    public static IntPtr CreateSingleEdit(IntPtr parent, LayoutRect rect, string defaultText, IntPtr font, float scale)
    {
        var r = ScaleRect(rect, scale);
        int editX = (int)(r.X + (3 * scale));
        int editY = (int)(r.Y + (3 * scale));
        int editW = (int)(r.Width - (6 * scale));
        int editH = (int)(r.Height - (6 * scale));

        IntPtr edit = CreateWindowEx(
            0,
            "EDIT",
            defaultText,
            0x50000080 | 0x0080, // WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL
            editX, editY, editW, editH,
            parent, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        if (font != IntPtr.Zero)
        {
            SendMessageW(edit, 0x0030, font, (IntPtr)1); // WM_SETFONT
        }

        // EM_SETMARGINS (0x00D3): set left/right padding so text is not pressed against edge
        int margin = (int)(4 * scale);
        SendMessageW(edit, 0x00D3, (IntPtr)(0x0001 | 0x0002), (IntPtr)((margin << 16) | margin));

        return edit;
    }

    public static void SetCueBanner(IntPtr editHwnd, string cueText)
    {
        if (editHwnd == IntPtr.Zero || string.IsNullOrEmpty(cueText)) return;
        IntPtr pStr = Marshal.StringToHGlobalUni(cueText);
        try
        {
            SendMessageW(editHwnd, 0x1501, IntPtr.Zero, pStr); // EM_SETCUEBANNER (0 = hide on focus)
        }
        finally
        {
            Marshal.FreeHGlobal(pStr);
        }
    }

    public static void DrawTextBoxContainer(ModernPaintContext ctx, LayoutRect logicalRect, IntPtr editHwnd, IntPtr focusedHwnd, bool enabled = true)
    {
        bool isFocused = enabled && editHwnd != IntPtr.Zero && focusedHwnd == editHwnd;
        DrawTextBoxContainer(ctx, logicalRect, isFocused, enabled);
    }

    public static void DrawTextBoxContainer(ModernPaintContext ctx, LayoutRect logicalRect, bool isFocused, bool enabled = true)
    {
        var r = ScaleRect(logicalRect, ctx.Scale);
        using var path = UIHelper.GetRoundedRectPath(r, 4 * ctx.Scale);

        using var bgBrush = new SolidBrush(enabled ? Color.FromArgb(45, 45, 45) : Color.FromArgb(30, 30, 30));
        ctx.Graphics.FillPath(bgBrush, path);

        Color borderCol = GetTextBoxBorderColor(enabled, isFocused, ctx.Accent);
        float penW = isFocused ? 1.5f * ctx.Scale : 1.0f * ctx.Scale;
        using var pen = new Pen(borderCol, penW);
        ctx.Graphics.DrawPath(pen, path);

        if (isFocused)
        {
            using var linePen = new Pen(ctx.Accent, 2.0f * ctx.Scale);
            ctx.Graphics.DrawLine(linePen, r.X + (3 * ctx.Scale), r.Bottom - (1f * ctx.Scale), r.Right - (3 * ctx.Scale), r.Bottom - (1f * ctx.Scale));
        }
    }

    private static Color GetTextBoxBorderColor(bool enabled, bool isFocused, Color accent)
    {
        if (!enabled) return Color.FromArgb(48, 48, 52);
        return isFocused ? accent : Color.FromArgb(68, 68, 76);
    }

    public static void DrawCollapsibleHeader(ModernPaintContext ctx, LayoutRect logicalRect, string title, bool isExpanded, string? badgeText, Font font)
    {
        var rect = ScaleRect(logicalRect, ctx.Scale);
        using var path = UIHelper.GetRoundedRectPath(rect, 4 * ctx.Scale);

        using var bgBrush = new SolidBrush(Color.FromArgb(36, 36, 42));
        using var borderPen = new Pen(Color.FromArgb(58, 58, 66), 1f * ctx.Scale);
        ctx.Graphics.FillPath(bgBrush, path);
        ctx.Graphics.DrawPath(borderPen, path);

        string chevron = isExpanded ? "▾  " : "▸  ";
        string fullText = chevron + title;

        using var textBrush = new SolidBrush(Color.FromArgb(215, 215, 225));
        using var sf = new StringFormat { LineAlignment = StringAlignment.Center };
        var textRect = new RectangleF(rect.X + (10 * ctx.Scale), rect.Y, rect.Width - (20 * ctx.Scale), rect.Height);
        ctx.Graphics.DrawString(fullText, font, textBrush, textRect, sf);

        if (!isExpanded && !string.IsNullOrEmpty(badgeText))
        {
            DrawHeaderBadge(ctx, rect, font, badgeText);
        }
    }

    private static void DrawHeaderBadge(ModernPaintContext ctx, RectangleF rect, Font font, string badgeText)
    {
        var badgeSize = ctx.Graphics.MeasureString(badgeText, font);
        float badgeW = badgeSize.Width + (12 * ctx.Scale);
        float badgeH = 18 * ctx.Scale;
        float badgeX = rect.Right - badgeW - (8 * ctx.Scale);
        float badgeY = rect.Y + (rect.Height - badgeH) / 2f;
        var badgeRect = new RectangleF(badgeX, badgeY, badgeW, badgeH);

        using var badgePath = UIHelper.GetRoundedRectPath(badgeRect, 3 * ctx.Scale);
        using var badgeBrush = new SolidBrush(ctx.Accent);
        ctx.Graphics.FillPath(badgeBrush, badgePath);

        using var badgeSf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var badgeFont = new Font(font.FontFamily, 10f * ctx.Scale, FontStyle.Bold, GraphicsUnit.Pixel);
        ctx.Graphics.DrawString(badgeText, badgeFont, Brushes.White, badgeRect, badgeSf);
    }

    public static void DrawCheckBox(ModernPaintContext ctx, LayoutRect logicalRect, bool isChecked, string text, Font font, Brush textBrush, bool enabled = true)
    {
        var r = ScaleRect(logicalRect, ctx.Scale);
        float boxSize = 16f * ctx.Scale;
        var boxRect = new RectangleF(r.X, r.Y + ((r.Height - boxSize) / 2f), boxSize, boxSize);

        using var path = UIHelper.GetRoundedRectPath(boxRect, 3.5f * ctx.Scale);

        if (isChecked)
        {
            using var fill = new SolidBrush(enabled ? ctx.Accent : Color.FromArgb(50, 50, 56));
            using var borderPen = new Pen(enabled ? UIHelper.Lighten(ctx.Accent, 0.2f) : Color.FromArgb(70, 70, 76), 1f * ctx.Scale);
            ctx.Graphics.FillPath(fill, path);
            ctx.Graphics.DrawPath(borderPen, path);

            var prevSmoothing = ctx.Graphics.SmoothingMode;
            ctx.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var checkPen = new Pen(enabled ? Color.White : Color.FromArgb(160, 160, 160), 2f * ctx.Scale)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            PointF p1 = new(boxRect.X + (3.5f * ctx.Scale), boxRect.Y + (8f * ctx.Scale));
            PointF p2 = new(boxRect.X + (6.5f * ctx.Scale), boxRect.Y + (11.5f * ctx.Scale));
            PointF p3 = new(boxRect.X + (12.5f * ctx.Scale), boxRect.Y + (4.5f * ctx.Scale));
            ctx.Graphics.DrawLines(checkPen, new[] { p1, p2, p3 });
            ctx.Graphics.SmoothingMode = prevSmoothing;
        }
        else
        {
            using var fill = new SolidBrush(Color.FromArgb(36, 36, 40));
            using var borderPen = new Pen(Color.FromArgb(88, 88, 96), 1.2f * ctx.Scale);
            ctx.Graphics.FillPath(fill, path);
            ctx.Graphics.DrawPath(borderPen, path);
        }

        float textX = boxRect.Right + (8f * ctx.Scale);
        float textY = r.Y + ((r.Height - font.GetHeight(ctx.Graphics)) / 2f);
        ctx.Graphics.DrawString(text, font, textBrush, textX, textY);
    }

    public static void DrawSegmentedToggle(ModernPaintContext ctx, LayoutRect logicalRect, string leftText, string rightText, bool isLeftSelected, Font font, bool enabled = true)
    {
        DrawMultiSegmentedToggle(ctx, logicalRect, new[] { leftText, rightText }, isLeftSelected ? 0 : 1, font, enabled);
    }

    public static void DrawMultiSegmentedToggle(ModernPaintContext ctx, LayoutRect logicalRect, string[] labels, int selectedIndex, Font font, bool enabled = true)
    {
        var r = ScaleRect(logicalRect, ctx.Scale);
        DrawToggleContainer(ctx.Graphics, ctx.Scale, r, enabled);

        if (labels == null || labels.Length == 0) return;

        float segW = r.Width / labels.Length;
        DrawSelectedSegment(ctx, r, segW, labels.Length, selectedIndex, enabled);
        DrawSegmentDividers(ctx.Graphics, ctx.Scale, r, segW, labels.Length, selectedIndex);
        DrawSegmentLabels(ctx.Graphics, r, segW, labels, selectedIndex, font, enabled);
    }

    private static void DrawToggleContainer(Graphics g, float scale, RectangleF r, bool enabled)
    {
        using var containerPath = UIHelper.GetRoundedRectPath(r, 4 * scale);
        using var containerFill = new SolidBrush(enabled ? Color.FromArgb(32, 32, 36) : Color.FromArgb(24, 24, 28));
        using var containerPen = new Pen(enabled ? Color.FromArgb(65, 65, 72) : Color.FromArgb(44, 44, 48), 1f * scale);
        g.FillPath(containerFill, containerPath);
        g.DrawPath(containerPen, containerPath);
    }

    private static void DrawSelectedSegment(ModernPaintContext ctx, RectangleF r, float segW, int count, int selectedIndex, bool enabled)
    {
        if (selectedIndex < 0 || selectedIndex >= count) return;
        var selectedRect = new RectangleF(r.X + (selectedIndex * segW) + (1 * ctx.Scale), r.Y + (1 * ctx.Scale), segW - (2 * ctx.Scale), r.Height - (2 * ctx.Scale));
        using var selPath = UIHelper.GetRoundedRectPath(selectedRect, 3 * ctx.Scale);
        Color selColor = enabled ? ctx.Accent : Color.FromArgb(48, 48, 54);
        using var selFill = new SolidBrush(selColor);
        ctx.Graphics.FillPath(selFill, selPath);
    }

    private static void DrawSegmentDividers(Graphics g, float scale, RectangleF r, float segW, int count, int selectedIndex)
    {
        using var divPen = new Pen(Color.FromArgb(50, 50, 56), 1f * scale);
        for (int i = 1; i < count; i++)
        {
            if (i != selectedIndex && i != (selectedIndex + 1))
            {
                float divX = r.X + (i * segW);
                g.DrawLine(divPen, divX, r.Y + (4 * scale), divX, r.Bottom - (4 * scale));
            }
        }
    }

    private static void DrawSegmentLabels(Graphics g, RectangleF r, float segW, string[] labels, int selectedIndex, Font font, bool enabled)
    {
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < labels.Length; i++)
        {
            bool isSel = i == selectedIndex;
            Color textCol = GetToggleTextColor(enabled, isSel);
            using var brush = new SolidBrush(textCol);
            var segRect = new RectangleF(r.X + (i * segW), r.Y, segW, r.Height);
            g.DrawString(labels[i], font, brush, segRect, sf);
        }
    }

    private static Color GetToggleTextColor(bool enabled, bool isSel)
    {
        if (!enabled) return Color.FromArgb(95, 95, 95);
        return isSel ? Color.White : Color.FromArgb(170, 170, 170);
    }

    public static void DrawButton(ModernPaintContext ctx, LayoutRect logicalRect, string text, Font font, bool isSelectedOrAccent, bool enabled = true, bool isDestructive = false)
    {
        var rect = ScaleRect(logicalRect, ctx.Scale);
        using var path = UIHelper.GetRoundedRectPath(rect, 4 * ctx.Scale);

        var colors = GetButtonColors(ctx.Accent, enabled, isSelectedOrAccent, isDestructive);

        using var fill = new SolidBrush(colors.bg);
        using var pen = new Pen(colors.border, 1f * ctx.Scale);
        ctx.Graphics.FillPath(fill, path);
        ctx.Graphics.DrawPath(pen, path);

        using var textBrush = new SolidBrush(colors.text);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        ctx.Graphics.DrawString(text, font, textBrush, rect, sf);
    }

    private static (Color bg, Color border, Color text) GetButtonColors(Color accent, bool enabled, bool isSelectedOrAccent, bool isDestructive)
    {
        if (!enabled)
        {
            return (Color.FromArgb(34, 34, 38), Color.FromArgb(46, 46, 50), Color.FromArgb(100, 100, 100));
        }
        if (isDestructive)
        {
            return (Color.FromArgb(42, 28, 30), Color.FromArgb(135, 50, 55), Color.FromArgb(245, 175, 175));
        }
        if (isSelectedOrAccent)
        {
            return (accent, UIHelper.Lighten(accent, 0.2f), Color.White);
        }
        return (Color.FromArgb(46, 46, 52), Color.FromArgb(70, 70, 78), Color.White);
    }

    public static string CompactMiddle(string text, Font font, float maxW, Graphics g, StringFormat sf)
    {
        if (string.IsNullOrEmpty(text)) return "";
        float measuredW = g.MeasureString(text, font, 10000, sf).Width;
        if (measuredW <= maxW) return text;

        string ellipsis = "...";
        float ellipsisW = g.MeasureString(ellipsis, font, 10000, sf).Width;
        if (ellipsisW >= maxW) return ellipsis;

        int left = 1;
        int right = 1;
        string result = text;

        while (left + right < text.Length)
        {
            string candidate = text[..left] + ellipsis + text[^right..];
            float w = g.MeasureString(candidate, font, 10000, sf).Width;
            if (w > maxW) break;

            result = candidate;
            if (left <= right) left++;
            else right++;
        }

        return result;
    }

    public static List<string> SelectOpenFiles(IntPtr hwndOwner, string title, string filter)
    {
        var files = new List<string>();
        var ofn = new OPENFILENAME();
        ofn.lStructSize = Marshal.SizeOf(ofn);
        ofn.hwndOwner = hwndOwner;
        ofn.lpstrFilter = filter;

        int maxFile = 65536;
        IntPtr fileBuffer = Marshal.AllocHGlobal(maxFile * 2);
        try
        {
            byte[] zeros = new byte[maxFile * 2];
            Marshal.Copy(zeros, 0, fileBuffer, zeros.Length);

            ofn.lpstrFile = fileBuffer;
            ofn.nMaxFile = maxFile;
            ofn.lpstrTitle = title;
            ofn.Flags = 0x00080000 | 0x00000200 | 0x00001000 | 0x00000004;

            if (GetOpenFileName(ref ofn))
            {
                files.AddRange(ParseOpenFileNameBuffer(fileBuffer));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
        }

        return files;
    }

    private static List<string> ParseOpenFileNameBuffer(IntPtr fileBuffer)
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

        if (paths.Count <= 1)
        {
            return paths;
        }

        string dir = paths[0];
        var files = new List<string>(paths.Count - 1);
        for (int i = 1; i < paths.Count; i++)
        {
            files.Add(Path.Combine(dir, paths[i]));
        }

        return files;
    }
}

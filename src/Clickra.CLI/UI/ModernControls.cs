using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using Clickra.UI.Layout;
using Clickra.UI.Native;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI;

/// <summary>
/// Reusable Win32 Fluent / Modern dark theme UI control primitives.
/// Provides pixel-perfect scaled rendering for text inputs, collapsible drawers,
/// checkboxes, segmented pill toggles, action buttons, and text compaction.
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

    public static void DrawTextBoxContainer(Graphics g, float scale, LayoutRect logicalRect, IntPtr editHwnd, IntPtr focusedHwnd, Color accent, bool enabled = true)
    {
        bool isFocused = enabled && editHwnd != IntPtr.Zero && focusedHwnd == editHwnd;
        DrawTextBoxContainer(g, scale, logicalRect, isFocused, accent, enabled);
    }

    public static void DrawTextBoxContainer(Graphics g, float scale, LayoutRect logicalRect, bool isFocused, Color accent, bool enabled = true)
    {
        var r = ScaleRect(logicalRect, scale);
        using var path = UIHelper.GetRoundedRectPath(r, 4 * scale);

        using var bgBrush = new SolidBrush(enabled ? Color.FromArgb(45, 45, 45) : Color.FromArgb(30, 30, 30));
        g.FillPath(bgBrush, path);

        Color borderCol = !enabled
            ? Color.FromArgb(48, 48, 52)
            : (isFocused ? accent : Color.FromArgb(68, 68, 76));
        float penW = isFocused ? 1.5f * scale : 1.0f * scale;
        using var pen = new Pen(borderCol, penW);
        g.DrawPath(pen, path);

        if (isFocused)
        {
            using var linePen = new Pen(accent, 2.0f * scale);
            g.DrawLine(linePen, r.X + (3 * scale), r.Bottom - (1f * scale), r.Right - (3 * scale), r.Bottom - (1f * scale));
        }
    }

    public static void DrawCollapsibleHeader(Graphics g, float scale, LayoutRect logicalRect, string title, bool isExpanded, bool hasActiveBadge, string? badgeText, Font font, Color accent)
    {
        var rect = ScaleRect(logicalRect, scale);
        using var path = UIHelper.GetRoundedRectPath(rect, 4 * scale);

        using var bgBrush = new SolidBrush(Color.FromArgb(36, 36, 42));
        using var borderPen = new Pen(Color.FromArgb(58, 58, 66), 1f * scale);
        g.FillPath(bgBrush, path);
        g.DrawPath(borderPen, path);

        string chevron = isExpanded ? "▾  " : "▸  ";
        string fullText = chevron + title;

        using var textBrush = new SolidBrush(Color.FromArgb(215, 215, 225));
        using var sf = new StringFormat { LineAlignment = StringAlignment.Center };
        var textRect = new RectangleF(rect.X + (10 * scale), rect.Y, rect.Width - (20 * scale), rect.Height);
        g.DrawString(fullText, font, textBrush, textRect, sf);

        if (hasActiveBadge && !isExpanded && !string.IsNullOrEmpty(badgeText))
        {
            var badgeSize = g.MeasureString(badgeText, font);
            float badgeW = badgeSize.Width + (12 * scale);
            float badgeH = 18 * scale;
            float badgeX = rect.Right - badgeW - (8 * scale);
            float badgeY = rect.Y + (rect.Height - badgeH) / 2f;
            var badgeRect = new RectangleF(badgeX, badgeY, badgeW, badgeH);

            using var badgePath = UIHelper.GetRoundedRectPath(badgeRect, 3 * scale);
            using var badgeBrush = new SolidBrush(accent);
            g.FillPath(badgeBrush, badgePath);

            using var badgeSf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var badgeFont = new Font(font.FontFamily, 10f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString(badgeText, badgeFont, Brushes.White, badgeRect, badgeSf);
        }
    }

    public static void DrawCheckBox(Graphics g, float scale, LayoutRect logicalRect, bool isChecked, string text, Font font, Brush textBrush, Color accent, bool enabled = true)
    {
        var r = ScaleRect(logicalRect, scale);
        float boxSize = 16f * scale;
        var boxRect = new RectangleF(r.X, r.Y + ((r.Height - boxSize) / 2f), boxSize, boxSize);

        using var path = UIHelper.GetRoundedRectPath(boxRect, 3.5f * scale);

        if (isChecked)
        {
            using var fill = new SolidBrush(enabled ? accent : Color.FromArgb(50, 50, 56));
            using var borderPen = new Pen(enabled ? UIHelper.Lighten(accent, 0.2f) : Color.FromArgb(70, 70, 76), 1f * scale);
            g.FillPath(fill, path);
            g.DrawPath(borderPen, path);

            var prevSmoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var checkPen = new Pen(enabled ? Color.White : Color.FromArgb(160, 160, 160), 2f * scale)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            PointF p1 = new(boxRect.X + (3.5f * scale), boxRect.Y + (8f * scale));
            PointF p2 = new(boxRect.X + (6.5f * scale), boxRect.Y + (11.5f * scale));
            PointF p3 = new(boxRect.X + (12.5f * scale), boxRect.Y + (4.5f * scale));
            g.DrawLines(checkPen, new[] { p1, p2, p3 });
            g.SmoothingMode = prevSmoothing;
        }
        else
        {
            using var fill = new SolidBrush(Color.FromArgb(36, 36, 40));
            using var borderPen = new Pen(Color.FromArgb(88, 88, 96), 1.2f * scale);
            g.FillPath(fill, path);
            g.DrawPath(borderPen, path);
        }

        float textX = boxRect.Right + (8f * scale);
        float textY = r.Y + ((r.Height - font.GetHeight(g)) / 2f);
        g.DrawString(text, font, textBrush, textX, textY);
    }

    public static void DrawSegmentedToggle(Graphics g, float scale, LayoutRect logicalRect, string leftText, string rightText, bool isLeftSelected, Font font, Color accent, bool enabled = true)
    {
        DrawMultiSegmentedToggle(g, scale, logicalRect, new[] { leftText, rightText }, isLeftSelected ? 0 : 1, font, accent, enabled);
    }

    public static void DrawMultiSegmentedToggle(Graphics g, float scale, LayoutRect logicalRect, string[] labels, int selectedIndex, Font font, Color accent, bool enabled = true)
    {
        var r = ScaleRect(logicalRect, scale);
        using var containerPath = UIHelper.GetRoundedRectPath(r, 4 * scale);
        using var containerFill = new SolidBrush(enabled ? Color.FromArgb(32, 32, 36) : Color.FromArgb(24, 24, 28));
        using var containerPen = new Pen(enabled ? Color.FromArgb(65, 65, 72) : Color.FromArgb(44, 44, 48), 1f * scale);
        g.FillPath(containerFill, containerPath);
        g.DrawPath(containerPen, containerPath);

        if (labels == null || labels.Length == 0) return;

        float segW = r.Width / labels.Length;
        if (selectedIndex >= 0 && selectedIndex < labels.Length)
        {
            var selectedRect = new RectangleF(r.X + (selectedIndex * segW) + (1 * scale), r.Y + (1 * scale), segW - (2 * scale), r.Height - (2 * scale));
            using var selPath = UIHelper.GetRoundedRectPath(selectedRect, 3 * scale);
            Color selColor = enabled ? accent : Color.FromArgb(48, 48, 54);
            using var selFill = new SolidBrush(selColor);
            g.FillPath(selFill, selPath);
        }

        // Dividers between non-selected segments
        using var divPen = new Pen(Color.FromArgb(50, 50, 56), 1f * scale);
        for (int i = 1; i < labels.Length; i++)
        {
            if (i != selectedIndex && i != (selectedIndex + 1))
            {
                float divX = r.X + (i * segW);
                g.DrawLine(divPen, divX, r.Y + (4 * scale), divX, r.Bottom - (4 * scale));
            }
        }

        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < labels.Length; i++)
        {
            bool isSel = i == selectedIndex;
            Color textCol = !enabled
                ? Color.FromArgb(95, 95, 95)
                : (isSel ? Color.White : Color.FromArgb(170, 170, 170));
            using var brush = new SolidBrush(textCol);
            var segRect = new RectangleF(r.X + (i * segW), r.Y, segW, r.Height);
            g.DrawString(labels[i], font, brush, segRect, sf);
        }
    }

    public static void DrawButton(Graphics g, float scale, LayoutRect logicalRect, string text, Font font, bool isSelectedOrAccent, bool enabled = true, bool isDestructive = false)
    {
        var rect = ScaleRect(logicalRect, scale);
        using var path = UIHelper.GetRoundedRectPath(rect, 4 * scale);

        Color accent = UIHelper.GetSystemColorizationColor();
        Color bg = !enabled
            ? Color.FromArgb(34, 34, 38)
            : (isDestructive ? Color.FromArgb(42, 28, 30) : (isSelectedOrAccent ? accent : Color.FromArgb(46, 46, 52)));
        Color border = !enabled
            ? Color.FromArgb(46, 46, 50)
            : (isDestructive ? Color.FromArgb(135, 50, 55) : (isSelectedOrAccent ? UIHelper.Lighten(accent, 0.2f) : Color.FromArgb(70, 70, 78)));

        using var fill = new SolidBrush(bg);
        using var pen = new Pen(border, 1f * scale);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        Color textCol = !enabled ? Color.FromArgb(100, 100, 100) : (isDestructive ? Color.FromArgb(245, 175, 175) : Color.White);
        using var textBrush = new SolidBrush(textCol);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, textBrush, rect, sf);
    }

    public static string CompactMiddle(string text, Font font, float maxW, Graphics g, StringFormat sf)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (g.MeasureString(text, font, 10000, sf).Width <= maxW) return text;

        string ext = Path.GetExtension(text) ?? "";
        string stem = Path.GetFileNameWithoutExtension(text) ?? text;
        if (stem.Length <= 4) return text;

        int low = 2;
        int high = stem.Length - 1;
        string best = text;

        while (low <= high)
        {
            int midLen = (low + high) / 2;
            int leftChars = Math.Max(1, (midLen * 5) / 8);
            int rightChars = Math.Max(1, midLen - leftChars);

            if (leftChars + rightChars >= stem.Length)
            {
                rightChars = Math.Max(1, stem.Length - leftChars - 1);
            }

            if (leftChars > 0 && rightChars > 0 && (leftChars + rightChars) < stem.Length)
            {
                string candidate = stem.Substring(0, leftChars) + "…" + stem.Substring(stem.Length - rightChars) + ext;
                if (g.MeasureString(candidate, font, 10000, sf).Width <= maxW)
                {
                    best = candidate;
                    low = midLen + 1;
                    continue;
                }
            }
            high = midLen - 1;
        }

        return best;
    }
}

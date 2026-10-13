namespace Clickra.UI.Layout;

/// <summary>Logical-pixel geometry shared by Markdown options painting and hit testing.</summary>
public static class MarkdownOptionsLayout
{
    public const int ClientHeight = 442;
    public const int ControlLeft = 154;
    public const int ControlWidth = 330;
    public const int ButtonHeight = 30;
    public const int LayoutSourceY = 168;
    public const int StyleOrTemplateY = 210;
    public const int PaperY = 252;
    public const int TextSizeY = 294;
    public const int CodeThemeY = 336;
    public const int SeparatorY = 384;

    public static LayoutRect HintRect => new(36, 126, 448, 36);
    public static LayoutRect LabelRect(int y) => new(36, y, ControlLeft - 36 - 12, ButtonHeight);
    public static LayoutRect RowButtonRect(int y, int count, int index)
    {
        const int gap = 6;
        int width = (ControlWidth - gap * (count - 1)) / count;
        return new LayoutRect(ControlLeft + index * (width + gap), y, width, ButtonHeight);
    }

    public static LayoutRect TemplateBrowseRect => new(ControlLeft, StyleOrTemplateY, 132, 30);
    public static LayoutRect TemplateStatusRect => new(296, StyleOrTemplateY, 188, 30);
    public static LayoutRect ConfirmRect => new(254, 398, 120, 32);
    public static LayoutRect CancelRect => new(386, 398, 98, 32);

    // Legacy Markdown click boundaries are inclusive at the right and bottom edges.
    // LayoutRect.Contains is intentionally half-open and would change existing behavior.
    public static bool ContainsInclusive(LayoutRect rect, int x, int y) =>
        x >= rect.X && x <= rect.Right && y >= rect.Y && y <= rect.Bottom;

    public static bool TryHitRow(int x, int y, int rowY, int count, out int index)
    {
        index = -1;
        if (y < rowY || y > rowY + ButtonHeight) return false;
        for (int i = 0; i < count; i++)
        {
            if (!ContainsInclusive(RowButtonRect(rowY, count, i), x, y)) continue;
            index = i;
            return true;
        }
        return false;
    }
}

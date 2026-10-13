namespace Clickra.UI.Layout;

/// <summary>Logical-pixel rectangles for the Native visual PDF splitter.</summary>
public static class VisualSplitterLayout
{
    public static LayoutRect ModeButton(int i) => new(36 + i * 148, 102, i == 2 ? 152 : 140, 26);
    public static LayoutRect ModeHitRow => new(0, 102, 520, 26);
    public static LayoutRect PagesButton(bool plus) => new(plus ? 136 : 36, 130, 24, 18);
    public static LayoutRect PagesHitRow => new(0, PagesButton(false).Y, 520, PagesButton(false).Height);
    public static int BodyTop(bool fixedPages) => 134 + (fixedPages ? 22 : 0);
    public static LayoutRect SegmentCard(int i, bool fixedPages) => new(42, BodyTop(fixedPages) + 24 + i * 23, 204, 20);
    // Cards have deliberate horizontal padding for easier selection; retain
    // the full historical card-list band including row gaps and empty slots.
    public static LayoutRect SegmentHitArea(bool fixedPages)
    {
        LayoutRect first = SegmentCard(0, fixedPages);
        return new(first.X - 6, first.Y, first.Width + 12, 374 - first.Y);
    }
    public static LayoutRect NavButton(int i, bool fixedPages) => new(i == 0 ? 266 : i == 1 ? 454 : 410, BodyTop(fixedPages) + 41, i == 2 ? 40 : 24, 20);
    public static LayoutRect NavHitArea(bool fixedPages)
    {
        LayoutRect previous = NavButton(0, fixedPages);
        return new(260, previous.Y - 5, 224, previous.Height + 10);
    }
    public static LayoutRect NavHitButton(int i, bool fixedPages)
    {
        LayoutRect drawn = NavButton(i, fixedPages);
        // Previous-page previously had an extra 2px of clickable width.
        // Keep the enlargement, derived from the drawn button, while
        // correcting the next-page leftward displacement.
        return i == 0
            ? new(drawn.X, drawn.Y - 5, drawn.Width + 2, drawn.Height + 10)
            : drawn.Inflate(0, 5);
    }
    public static LayoutRect PreviewArea(bool fixedPages)
    {
        LayoutRect nav = NavButton(0, fixedPages);
        int top = nav.Bottom + 4;
        return new(266, top, 212, 380 - 8 - top);
    }
    public static LayoutRect PreviewHitArea(bool fixedPages)
    {
        LayoutRect drawn = PreviewArea(fixedPages);
        // Retain a 2px bottom tolerance, without the old 1px top offset.
        return new(drawn.X, drawn.Y, drawn.Width, drawn.Height + 2);
    }
    public static LayoutRect ActionButton(int i) => new(i switch { 0 => 36, 1 => 138, 2 => 228, 3 => 336, _ => 416 }, 380, i switch { 0 => 96, 1 => 84, 2 => 84, 3 => 74, _ => 68 }, 26);
    public static LayoutRect ActionHitRow => new(0, 380, 520, 26);
    public static LayoutRect ZoomModal => new(24, 20, 472, 380);
    public static LayoutRect ZoomButton(int i) => new(i switch { 0 => 376, 1 => 410, _ => 444 }, 366, i == 2 ? 44 : 28, 22);
}

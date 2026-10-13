namespace Clickra.UI.Layout;

/// <summary>Logical-pixel rectangles for the Native visual PDF splitter.</summary>
public static class VisualSplitterLayout
{
    public static LayoutRect ModeButton(int i) => new(36 + i * 148, 102, i == 2 ? 152 : 140, 26);
    public static LayoutRect ModeHitRow => new(0, 102, 520, 26);
    public static LayoutRect PagesButton(bool plus) => new(plus ? 136 : 36, 130, 24, 18);
    public static LayoutRect PagesHitRow => new(0, 131, 520, 18);
    public static LayoutRect PagesHitButton(bool plus) => new(plus ? 132 : 36, 131, 24, 18);
    public static int BodyTop(bool fixedPages) => 134 + (fixedPages ? 22 : 0);
    public static LayoutRect SegmentCard(int i, bool fixedPages) => new(42, BodyTop(fixedPages) + 24 + i * 23, 204, 20);
    public static LayoutRect SegmentHitArea(bool fixedPages) => new(36, 158 + (fixedPages ? 22 : 0), 216, 374 - 158 - (fixedPages ? 22 : 0));
    public static LayoutRect NavButton(int i, bool fixedPages) => new(i == 0 ? 266 : i == 1 ? 454 : 410, BodyTop(fixedPages) + 41, i == 2 ? 40 : 24, 20);
    public static LayoutRect NavHitArea(bool fixedPages) => new(260, 170 + (fixedPages ? 22 : 0), 224, 30);
    public static LayoutRect NavHitButton(int i, bool fixedPages) => new(i == 0 ? 266 : i == 1 ? 452 : 410, 170 + (fixedPages ? 22 : 0), i == 2 ? 40 : 26, 30);
    public static LayoutRect PreviewHitArea(bool fixedPages) => new(266, 200 + (fixedPages ? 22 : 0), 212, 374 - 200 - (fixedPages ? 22 : 0));
    public static LayoutRect ActionButton(int i) => new(i switch { 0 => 36, 1 => 138, 2 => 228, 3 => 336, _ => 416 }, 380, i switch { 0 => 96, 1 => 84, 2 => 84, 3 => 74, _ => 68 }, 26);
    public static LayoutRect ActionHitRow => new(0, 380, 520, 26);
    public static LayoutRect ZoomModal => new(24, 20, 472, 380);
    public static LayoutRect ZoomButton(int i) => new(i switch { 0 => 376, 1 => 410, _ => 444 }, 366, i == 2 ? 44 : 28, 22);
}

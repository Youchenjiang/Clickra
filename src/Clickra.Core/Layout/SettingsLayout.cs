namespace Clickra.Core.Layout;

/// <summary>
/// Shared geometry for the NativeAOT settings surface. Painting registers hit rectangles from
/// these primitives so later responsive layout changes cannot move a control without moving its
/// interactive area with it.
/// </summary>
public static class SettingsLayout
{
    public const int ContentTop = 100;
    public const int InlineGap = 10;
    public const int HeaderDescriptionOffset = 22;

    public const int ToggleRightInset = 100;
    public const int ToggleTopOffset = 5;
    public const int ToggleWidth = 44;
    public const int ToggleHeight = 22;
    public const int ToggleSectionHeight = 70;
    public const int PrimaryToggleSectionHeight = 52;

    public const int ControlTopOffset = 50;
    public const int ButtonHeight = 30;
    public const int ChoiceSectionHeight = 88;
    public const int PrimaryChoiceSectionHeight = 84;

    public const int LanguageDropdownOffset = 50;
    public const int LanguageSectionHeight = 110;
    public const int PdfLanguageLabelOffset = 50;
    public const int PdfLanguageDropdownOffset = 72;
    public const int PdfLanguageSectionHeight = 117;

    public const int SliderWidth = 300;
    public const int SliderHitHorizontalPadding = 10;
    public const int SliderHitTopPadding = 4;
    public const int SliderHitHeight = 62;
    public const int SliderHeaderGap = 48;
    public const int SliderSectionHeight = 72;

    public const int CompactToggleSectionHeight = 44;
    public const int DynamicNumberHeaderGap = 50;
    public const int DynamicNumberValueHeight = 28;
    public const int DynamicNumberButtonWidth = 34;
    public const int DynamicNumberSectionTail = 50;
    public const int DynamicChoiceSectionHeight = 50;

    public const int WideBreakpoint = 620;
    public const int ContentRightMargin = 10;
    public const int ColumnGap = 10;
    public const int CardPadding = 12;
    public const int OverviewCardHeight = 126;
    public const int OverviewRowGap = 48;
    public const int CardGap = 14;
    public const int CompressionCardHeight = 214;
    public const int CompressionSliderTop = 52;
    public const int CompressionSecondaryTop = 122;
    public const int ChoiceCardHeight = 72;
    public const int FluentCardHeight = 76;

    public static bool IsWide(int logW) => logW >= WideBreakpoint;

    public static int ColumnWidth(int contentX, int logW) =>
        (logW - contentX - ContentRightMargin - ColumnGap) / 2;

    public static int ColumnX(int contentX, int logW, int columnIndex) =>
        columnIndex == 0 ? contentX : contentX + ColumnWidth(contentX, logW) + ColumnGap;

    public static int SliderWidthFor(int availableWidth) =>
        Math.Min(SliderWidth, Math.Max(120, availableWidth - 2 * SliderHitHorizontalPadding));

    public static LayoutRect CardRect(int x, int y, int width, int height) =>
        new LayoutRect(x, y, width, height);

    public static LayoutRect ToggleRect(int logW, int sectionY) =>
        new LayoutRect(logW - ToggleRightInset, sectionY + ToggleTopOffset, ToggleWidth, ToggleHeight);

    public static LayoutRect ToggleRectWithin(int x, int width, int sectionY) =>
        new LayoutRect(x + width - ToggleWidth, sectionY + ToggleTopOffset, ToggleWidth, ToggleHeight);

    public static LayoutRect ButtonRect(int x, int y, int width) =>
        new LayoutRect(x, y, width, ButtonHeight);

    public static LayoutRect SliderHitRect(int x, int y, int width) =>
        new LayoutRect(
            x - SliderHitHorizontalPadding,
            y - SliderHitTopPadding,
            width + 2 * SliderHitHorizontalPadding,
            SliderHitHeight);
}

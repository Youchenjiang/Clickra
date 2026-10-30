namespace Clickra.UI.Layout;

/// <summary>
/// Logical-pixel geometry for the native PDF print confirmation and preview dialog.
/// Width: 520, Height: 440 (matches standard prompt window scale).
/// </summary>
public static class PrintOptionsLayout
{
    public const int ClientWidth = 520;
    public const int ClientHeight = 440;

    // Left controls column (Options)
    public const int LeftColumnX = 36;
    public const int LeftColumnWidth = 200;

    public const int PrinterLabelY = 100;
    public const int PrinterComboY = 122;
    public const int PrinterComboHeight = 26;

    public const int RangeLabelY = 160;
    public const int RangeInputY = 182;
    public const int RangeInputHeight = 24;

    public const int CopiesLabelY = 220;
    public const int CopiesInputY = 242;
    public const int CopiesInputHeight = 24;

    public const int FitToPageY = 282;
    public const int FitToPageHeight = 22;

    public const int DarkenY = 312;
    public const int DarkenHeight = 22;

    // Right preview column
    public const int PreviewLeft = 256;
    public const int PreviewTop = 100;
    public const int PreviewWidth = 228;
    public const int PreviewHeight = 236;

    public static LayoutRect PreviewArea => new(PreviewLeft, PreviewTop, PreviewWidth, PreviewHeight);

    // Nav buttons below preview
    public const int NavY = 344;
    public const int NavButtonWidth = 26;
    public const int NavButtonHeight = 22;

    public static LayoutRect PrevPageButton => new(PreviewLeft, NavY, NavButtonWidth, NavButtonHeight);
    public static LayoutRect NextPageButton => new(PreviewLeft + PreviewWidth - NavButtonWidth, NavY, NavButtonWidth, NavButtonHeight);
    public static LayoutRect PageTextRect => new(PreviewLeft + NavButtonWidth + 4, NavY, PreviewWidth - (NavButtonWidth * 2) - 8, NavButtonHeight);

    // Bottom action buttons
    public const int ActionY = 388;
    public static LayoutRect ConfirmButton => new(296, ActionY, 100, 30);
    public static LayoutRect CancelButton => new(404, ActionY, 80, 30);
}

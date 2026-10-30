namespace Clickra.UI.Layout;

/// <summary>
/// Logical-pixel geometry for the native PDF print confirmation and preview dialog.
/// Width: 520, Height: 460 (matches standard prompt window scale).
/// </summary>
public static class PrintOptionsLayout
{
    public const int ClientWidth = 520;
    public const int ClientHeight = 460;

    // Left controls column (Options)
    public const int LeftColumnX = 36;
    public const int LeftColumnWidth = 200;

    public const int PrinterLabelY = 96;
    public const int PrinterComboY = 118;
    public const int PrinterComboHeight = 26;
    public static LayoutRect PrinterComboRect => new(LeftColumnX, PrinterComboY, LeftColumnWidth, PrinterComboHeight);

    public const int PaperLabelY = 148;
    public const int PaperComboY = 168;
    public const int PaperComboHeight = 26;
    public static LayoutRect PaperComboRect => new(LeftColumnX, PaperComboY, LeftColumnWidth, PaperComboHeight);

    public const int DuplexLabelY = 198;
    public const int DuplexComboY = 218;
    public const int DuplexComboHeight = 26;
    public static LayoutRect DuplexComboRect => new(LeftColumnX, DuplexComboY, LeftColumnWidth, DuplexComboHeight);

    public const int RangeLabelY = 248;
    public const int RangeInputY = 268;
    public const int RangeInputHeight = 24;

    public const int CopiesLabelY = 298;
    public const int CopiesInputY = 318;
    public const int CopiesInputHeight = 24;

    public const int FitToPageY = 348;
    public const int FitToPageHeight = 20;

    public const int DarkenY = 372;
    public const int DarkenHeight = 20;

    // Right preview column
    public const int PreviewLeft = 256;
    public const int PreviewTop = 96;
    public const int PreviewWidth = 228;
    public const int PreviewHeight = 266;

    public static LayoutRect PreviewArea => new(PreviewLeft, PreviewTop, PreviewWidth, PreviewHeight);

    // Nav buttons below preview
    public const int NavY = 370;
    public const int NavButtonWidth = 26;
    public const int NavButtonHeight = 22;

    public static LayoutRect PrevPageButton => new(PreviewLeft, NavY, NavButtonWidth, NavButtonHeight);
    public static LayoutRect NextPageButton => new(PreviewLeft + PreviewWidth - NavButtonWidth, NavY, NavButtonWidth, NavButtonHeight);
    public static LayoutRect PageTextRect => new(PreviewLeft + NavButtonWidth + 4, NavY, PreviewWidth - (NavButtonWidth * 2) - 8, NavButtonHeight);

    // Bottom action buttons
    public const int SeparatorY = 402;
    public const int ActionY = 414;
    public static LayoutRect ConfirmButton => new(296, ActionY, 100, 30);
    public static LayoutRect CancelButton => new(404, ActionY, 80, 30);

    // Zoom modal geometry (logical px)
    public static LayoutRect ZoomModal => new(24, 20, 472, 420);
    public static LayoutRect ZoomButton(int i) => new(i switch { 0 => 376, 1 => 410, _ => 444 }, 406, i == 2 ? 44 : 28, 22);
    public static LayoutRect ZoomCloseButton => new(414, 28, 70, 22);
}

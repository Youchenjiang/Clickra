namespace Clickra.UI.Layout;

/// <summary>
/// Logical-pixel geometry for the native PDF print confirmation and preview dialog.
/// Width: 520, Height: 466 (matches standard prompt window scale).
/// </summary>
public static class PrintOptionsLayout
{
    public const int ClientWidth = 520;
    public const int ClientHeight = 466;

    // Left controls column (Options)
    public const int LeftColumnX = 36;
    public const int LeftColumnWidth = 200;

    // Group 1: Device & Media (Printer, Paper, Duplex)
    public const int PrinterLabelY = 96;
    public const int PrinterComboY = 116;
    public const int PrinterComboHeight = 26;
    public static LayoutRect PrinterComboRect => new(LeftColumnX, PrinterComboY, LeftColumnWidth, PrinterComboHeight);

    public const int PaperLabelY = 148;
    public const int PaperComboY = 168;
    public const int PaperComboHeight = 26;
    public static LayoutRect PaperComboRect => new(LeftColumnX, PaperComboY, LeftColumnWidth, PaperComboHeight);

    public const int DuplexLabelY = 200;
    public const int DuplexComboY = 220;
    public const int DuplexComboHeight = 26;
    public static LayoutRect DuplexComboRect => new(LeftColumnX, DuplexComboY, LeftColumnWidth, DuplexComboHeight);

    // Group 2: Scope & Quantity (Range, Copies)
    public const int RangeLabelY = 258;
    public const int RangeInputY = 278;
    public const int RangeInputHeight = 24;
    public static LayoutRect RangeAllRect => new(LeftColumnX, RangeInputY, 70, RangeInputHeight);
    public static LayoutRect RangeCustomRect => new(LeftColumnX + 74, RangeInputY, LeftColumnWidth - 74, RangeInputHeight);

    public const int CopiesLabelY = 308;
    public const int CopiesInputY = 328;
    public const int CopiesInputHeight = 24;
    public const int CopiesButtonWidth = 44;
    public static LayoutRect CopiesMinusRect => new(LeftColumnX, CopiesInputY, CopiesButtonWidth, CopiesInputHeight);
    public static LayoutRect CopiesDisplayRect => new(LeftColumnX + CopiesButtonWidth + 4, CopiesInputY, LeftColumnWidth - (CopiesButtonWidth * 2) - 8, CopiesInputHeight);
    public static LayoutRect CopiesPlusRect => new(LeftColumnX + LeftColumnWidth - CopiesButtonWidth, CopiesInputY, CopiesButtonWidth, CopiesInputHeight);

    // Group 3: Layout & Contrast (Fit, Darken)
    public const int FitToPageY = 364;
    public const int FitToPageHeight = 18;
    public static LayoutRect FitToPageRect => new(LeftColumnX, FitToPageY, LeftColumnWidth, FitToPageHeight);

    public const int DarkenY = 388;
    public const int DarkenHeight = 18;
    public static LayoutRect DarkenRect => new(LeftColumnX, DarkenY, LeftColumnWidth, DarkenHeight);

    // Right preview column (Card Panel)
    public const int PreviewCardLeft = 256;
    public const int PreviewCardTop = 28;
    public const int PreviewCardWidth = 228;
    public const int PreviewCardHeight = 378;
    public static LayoutRect PreviewCardRect => new(PreviewCardLeft, PreviewCardTop, PreviewCardWidth, PreviewCardHeight);

    // Card Header (Y: 28..58)
    public const int CardHeaderHeight = 30;
    public static LayoutRect CardHeaderRect => new(PreviewCardLeft, PreviewCardTop, PreviewCardWidth, CardHeaderHeight);

    // Card Body / Viewport (Y: 58..372, Height: 314)
    public const int PreviewTop = PreviewCardTop + CardHeaderHeight;
    public const int PreviewLeft = PreviewCardLeft;
    public const int PreviewWidth = PreviewCardWidth;
    public const int PreviewHeight = 314;
    public static LayoutRect PreviewArea => new(PreviewLeft, PreviewTop, PreviewWidth, PreviewHeight);

    // Zoom Pill inside Card Viewport (bottom-right)
    public const int ZoomPillWidth = 64;
    public const int ZoomPillHeight = 20;
    public static LayoutRect ZoomPillRect => new(PreviewLeft + PreviewWidth - ZoomPillWidth - 8, PreviewTop + PreviewHeight - ZoomPillHeight - 8, ZoomPillWidth, ZoomPillHeight);

    // Card Footer / Navigation (Y: 372..406, Height: 34)
    public const int NavY = PreviewTop + PreviewHeight;
    public const int NavHeight = 34;
    public const int NavButtonWidth = 26;
    public const int NavButtonHeight = 22;
    public static LayoutRect CardFooterRect => new(PreviewCardLeft, NavY, PreviewCardWidth, NavHeight);
    public static LayoutRect PrevPageButton => new(PreviewLeft + 8, NavY + 6, NavButtonWidth, NavButtonHeight);
    public static LayoutRect NextPageButton => new(PreviewLeft + PreviewWidth - NavButtonWidth - 8, NavY + 6, NavButtonWidth, NavButtonHeight);
    public static LayoutRect PageTextRect => new(PreviewLeft + NavButtonWidth + 12, NavY + 6, PreviewWidth - (NavButtonWidth * 2) - 24, NavButtonHeight);

    // Bottom action buttons
    public const int SeparatorY = 418;
    public const int ActionY = 428;
    public static LayoutRect ConfirmButton => new(296, ActionY, 100, 28);
    public static LayoutRect CancelButton => new(404, ActionY, 80, 28);

    // Zoom modal geometry (logical px)
    public static LayoutRect ZoomModal => new(24, 20, 472, 420);
    public static LayoutRect ZoomButton(int i) => new(i switch { 0 => 376, 1 => 410, _ => 444 }, 406, i == 2 ? 44 : 28, 22);
    public static LayoutRect ZoomCloseButton => new(414, 28, 70, 22);
}

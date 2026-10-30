using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Clickra.Core;
using Clickra.Core.Modules.PdfPrint.Application;
using Clickra.Core.Modules.PdfPrint.Ports;
using Clickra.Core.Modules.PdfPrint.Processing;
using Clickra.Core.Processors;
using Clickra.Core.Rendering;
using Clickra.UI.Layout;
using UglyToad.PdfPig;

using static Clickra.UI.Native.Win32;

namespace Clickra.UI;

public partial class ProgressWindow
{
    private const int PrintOptionsClientHeight = PrintOptionsLayout.ClientHeight;

    private bool _isPromptingPrintOptions;
    private Action<bool>? _printDecision = null; // skipcq: CS-R1137
    private bool _printDecisionResolved;

    private List<string> _installedPrinters = new();
    private int _printPrinterIndex;
    private string _printPrinterName = "";
    private List<string> _supportedPaperSizes = new();
    private int _paperSizeIndex;
    private bool _printerCanDuplex;
    private int _duplexIndex; // 0 = Simplex, 1 = DuplexLongEdge, 2 = DuplexShortEdge
    private string _printPageRange = "all";
    private int _printCopies = 1;
    private bool _printFitToPage = true;
    private bool _printDarken;

    private float _pdfPageWidthPt = 595.28f;
    private float _pdfPageHeightPt = 841.89f;

    private bool _printIsZoomed;
    private float _printZoomFactor = 1f;
    private float _printZoomPanX;
    private float _printZoomPanY;
    private bool _printZoomDragging;
    private int _printZoomDragLastX;
    private int _printZoomDragLastY;

    private const float PrintZoomImgLeft = 40f;
    private const float PrintZoomImgTop = 56f;
    private const float PrintZoomImgW = 440f;
    private const float PrintZoomImgH = 340f;

    private enum PrintDropdownType
    {
        None = 0,
        Printer,
        PaperSize,
        Duplex
    }

    private PrintDropdownType _openDropdown = PrintDropdownType.None;
    private int _hoveredDropdownIndex = -1;
    private int _dropdownScrollOffset;

    private PdfPreviewModel _printPreviewModel = new(1);
    private enum PrintElement
    {
        None = 0,
        PrinterCombo,
        PaperCombo,
        DuplexCombo,
        RangeAll,
        RangeCustom,
        MinusCopies,
        PlusCopies,
        FitToPage,
        Darken,
        PreviewArea,
        PrevPage,
        NextPage,
        Confirm,
        Cancel
    }

    private PrintElement _hoveredPrintElement = PrintElement.None;
    private PrintElement _pressedPrintElement = PrintElement.None;

    private readonly IPrinterDiscoveryService _printerDiscovery = new SystemPrinterDiscoveryService();
    private readonly Dictionary<int, Bitmap> _printThumbnails = new();
    private readonly HashSet<int> _printPendingThumbnails = new();

    private void RefreshPrinterCapabilities(IPrinterDiscoveryService discovery)
    {
        try
        {
            _supportedPaperSizes = discovery.GetSupportedPaperSizes(_printPrinterName).ToList();
            if (_supportedPaperSizes.Count == 0)
            {
                _supportedPaperSizes.Add("A4");
            }
            int a4Index = _supportedPaperSizes.FindIndex(p => string.Equals(p, "A4", StringComparison.OrdinalIgnoreCase));
            _paperSizeIndex = a4Index >= 0 ? a4Index : 0;
            _printerCanDuplex = discovery.CanDuplex(_printPrinterName);
            if (!_printerCanDuplex)
            {
                _duplexIndex = 0;
            }
        }
        catch
        {
            _supportedPaperSizes = new List<string> { "A4" };
            _paperSizeIndex = 0;
            _printerCanDuplex = false;
            _duplexIndex = 0;
        }
    }

    private void InitializePrintOptions()
    {
        try
        {
            _installedPrinters = _printerDiscovery.GetInstalledPrinters().ToList();
            if (_installedPrinters.Count == 0)
            {
                _installedPrinters.Add(_printerDiscovery.GetDefaultPrinter() ?? "Default Printer");
            }
            _printPrinterName = _printerDiscovery.GetDefaultPrinter() ?? _installedPrinters[0];
            _printPrinterIndex = Math.Max(0, _installedPrinters.FindIndex(p => string.Equals(p, _printPrinterName, StringComparison.OrdinalIgnoreCase)));
            RefreshPrinterCapabilities(_printerDiscovery);

            var printersToWarm = _installedPrinters.ToList();
            Task.Run(() =>
            {
                foreach (string printer in printersToWarm)
                {
                    if (_cts.IsCancellationRequested) break;
                    _printerDiscovery.GetSupportedPaperSizes(printer);
                    _printerDiscovery.CanDuplex(printer);
                }
            }, _cts.Token);
        }
        catch
        {
            _installedPrinters = new List<string> { "Default Printer" };
            _printPrinterName = _installedPrinters[0];
            _printPrinterIndex = 0;
            _supportedPaperSizes = new List<string> { "A4" };
            _paperSizeIndex = 0;
            _printerCanDuplex = false;
            _duplexIndex = 0;
        }

        if (_files.Count > 0 && File.Exists(_files[0]))
        {
            int totalPages = 1;
            try
            {
                using var doc = PdfDocument.Open(_files[0]);
                totalPages = Math.Max(1, doc.NumberOfPages);
                var p1 = doc.GetPage(1);
                _pdfPageWidthPt = (float)p1.Width;
                _pdfPageHeightPt = (float)p1.Height;
            }
            catch
            {
                // Fallback to single page for unreadable or encrypted files
            }
            _printPreviewModel = new PdfPreviewModel(totalPages);
            RequestPrintPreviewThumbnail(1);
        }
    }

    private void RequestPrintPreviewThumbnail(int page)
    {
        lock (_printThumbnails)
        {
            if (_printThumbnails.ContainsKey(page) || _printPendingThumbnails.Contains(page))
                return;
            _printPendingThumbnails.Add(page);
        }

        if (_files.Count == 0 || !File.Exists(_files[0])) return;
        string filePath = _files[0];
        string fontName = PdfPageThumbnailRenderer.GetTextFontName(SettingsPresentationController.Shared.Language);

        Task.Run(() => RenderThumbnailBackground(filePath, page, fontName), _cts.Token);
    }

    private void RenderThumbnailBackground(string filePath, int page, string fontName)
    {
        if (_cts.IsCancellationRequested) return;
        Bitmap? bmp = null;
        try
        {
            using (var doc = PdfDocument.Open(filePath))
            {
                if (page >= 1 && page <= doc.NumberOfPages)
                {
                    var p = doc.GetPage(page);
                    _pdfPageWidthPt = (float)p.Width;
                    _pdfPageHeightPt = (float)p.Height;
                }
            }
            bmp = PdfPageThumbnailRenderer.RenderPageFromFile(filePath, page, 456, fontName);
            if (bmp != null && _printDarken)
            {
                PdfContrastEnhancer.Enhance(bmp, 2.2);
            }
        }
        catch
        {
            // Render failure falls back to null
        }
        lock (_printThumbnails)
        {
            _printPendingThumbnails.Remove(page);
            if (bmp != null)
            {
                _printThumbnails[page] = bmp;
            }
        }
        if (_hwnd != IntPtr.Zero)
        {
            PostMessageW(_hwnd, WM_USER_INVALIDATE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private static RectangleF ScalePrintRect(LayoutRect rect, float scale) =>
        new(rect.X * scale, rect.Y * scale, rect.Width * scale, rect.Height * scale);

    private void PaintPrintOptions(Graphics g, float s)
    {
        if (_msgFont is null || _tipFont is null) return;
        Font msgFont = _msgFont;
        Font tipFont = _tipFont;

        using var bodyBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
        using var mutedBrush = new SolidBrush(Color.FromArgb(145, 145, 145));

        string currentPaperText = _supportedPaperSizes.Count > 0 && _paperSizeIndex >= 0 && _paperSizeIndex < _supportedPaperSizes.Count
            ? _supportedPaperSizes[_paperSizeIndex]
            : "A4";

        PaintPrintControls(g, s, bodyBrush, currentPaperText);
        PaintPrintPreviewCard(g, s, msgFont, tipFont, bodyBrush, mutedBrush, currentPaperText);
        PaintPrintActionButtons(g, s);

        if (_openDropdown != PrintDropdownType.None)
        {
            PaintOpenDropdownOverlay(g, s);
        }

        if (_printIsZoomed)
        {
            PaintPrintZoomOverlay(g, s);
        }
    }

    private void PaintPrintControls(Graphics g, float s, Brush bodyBrush, string currentPaperText)
    {
        // 1. Printer Row
        DrawPrintLabel(g, s, Loc("print_options_printer"), PrintOptionsLayout.PrinterLabelY, bodyBrush);
        DrawPrintDropdownButton(g, s, PrintOptionsLayout.PrinterComboRect, _printPrinterName,
            isOpen: _openDropdown == PrintDropdownType.Printer,
            enabled: _installedPrinters.Count > 0,
            isHovered: _hoveredPrintElement == PrintElement.PrinterCombo);

        // 2. Paper Size Row
        DrawPrintLabel(g, s, Loc("print_options_paper_size"), PrintOptionsLayout.PaperLabelY, bodyBrush);
        DrawPrintDropdownButton(g, s, PrintOptionsLayout.PaperComboRect, currentPaperText,
            isOpen: _openDropdown == PrintDropdownType.PaperSize,
            enabled: _supportedPaperSizes.Count > 0,
            isHovered: _hoveredPrintElement == PrintElement.PaperCombo);

        // 3. Duplex Row
        DrawPrintLabel(g, s, Loc("print_options_duplex"), PrintOptionsLayout.DuplexLabelY, bodyBrush);
        string currentDuplexText = !_printerCanDuplex
            ? Loc("print_options_duplex_unsupported")
            : _duplexIndex switch
            {
                1 => Loc("print_options_duplex_long"),
                2 => Loc("print_options_duplex_short"),
                _ => Loc("print_options_duplex_simplex")
            };
        DrawPrintDropdownButton(g, s, PrintOptionsLayout.DuplexComboRect, currentDuplexText,
            isOpen: _openDropdown == PrintDropdownType.Duplex,
            enabled: _printerCanDuplex,
            isHovered: _hoveredPrintElement == PrintElement.DuplexCombo);

        // 4. Page Range Row
        DrawPrintLabel(g, s, Loc("print_options_range"), PrintOptionsLayout.RangeLabelY, bodyBrush);
        bool isAll = string.Equals(_printPageRange, "all", StringComparison.OrdinalIgnoreCase);
        DrawPrintButton(g, s, PrintOptionsLayout.RangeAllRect, Loc("print_options_range_all"),
            new ButtonVisualState(isAll, Hovered: _hoveredPrintElement == PrintElement.RangeAll, Pressed: _pressedPrintElement == PrintElement.RangeAll));
        DrawPrintButton(g, s, PrintOptionsLayout.RangeCustomRect, isAll ? Loc("print_options_range_custom") : _printPageRange,
            new ButtonVisualState(!isAll, Hovered: _hoveredPrintElement == PrintElement.RangeCustom, Pressed: _pressedPrintElement == PrintElement.RangeCustom));

        // 5. Copies Row (Full Width Aligned)
        DrawPrintLabel(g, s, Loc("print_options_copies"), PrintOptionsLayout.CopiesLabelY, bodyBrush);
        DrawPrintButton(g, s, PrintOptionsLayout.CopiesMinusRect, "-",
            new ButtonVisualState(false, Enabled: _printCopies > 1, Hovered: _hoveredPrintElement == PrintElement.MinusCopies, Pressed: _pressedPrintElement == PrintElement.MinusCopies));
        DrawPrintDisplayBox(g, s, PrintOptionsLayout.CopiesDisplayRect, _printCopies.ToString());
        DrawPrintButton(g, s, PrintOptionsLayout.CopiesPlusRect, "+",
            new ButtonVisualState(false, Enabled: _printCopies < 99, Hovered: _hoveredPrintElement == PrintElement.PlusCopies, Pressed: _pressedPrintElement == PrintElement.PlusCopies));

        // 6. Fit to Page Checkbox
        DrawPrintCheckbox(g, s, PrintOptionsLayout.FitToPageRect, Loc("print_options_fit"), _printFitToPage,
            hovered: _hoveredPrintElement == PrintElement.FitToPage);

        // 7. Darken Text Checkbox
        DrawPrintCheckbox(g, s, PrintOptionsLayout.DarkenRect, Loc("print_options_darken"), _printDarken,
            hovered: _hoveredPrintElement == PrintElement.Darken);
    }

    private void PaintPrintPreviewCard(Graphics g, float s, Font msgFont, Font tipFont, Brush bodyBrush, Brush mutedBrush, string currentPaperText)
    {
        var cardRect = ScalePrintRect(PrintOptionsLayout.PreviewCardRect, s);
        var headerRect = ScalePrintRect(PrintOptionsLayout.CardHeaderRect, s);
        var viewportRect = ScalePrintRect(PrintOptionsLayout.PreviewArea, s);
        var footerRect = ScalePrintRect(PrintOptionsLayout.CardFooterRect, s);

        // 1. Card Outer Shell: Rounded rectangle, subtle background & border
        using (var cardPath = UIHelper.GetRoundedRectPath(cardRect, 6 * s))
        using (var cardBgBrush = new SolidBrush(Color.FromArgb(24, 25, 29)))
        using (var cardBorderPen = new Pen(Color.FromArgb(52, 56, 66), 1f * s))
        {
            g.FillPath(cardBgBrush, cardPath);
            g.DrawPath(cardBorderPen, cardPath);
        }

        // 2. Card Header
        var paperDim = PaperSizeRegistry.GetDimensions(currentPaperText);
        PaintCardHeader(g, s, msgFont, tipFont, headerRect, paperDim);

        // 3. Card Viewport (Paper Canvas)
        PaintCardViewport(g, s, tipFont, mutedBrush, viewportRect, paperDim);

        // 4. Zoom Pill Badge at bottom-right of Viewport
        PaintZoomPillBadge(g, s, tipFont);

        // 5. Card Footer: Integrated Navigation Bar
        PaintCardFooter(g, s, msgFont, bodyBrush, footerRect);

        // Re-stroke outer card rounded border so sub-panel fills don't square off corners
        using (var cardBorderPen = new Pen(Color.FromArgb(52, 56, 66), 1f * s))
        using (var cardPath = UIHelper.GetRoundedRectPath(cardRect, 6 * s))
        {
            g.DrawPath(cardBorderPen, cardPath);
        }
    }

    private void PaintCardHeader(Graphics g, float s, Font msgFont, Font tipFont, RectangleF headerRect, PaperDimension paperDim)
    {
        using (var headerBg = new SolidBrush(Color.FromArgb(28, 30, 36)))
        {
            g.FillRectangle(headerBg, headerRect);
        }
        using (var headerBottomPen = new Pen(Color.FromArgb(44, 46, 54), 1f * s))
        {
            g.DrawLine(headerBottomPen, headerRect.Left, headerRect.Bottom, headerRect.Right, headerRect.Bottom);
        }

        // Card Header Left: "即時預覽" (Live Preview)
        string previewTitle = Loc("print_options_preview");
        using (var previewTitleBrush = new SolidBrush(Color.FromArgb(220, 225, 235)))
        using (var titleFormat = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        })
        {
            var titleRect = new RectangleF(headerRect.Left + (12f * s), headerRect.Top, headerRect.Width - (130f * s), headerRect.Height);
            g.DrawString(previewTitle, msgFont, previewTitleBrush, titleRect, titleFormat);
        }

        // Card Header Right: Paper Size specification pill
        string paperInfo = $"{paperDim.DisplayName} · {paperDim.WidthMm:0}×{paperDim.HeightMm:0} mm";
        var paperInfoSize = g.MeasureString(paperInfo, tipFont);
        float pillW = paperInfoSize.Width + (12f * s);
        float pillH = 18f * s;
        float pillX = headerRect.Right - pillW - (8f * s);
        float pillY = headerRect.Top + ((headerRect.Height - pillH) / 2f);
        var paperPillRect = new RectangleF(pillX, pillY, pillW, pillH);

        using (var pillPath = UIHelper.GetRoundedRectPath(paperPillRect, 3 * s))
        using (var pillBg = new SolidBrush(Color.FromArgb(36, 40, 48)))
        using (var pillBorder = new Pen(Color.FromArgb(62, 68, 80), 1f * s))
        using (var pillTextBrush = new SolidBrush(Color.FromArgb(185, 195, 210)))
        using (var pillFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.FillPath(pillBg, pillPath);
            g.DrawPath(pillBorder, pillPath);
            g.DrawString(paperInfo, tipFont, pillTextBrush, paperPillRect, pillFormat);
        }
    }

    private void PaintCardViewport(Graphics g, float s, Font tipFont, Brush mutedBrush, RectangleF viewportRect, PaperDimension paperDim)
    {
        using var viewportBgBrush = new SolidBrush(Color.FromArgb(18, 19, 22));
        g.FillRectangle(viewportBgBrush, viewportRect);

        int curPage = _printPreviewModel.CurrentPageNumber;
        Bitmap? bmp;
        lock (_printThumbnails)
        {
            _printThumbnails.TryGetValue(curPage, out bmp);
        }

        if (bmp != null)
        {
            bool isLandscape = _pdfPageWidthPt > _pdfPageHeightPt || bmp.Width > bmp.Height;
            float targetPaperW = isLandscape ? Math.Max(paperDim.WidthPt, paperDim.HeightPt) : Math.Min(paperDim.WidthPt, paperDim.HeightPt);
            float targetPaperH = isLandscape ? Math.Min(paperDim.WidthPt, paperDim.HeightPt) : Math.Max(paperDim.WidthPt, paperDim.HeightPt);

            float maxSheetW = viewportRect.Width - (24f * s);
            float maxSheetH = viewportRect.Height - (28f * s);
            float sheetScale = Math.Min(maxSheetW / targetPaperW, maxSheetH / targetPaperH);
            float sheetW = targetPaperW * sheetScale;
            float sheetH = targetPaperH * sheetScale;
            float sheetX = viewportRect.X + ((viewportRect.Width - sheetW) / 2f);
            float sheetY = viewportRect.Y + ((viewportRect.Height - sheetH) / 2f);

            // Sheet shadow
            using var shadowBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
            g.FillRectangle(shadowBrush, sheetX + (3f * s), sheetY + (3f * s), sheetW, sheetH);

            // Sheet canvas
            using var sheetBrush = new SolidBrush(Color.White);
            g.FillRectangle(sheetBrush, sheetX, sheetY, sheetW, sheetH);
            using var sheetPen = new Pen(Color.FromArgb(170, 175, 185), 1f * s);
            g.DrawRectangle(sheetPen, sheetX, sheetY, sheetW, sheetH);

            var pageBounds = CalculatePageDrawBounds(bmp, new RectangleF(sheetX, sheetY, sheetW, sheetH), _printFitToPage, _pdfPageWidthPt, _pdfPageHeightPt, new SizeF(targetPaperW, targetPaperH));

            var oldClip = g.Clip;
            g.SetClip(new RectangleF(sheetX, sheetY, sheetW, sheetH));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(bmp, pageBounds.X, pageBounds.Y, pageBounds.Width, pageBounds.Height);
            g.Clip = oldClip;
        }
        else
        {
            using var centerFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(Loc("print_options_loading"), tipFont, mutedBrush, viewportRect, centerFormat);
        }
    }

    private void PaintZoomPillBadge(Graphics g, float s, Font tipFont)
    {
        bool isZoomPillHovered = _hoveredPrintElement == PrintElement.PreviewArea;
        var zoomPillRect = ScalePrintRect(PrintOptionsLayout.ZoomPillRect, s);
        using (var zoomPath = UIHelper.GetRoundedRectPath(zoomPillRect, zoomPillRect.Height / 2f))
        using (var zoomBg = new SolidBrush(isZoomPillHovered ? Color.FromArgb(235, 38, 44, 58) : Color.FromArgb(180, 24, 26, 32)))
        using (var zoomBorder = new Pen(isZoomPillHovered ? UIHelper.GetSystemColorizationColor() : Color.FromArgb(70, 75, 88), 1f * s))
        using (var zoomTextBrush = new SolidBrush(isZoomPillHovered ? Color.White : Color.FromArgb(195, 200, 210)))
        {
            g.FillPath(zoomBg, zoomPath);
            g.DrawPath(zoomBorder, zoomPath);
            float iconX = zoomPillRect.Left + (11f * s);
            float iconY = zoomPillRect.Top + (zoomPillRect.Height / 2f);
            VectorIcons.DrawMagnifier(g, iconX, iconY, 10f * s, zoomTextBrush.Color, 1.4f * s);

            var textRect = new RectangleF(zoomPillRect.Left + (18f * s), zoomPillRect.Top, zoomPillRect.Width - (20f * s), zoomPillRect.Height);
            using var textFormat = new StringFormat { LineAlignment = StringAlignment.Center };
            g.DrawString(Loc("pdf_split_zoom_tag"), tipFont, zoomTextBrush, textRect, textFormat);
        }
    }

    private void PaintCardFooter(Graphics g, float s, Font msgFont, Brush bodyBrush, RectangleF footerRect)
    {
        using (var footerBg = new SolidBrush(Color.FromArgb(28, 30, 36)))
        {
            g.FillRectangle(footerBg, footerRect);
        }
        using (var footerTopPen = new Pen(Color.FromArgb(44, 46, 54), 1f * s))
        {
            g.DrawLine(footerTopPen, footerRect.Left, footerRect.Top, footerRect.Right, footerRect.Top);
        }

        DrawPrintButton(g, s, PrintOptionsLayout.PrevPageButton, "<",
            new ButtonVisualState(false, Enabled: _printPreviewModel.CanNavigatePrevious, Hovered: _hoveredPrintElement == PrintElement.PrevPage, Pressed: _pressedPrintElement == PrintElement.PrevPage));

        using (var pageFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            string pageText = Loc("print_options_page_indicator", _printPreviewModel.CurrentPageNumber, _printPreviewModel.TotalPages);
            g.DrawString(pageText, msgFont, bodyBrush, ScalePrintRect(PrintOptionsLayout.PageTextRect, s), pageFormat);
        }

        DrawPrintButton(g, s, PrintOptionsLayout.NextPageButton, ">",
            new ButtonVisualState(false, Enabled: _printPreviewModel.CanNavigateNext, Hovered: _hoveredPrintElement == PrintElement.NextPage, Pressed: _pressedPrintElement == PrintElement.NextPage));
    }

    private void PaintPrintActionButtons(Graphics g, float s)
    {
        using var sepPen = new Pen(Color.FromArgb(50, 52, 60), 1f * s);
        g.DrawLine(sepPen, 36 * s, PrintOptionsLayout.SeparatorY * s, 484 * s, PrintOptionsLayout.SeparatorY * s);
        DrawPrintButton(g, s, PrintOptionsLayout.ConfirmButton, Loc("print_options_confirm"),
            new ButtonVisualState(true, Hovered: _hoveredPrintElement == PrintElement.Confirm, Pressed: _pressedPrintElement == PrintElement.Confirm));
        DrawPrintButton(g, s, PrintOptionsLayout.CancelButton, Loc("dialog_cancel"),
            new ButtonVisualState(false, Hovered: _hoveredPrintElement == PrintElement.Cancel, Pressed: _pressedPrintElement == PrintElement.Cancel));
    }

    private void DrawPrintLabel(Graphics g, float s, string text, int y, Brush brush)
    {
        if (_msgFont is null) return;
        var rect = ScalePrintRect(new LayoutRect(PrintOptionsLayout.LeftColumnX, y, PrintOptionsLayout.LeftColumnWidth, 20), s);
        using var format = new StringFormat { LineAlignment = StringAlignment.Center };
        g.DrawString(text, _msgFont, brush, rect, format);
    }

    private readonly record struct ButtonVisualState(bool Selected, bool Enabled = true, bool Hovered = false, bool Pressed = false);

    private void DrawPrintButton(Graphics g, float s, LayoutRect logicalRect, string text, ButtonVisualState state)
    {
        var rect = ScalePrintRect(logicalRect, s);
        using var path = UIHelper.GetRoundedRectPath(rect, 4 * s);
        Color accent = UIHelper.GetSystemColorizationColor();
        GetButtonColors(state, accent, out Color bg, out Color border);

        using var fill = new SolidBrush(bg);
        using var pen = new Pen(border, 1f * s);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        Color textColor = state.Enabled ? Color.White : Color.FromArgb(120, 120, 120);
        using var textBrush = new SolidBrush(textColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        g.DrawString(text, _msgFont ?? SystemFonts.DefaultFont, textBrush, rect, format);
    }

    private static void GetButtonColors(ButtonVisualState state, Color accent, out Color bg, out Color border)
    {
        if (!state.Enabled)
        {
            bg = Color.FromArgb(35, 35, 35);
            border = Color.FromArgb(48, 48, 48);
        }
        else if (state.Pressed)
        {
            bg = state.Selected ? UIHelper.Darken(accent, 0.25f) : Color.FromArgb(68, 68, 68);
            border = state.Selected ? accent : UIHelper.Lighten(accent, 0.2f);
        }
        else if (state.Hovered)
        {
            bg = state.Selected ? UIHelper.Lighten(accent, 0.15f) : Color.FromArgb(58, 58, 58);
            border = state.Selected ? UIHelper.Lighten(accent, 0.35f) : Color.FromArgb(95, 95, 95);
        }
        else if (state.Selected)
        {
            bg = accent;
            border = UIHelper.Lighten(accent, 0.18f);
        }
        else
        {
            bg = Color.FromArgb(45, 45, 45);
            border = Color.FromArgb(70, 70, 70);
        }
    }

    private void DrawPrintDisplayBox(Graphics g, float s, LayoutRect logicalRect, string text)
    {
        var rect = ScalePrintRect(logicalRect, s);
        using var path = UIHelper.GetRoundedRectPath(rect, 4 * s);
        using var fill = new SolidBrush(Color.FromArgb(32, 32, 32));
        using var pen = new Pen(Color.FromArgb(60, 60, 60), 1f * s);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        using var textBrush = new SolidBrush(Color.FromArgb(230, 230, 230));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, _msgFont ?? SystemFonts.DefaultFont, textBrush, rect, format);
    }

    private void DrawPrintCheckbox(Graphics g, float s, LayoutRect logicalRect, string text, bool isChecked, bool hovered = false)
    {
        var rect = ScalePrintRect(logicalRect, s);
        float boxSize = 16f * s;
        var boxRect = new RectangleF(rect.X, rect.Y + ((rect.Height - boxSize) / 2f), boxSize, boxSize);

        using var path = UIHelper.GetRoundedRectPath(boxRect, 3 * s);
        Color accent = UIHelper.GetSystemColorizationColor();
        GetCheckboxColors(isChecked, hovered, accent, out Color bg, out Color border);

        using var fill = new SolidBrush(bg);
        using var pen = new Pen(border, 1f * s);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        if (isChecked)
        {
            using var checkPen = new Pen(Color.White, 1.8f * s);
            g.DrawLines(checkPen, new[]
            {
                new PointF(boxRect.X + (3f * s), boxRect.Y + (8f * s)),
                new PointF(boxRect.X + (7f * s), boxRect.Y + (12f * s)),
                new PointF(boxRect.X + (13f * s), boxRect.Y + (4f * s))
            });
        }

        var labelRect = new RectangleF(boxRect.Right + (8f * s), rect.Y, rect.Width - boxSize - (8f * s), rect.Height);
        using var textBrush = new SolidBrush(hovered ? Color.White : Color.FromArgb(220, 220, 220));
        using var format = new StringFormat { LineAlignment = StringAlignment.Center };
        g.DrawString(text, _msgFont ?? SystemFonts.DefaultFont, textBrush, labelRect, format);
    }

    private static void GetCheckboxColors(bool isChecked, bool hovered, Color accent, out Color bg, out Color border)
    {
        if (isChecked)
        {
            bg = hovered ? UIHelper.Lighten(accent, 0.15f) : accent;
            border = hovered ? UIHelper.Lighten(accent, 0.35f) : UIHelper.Lighten(accent, 0.2f);
        }
        else
        {
            bg = hovered ? Color.FromArgb(55, 55, 55) : Color.FromArgb(40, 40, 40);
            border = hovered ? Color.FromArgb(120, 120, 120) : Color.FromArgb(80, 80, 80);
        }
    }

    private const int PrintDropdownItemHeight = 24;

    private List<string> GetOpenDropdownItems()
    {
        return _openDropdown switch
        {
            PrintDropdownType.Printer => _installedPrinters,
            PrintDropdownType.PaperSize => _supportedPaperSizes,
            PrintDropdownType.Duplex => new List<string>
            {
                Loc("print_options_duplex_simplex"),
                Loc("print_options_duplex_long"),
                Loc("print_options_duplex_short")
            },
            _ => new List<string>()
        };
    }

    private int GetOpenDropdownSelectedIndex()
    {
        return _openDropdown switch
        {
            PrintDropdownType.Printer => _printPrinterIndex,
            PrintDropdownType.PaperSize => _paperSizeIndex,
            PrintDropdownType.Duplex => _duplexIndex,
            _ => -1
        };
    }

    private int GetOpenDropdownMaxVisible()
    {
        return _openDropdown switch
        {
            PrintDropdownType.Printer => 6,
            PrintDropdownType.PaperSize => 7,
            PrintDropdownType.Duplex => 3,
            _ => 5
        };
    }

    private LayoutRect GetOpenDropdownPopupRect()
    {
        LayoutRect anchor = _openDropdown switch
        {
            PrintDropdownType.Printer => PrintOptionsLayout.PrinterComboRect,
            PrintDropdownType.PaperSize => PrintOptionsLayout.PaperComboRect,
            PrintDropdownType.Duplex => PrintOptionsLayout.DuplexComboRect,
            _ => new LayoutRect(0, 0, 0, 0)
        };

        if (anchor.Width == 0) return new LayoutRect(0, 0, 0, 0);

        var items = GetOpenDropdownItems();
        int visibleCount = Math.Min(GetOpenDropdownMaxVisible(), Math.Max(1, items.Count));
        int popupH = (visibleCount * PrintDropdownItemHeight) + 6;
        return new LayoutRect(anchor.X, anchor.Bottom + 2, anchor.Width, popupH);
    }

    private void DrawPrintDropdownButton(Graphics g, float s, LayoutRect logicalRect, string text, bool isOpen, bool enabled, bool isHovered)
    {
        var rect = ScalePrintRect(logicalRect, s);
        using var path = UIHelper.GetRoundedRectPath(rect, 4 * s);
        Color accent = UIHelper.GetSystemColorizationColor();
        GetDropdownButtonColors(enabled, isOpen, isHovered, accent, out Color bg, out Color border, out Color chevronCol);

        using var bgBrush = new SolidBrush(bg);
        using var borderPen = new Pen(border, isOpen ? 1.5f * s : 1f * s);
        g.FillPath(bgBrush, path);
        g.DrawPath(borderPen, path);

        // Text
        Color textCol = !enabled ? Color.FromArgb(120, 120, 120) : Color.White;
        using var textBrush = new SolidBrush(textCol);
        float textRight = rect.Right - (24f * s);
        var textRect = new RectangleF(rect.X + (10f * s), rect.Y, Math.Max(0, textRight - rect.X - (10f * s)), rect.Height);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        g.DrawString(text, _msgFont ?? SystemFonts.DefaultFont, textBrush, textRect, format);

        // Vector Chevron
        float chevronX = rect.Right - (13f * s);
        float chevronY = rect.Y + (rect.Height / 2f);
        if (isOpen)
        {
            VectorIcons.DrawChevronUp(g, chevronX, chevronY, 3.5f * s, chevronCol, 1.6f * s);
        }
        else
        {
            VectorIcons.DrawChevronDown(g, chevronX, chevronY, 3.5f * s, chevronCol, 1.6f * s);
        }
    }

    private static void GetDropdownButtonColors(bool enabled, bool isOpen, bool isHovered, Color accent, out Color bg, out Color border, out Color chevronCol)
    {
        if (!enabled)
        {
            bg = Color.FromArgb(32, 32, 32);
            border = Color.FromArgb(48, 48, 48);
            chevronCol = Color.FromArgb(90, 90, 90);
            return;
        }

        bg = isHovered ? Color.FromArgb(56, 56, 56) : Color.FromArgb(42, 42, 42);
        if (isOpen)
        {
            border = accent;
            chevronCol = accent;
        }
        else
        {
            border = isHovered ? Color.FromArgb(100, 100, 100) : Color.FromArgb(68, 68, 68);
            chevronCol = Color.FromArgb(170, 175, 185);
        }
    }

    private void PaintOpenDropdownOverlay(Graphics g, float s)
    {
        var popupRect = GetOpenDropdownPopupRect();
        var scaledPopup = ScalePrintRect(popupRect, s);

        // Drop shadow for floating popup
        using (var shadowBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
        {
            g.FillRectangle(shadowBrush, scaledPopup.X + (3f * s), scaledPopup.Y + (3f * s), scaledPopup.Width, scaledPopup.Height);
        }

        // Popup background & border
        using (var popupPath = UIHelper.GetRoundedRectPath(scaledPopup, 4 * s))
        using (var bgBrush = new SolidBrush(Color.FromArgb(32, 32, 34)))
        using (var borderPen = new Pen(Color.FromArgb(75, 80, 90), 1f * s))
        {
            g.FillPath(bgBrush, popupPath);
            g.DrawPath(borderPen, popupPath);
        }

        var items = GetOpenDropdownItems();
        int selectedIdx = GetOpenDropdownSelectedIndex();
        int maxVisible = GetOpenDropdownMaxVisible();

        if (_dropdownScrollOffset < 0) _dropdownScrollOffset = 0;
        int maxScroll = Math.Max(0, items.Count - maxVisible);
        if (_dropdownScrollOffset > maxScroll) _dropdownScrollOffset = maxScroll;

        int drawCount = Math.Min(maxVisible, items.Count - _dropdownScrollOffset);
        float listStartY = (popupRect.Y + 3) * s;
        float itemH = PrintDropdownItemHeight * s;
        float itemW = (popupRect.Width - (items.Count > maxVisible ? 12 : 6)) * s;
        float itemX = (popupRect.X + 3) * s;

        Color accent = UIHelper.GetSystemColorizationColor();

        for (int i = 0; i < drawCount; i++)
        {
            int itemIdx = _dropdownScrollOffset + i;
            float curItemY = listStartY + (i * itemH);
            bool isHovered = itemIdx == _hoveredDropdownIndex;
            bool isSelected = itemIdx == selectedIdx;

            var itemRect = new RectangleF(itemX, curItemY, itemW, itemH);

            if (isHovered)
            {
                using var itemBg = new SolidBrush(Color.FromArgb(54, 58, 68));
                using var itemPath = UIHelper.GetRoundedRectPath(itemRect, 3 * s);
                g.FillPath(itemBg, itemPath);
            }
            else if (isSelected)
            {
                using var itemBg = new SolidBrush(Color.FromArgb(40, 44, 52));
                using var itemPath = UIHelper.GetRoundedRectPath(itemRect, 3 * s);
                g.FillPath(itemBg, itemPath);
            }

            // Draw checkmark if selected
            if (isSelected)
            {
                VectorIcons.DrawSuccessCheckmark(g, itemX + (4f * s), curItemY + (3f * s), itemH - (6f * s), accent, 1.8f * s);
            }

            float textLeft = itemX + (isSelected ? 22f * s : 8f * s);
            var textRect = new RectangleF(textLeft, curItemY, itemW - (isSelected ? 24f * s : 10f * s), itemH);
            Color textCol = GetDropdownItemTextColor(isHovered, isSelected);
            using var textBrush = new SolidBrush(textCol);
            using var format = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            g.DrawString(items[itemIdx], _msgFont ?? SystemFonts.DefaultFont, textBrush, textRect, format);
        }

        PaintDropdownScrollbar(g, s, popupRect, items.Count, maxVisible, maxScroll);
    }

    private static Color GetDropdownItemTextColor(bool isHovered, bool isSelected)
    {
        if (isHovered) return Color.White;
        if (isSelected) return Color.FromArgb(240, 240, 240);
        return Color.FromArgb(195, 200, 210);
    }

    private void PaintDropdownScrollbar(Graphics g, float s, LayoutRect popupRect, int itemsCount, int maxVisible, int maxScroll)
    {
        if (itemsCount <= maxVisible) return;

        float trackX = (popupRect.Right - 7) * s;
        float trackY = (popupRect.Y + 4) * s;
        float trackW = 4f * s;
        float trackH = (popupRect.Height - 8) * s;

        using (var trackBrush = new SolidBrush(Color.FromArgb(42, 42, 45)))
        {
            g.FillRectangle(trackBrush, trackX, trackY, trackW, trackH);
        }

        float thumbH = Math.Max(16f * s, ((float)maxVisible / itemsCount) * trackH);
        float thumbY = trackY + (((float)_dropdownScrollOffset / maxScroll) * (trackH - thumbH));

        using var thumbBrush = new SolidBrush(Color.FromArgb(110, 115, 125));
        using var thumbPath = UIHelper.GetRoundedRectPath(new RectangleF(trackX, thumbY, trackW, thumbH), 2 * s);
        g.FillPath(thumbBrush, thumbPath);
    }

    private void HandleOpenDropdownClick(IntPtr hwnd, int mouseX, int mouseY)
    {
        if (TrySelectDropdownItem(hwnd, mouseX, mouseY) ||
            TryHandleDropdownToggles(hwnd, mouseX, mouseY))
        {
            return;
        }

        // Clicked outside popup: dismiss
        _openDropdown = PrintDropdownType.None;
        _hoveredDropdownIndex = -1;
        InvalidateRect(hwnd, IntPtr.Zero, false);
    }

    private bool TrySelectDropdownItem(IntPtr hwnd, int mouseX, int mouseY)
    {
        var popupRect = GetOpenDropdownPopupRect();
        if (!popupRect.ContainsInclusive(mouseX, mouseY)) return false;

        int listTop = popupRect.Y + 3;
        int clickedRow = (mouseY - listTop) / PrintDropdownItemHeight;
        int itemIdx = _dropdownScrollOffset + clickedRow;
        var items = GetOpenDropdownItems();

        if (itemIdx >= 0 && itemIdx < items.Count)
        {
            ApplyDropdownSelection(itemIdx);
        }

        _openDropdown = PrintDropdownType.None;
        _hoveredDropdownIndex = -1;
        InvalidateRect(hwnd, IntPtr.Zero, false);
        return true;
    }

    private void ApplyDropdownSelection(int itemIdx)
    {
        switch (_openDropdown)
        {
            case PrintDropdownType.Printer:
                _printPrinterIndex = itemIdx;
                _printPrinterName = _installedPrinters[itemIdx];
                RefreshPrinterCapabilities(_printerDiscovery);
                break;

            case PrintDropdownType.PaperSize:
                _paperSizeIndex = itemIdx;
                break;

            case PrintDropdownType.Duplex:
                _duplexIndex = Math.Clamp(itemIdx, 0, 2);
                break;
        }
    }

    private static PrintElement GetPrintElementAt(int mouseX, int mouseY)
    {
        if (PrintOptionsLayout.PrinterComboRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.PrinterCombo;
        if (PrintOptionsLayout.PaperComboRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.PaperCombo;
        if (PrintOptionsLayout.DuplexComboRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.DuplexCombo;

        if (PrintOptionsLayout.RangeAllRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.RangeAll;
        if (PrintOptionsLayout.RangeCustomRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.RangeCustom;

        if (PrintOptionsLayout.CopiesMinusRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.MinusCopies;
        if (PrintOptionsLayout.CopiesPlusRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.PlusCopies;

        if (PrintOptionsLayout.FitToPageRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.FitToPage;
        if (PrintOptionsLayout.DarkenRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.Darken;

        if (PrintOptionsLayout.PreviewArea.ContainsInclusive(mouseX, mouseY)) return PrintElement.PreviewArea;

        if (PrintOptionsLayout.PrevPageButton.ContainsInclusive(mouseX, mouseY)) return PrintElement.PrevPage;
        if (PrintOptionsLayout.NextPageButton.ContainsInclusive(mouseX, mouseY)) return PrintElement.NextPage;

        if (PrintOptionsLayout.ConfirmButton.ContainsInclusive(mouseX, mouseY)) return PrintElement.Confirm;
        if (PrintOptionsLayout.CancelButton.ContainsInclusive(mouseX, mouseY)) return PrintElement.Cancel;

        return PrintElement.None;
    }

    private void HandlePrintOptionsClick(IntPtr hwnd, int mouseX, int mouseY)
    {
        if (_openDropdown != PrintDropdownType.None)
        {
            HandleOpenDropdownClick(hwnd, mouseX, mouseY);
            return;
        }

        _ = TryHandleDropdownToggles(hwnd, mouseX, mouseY) ||
            TryHandleRangeAndCopies(hwnd, mouseX, mouseY) ||
            TryHandleCheckboxAndPreviewClicks(hwnd, mouseX, mouseY) ||
            TryHandlePaginationAndActions(hwnd, mouseX, mouseY);
    }

    private bool TryHandleDropdownToggles(IntPtr hwnd, int mouseX, int mouseY)
    {
        if (PrintOptionsLayout.PrinterComboRect.ContainsInclusive(mouseX, mouseY))
            return TogglePrinterDropdown(hwnd);

        if (PrintOptionsLayout.PaperComboRect.ContainsInclusive(mouseX, mouseY))
            return TogglePaperDropdown(hwnd);

        if (PrintOptionsLayout.DuplexComboRect.ContainsInclusive(mouseX, mouseY))
            return ToggleDuplexDropdown(hwnd);

        return false;
    }

    private bool TogglePrinterDropdown(IntPtr hwnd)
    {
        if (_installedPrinters.Count > 0)
        {
            _openDropdown = _openDropdown == PrintDropdownType.Printer ? PrintDropdownType.None : PrintDropdownType.Printer;
            _dropdownScrollOffset = 0;
            _hoveredDropdownIndex = _printPrinterIndex;
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }
        return true;
    }

    private bool TogglePaperDropdown(IntPtr hwnd)
    {
        if (_supportedPaperSizes.Count > 0)
        {
            _openDropdown = _openDropdown == PrintDropdownType.PaperSize ? PrintDropdownType.None : PrintDropdownType.PaperSize;
            int maxVis = GetOpenDropdownMaxVisible();
            _dropdownScrollOffset = Math.Max(0, Math.Min(_paperSizeIndex, _supportedPaperSizes.Count - maxVis));
            _hoveredDropdownIndex = _paperSizeIndex;
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }
        return true;
    }

    private bool ToggleDuplexDropdown(IntPtr hwnd)
    {
        if (_printerCanDuplex)
        {
            _openDropdown = _openDropdown == PrintDropdownType.Duplex ? PrintDropdownType.None : PrintDropdownType.Duplex;
            _dropdownScrollOffset = 0;
            _hoveredDropdownIndex = _duplexIndex;
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }
        return true;
    }

    private bool TryHandleRangeAndCopies(IntPtr hwnd, int mouseX, int mouseY)
    {
        if (PrintOptionsLayout.RangeAllRect.ContainsInclusive(mouseX, mouseY))
        {
            _printPageRange = "all";
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return true;
        }
        if (PrintOptionsLayout.RangeCustomRect.ContainsInclusive(mouseX, mouseY))
        {
            string defaultPrompt = string.Equals(_printPageRange, "all", StringComparison.OrdinalIgnoreCase)
                ? $"1-{_printPreviewModel.TotalPages}"
                : _printPageRange;
            string? input = NativeDialogs.PromptText(
                hwnd,
                Loc("progress_sub_print_options"),
                Loc("print_options_range_prompt"),
                defaultPrompt,
                isPassword: false,
                _dpiScale,
                SettingsPresentationController.Shared.Language);
            if (!string.IsNullOrWhiteSpace(input))
            {
                _printPageRange = input.Trim();
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return true;
        }

        if (PrintOptionsLayout.CopiesMinusRect.ContainsInclusive(mouseX, mouseY))
        {
            if (_printCopies > 1)
            {
                _printCopies--;
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return true;
        }
        if (PrintOptionsLayout.CopiesPlusRect.ContainsInclusive(mouseX, mouseY))
        {
            if (_printCopies < 99)
            {
                _printCopies++;
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return true;
        }

        return false;
    }

    private bool TryHandleCheckboxAndPreviewClicks(IntPtr hwnd, int mouseX, int mouseY)
    {
        if (PrintOptionsLayout.FitToPageRect.ContainsInclusive(mouseX, mouseY))
        {
            _printFitToPage = !_printFitToPage;
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return true;
        }

        if (PrintOptionsLayout.DarkenRect.ContainsInclusive(mouseX, mouseY))
        {
            _printDarken = !_printDarken;
            lock (_printThumbnails)
            {
                _printThumbnails.Clear();
                _printPendingThumbnails.Clear();
            }
            RequestPrintPreviewThumbnail(_printPreviewModel.CurrentPageNumber);
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return true;
        }

        if (PrintOptionsLayout.PreviewArea.ContainsInclusive(mouseX, mouseY))
        {
            OpenPrintZoom(hwnd);
            return true;
        }

        return false;
    }

    private bool TryHandlePaginationAndActions(IntPtr hwnd, int mouseX, int mouseY)
    {
        if (PrintOptionsLayout.PrevPageButton.ContainsInclusive(mouseX, mouseY))
        {
            if (_printPreviewModel.CanNavigatePrevious)
            {
                _printPreviewModel.Navigate(-1);
                RequestPrintPreviewThumbnail(_printPreviewModel.CurrentPageNumber);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return true;
        }
        if (PrintOptionsLayout.NextPageButton.ContainsInclusive(mouseX, mouseY))
        {
            if (_printPreviewModel.CanNavigateNext)
            {
                _printPreviewModel.Navigate(1);
                RequestPrintPreviewThumbnail(_printPreviewModel.CurrentPageNumber);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return true;
        }

        if (PrintOptionsLayout.ConfirmButton.ContainsInclusive(mouseX, mouseY))
        {
            ConfirmPrint(hwnd);
            return true;
        }
        if (PrintOptionsLayout.CancelButton.ContainsInclusive(mouseX, mouseY))
        {
            CancelPrint(hwnd);
            return true;
        }

        return false;
    }

    private void ConfirmPrint(IntPtr hwnd)
    {
        string? selectedPaper = _supportedPaperSizes.Count > 0 && _paperSizeIndex >= 0 && _paperSizeIndex < _supportedPaperSizes.Count
            ? _supportedPaperSizes[_paperSizeIndex]
            : null;
        var duplexMode = _printerCanDuplex ? (PrintDuplexMode)_duplexIndex : PrintDuplexMode.Simplex;

        _commandOptions = new PrintPdfOptions(
            _printPrinterName,
            _printPageRange,
            _printCopies,
            200,
            _printFitToPage,
            SettingsPresentationController.Shared.Language,
            _printDarken,
            2.2,
            duplexMode,
            selectedPaper).ToDictionary();

        ResolvePrintDecision(true);
        _isPromptingPrintOptions = false;
        _progressPresentation.ResumeRunning();
        ResizeWindowForPrintOptions(hwnd, false);
        StartProcessingThread(hwnd);
    }

    private void CancelPrint(IntPtr hwnd)
    {
        ResolvePrintDecision(false);
        _isPromptingPrintOptions = false;
        DestroyWindow(hwnd);
    }

    private void ResizeWindowForPrintOptions(IntPtr hwnd, bool expand)
    {
        float s = _dpiScale;
        int clientW = (int)(PrintOptionsLayout.ClientWidth * s);
        int clientH = (int)((expand ? PrintOptionsClientHeight : 280) * s);
        ResizeClientSurface(hwnd, clientW, clientH);
    }

    public void OpenPrintZoom(IntPtr hwnd)
    {
        _printIsZoomed = true;
        _printZoomFactor = 1f;
        _printZoomPanX = 0f;
        _printZoomPanY = 0f;
        _printZoomDragging = false;
        InvalidateRect(hwnd, IntPtr.Zero, true);
    }

    public void ClosePrintZoom(IntPtr hwnd)
    {
        _printIsZoomed = false;
        _printZoomFactor = 1f;
        _printZoomPanX = 0f;
        _printZoomPanY = 0f;
        _printZoomDragging = false;
        InvalidateRect(hwnd, IntPtr.Zero, true);
    }

    private void SetPrintZoomFactor(float newFactor, float anchorX, float anchorY)
    {
        float oldFactor = _printZoomFactor;
        _printZoomFactor = Math.Clamp(newFactor, 1f, 5f);
        if (Math.Abs(_printZoomFactor - oldFactor) > 0.001f)
        {
            float ratio = _printZoomFactor / oldFactor;
            _printZoomPanX = anchorX - (ratio * (anchorX - _printZoomPanX));
            _printZoomPanY = anchorY - (ratio * (anchorY - _printZoomPanY));
            ClampPrintZoomPan();
        }
    }

    private void ClampPrintZoomPan()
    {
        float fitW = PrintZoomImgW;
        float fitH = PrintZoomImgH;
        float contentW = fitW * _printZoomFactor;
        float contentH = fitH * _printZoomFactor;

        float maxPanX = Math.Max(0f, (contentW - fitW) / 2f);
        float maxPanY = Math.Max(0f, (contentH - fitH) / 2f);

        _printZoomPanX = Math.Clamp(_printZoomPanX, -maxPanX, maxPanX);
        _printZoomPanY = Math.Clamp(_printZoomPanY, -maxPanY, maxPanY);
    }

    internal static float ZoomFactorStep(float current, int direction) =>
        direction > 0
            ? (float)Math.Round(Math.Min(5f, current * 1.25f), 2)
            : (float)Math.Round(Math.Max(1f, current / 1.25f), 2);

    private bool HandlePrintZoomClick(IntPtr hwnd, int mouseX, int mouseY)
    {
        if (!_printIsZoomed) return false;

        // Check close button
        if (PrintOptionsLayout.ZoomCloseButton.ContainsInclusive(mouseX, mouseY))
        {
            ClosePrintZoom(hwnd);
            return true;
        }

        if (TryHandleZoomButtons(hwnd, mouseX, mouseY) ||
            TryHandleZoomViewportDrag(hwnd, mouseX, mouseY))
        {
            return true;
        }

        // Click outside modal closes zoom
        if (!PrintOptionsLayout.ZoomModal.ContainsInclusive(mouseX, mouseY))
        {
            ClosePrintZoom(hwnd);
        }

        return true;
    }

    private bool TryHandleZoomButtons(IntPtr hwnd, int mouseX, int mouseY)
    {
        float zoomBtnY = PrintOptionsLayout.ZoomButton(0).Y;
        float zoomBtnH = PrintOptionsLayout.ZoomButton(0).Height;
        if (mouseY < zoomBtnY || mouseY > zoomBtnY + zoomBtnH) return false;

        float btnOutX = PrintOptionsLayout.ZoomButton(0).X;
        float btnInX = PrintOptionsLayout.ZoomButton(1).X;
        float btnFitX = PrintOptionsLayout.ZoomButton(2).X;
        float cx = PrintZoomImgLeft + (PrintZoomImgW / 2f);
        float cy = PrintZoomImgTop + (PrintZoomImgH / 2f);

        if (mouseX >= btnOutX && mouseX <= btnOutX + PrintOptionsLayout.ZoomButton(0).Width)
        {
            SetPrintZoomFactor(ZoomFactorStep(_printZoomFactor, -1), cx, cy);
            InvalidateRect(hwnd, IntPtr.Zero, true);
            return true;
        }
        if (mouseX >= btnInX && mouseX <= btnInX + PrintOptionsLayout.ZoomButton(1).Width)
        {
            SetPrintZoomFactor(ZoomFactorStep(_printZoomFactor, +1), cx, cy);
            InvalidateRect(hwnd, IntPtr.Zero, true);
            return true;
        }
        if (mouseX >= btnFitX && mouseX <= btnFitX + PrintOptionsLayout.ZoomButton(2).Width)
        {
            _printZoomFactor = 1f;
            _printZoomPanX = 0f;
            _printZoomPanY = 0f;
            InvalidateRect(hwnd, IntPtr.Zero, true);
            return true;
        }

        return false;
    }

    private bool TryHandleZoomViewportDrag(IntPtr hwnd, int mouseX, int mouseY)
    {
        if (mouseX >= PrintZoomImgLeft && mouseX <= PrintZoomImgLeft + PrintZoomImgW &&
            mouseY >= PrintZoomImgTop && mouseY <= PrintZoomImgTop + PrintZoomImgH)
        {
            _printZoomDragging = true;
            _printZoomDragLastX = mouseX;
            _printZoomDragLastY = mouseY;
            SetCapture(hwnd);
            return true;
        }
        return false;
    }

    private void PaintPrintZoomOverlay(Graphics g, float s)
    {
        if (!_printIsZoomed) return;
        if (_tipFont is null || _msgFont is null) return;

        // 1. Dimmed backdrop
        using var overlayBg = new SolidBrush(Color.FromArgb(235, 15, 18, 24));
        g.FillRectangle(overlayBg, 0, 0, PrintOptionsLayout.ClientWidth * s, PrintOptionsClientHeight * s);

        // 2. Modal frame
        var modalRect = ScalePrintRect(PrintOptionsLayout.ZoomModal, s);
        using var modalBg = new SolidBrush(Color.FromArgb(32, 35, 42));
        using var modalPen = new Pen(Color.FromArgb(80, 90, 110), 1.5f * s);
        g.FillRectangle(modalBg, modalRect);
        g.DrawRectangle(modalPen, modalRect.X, modalRect.Y, modalRect.Width, modalRect.Height);

        // 3. Title
        string currentPaperText = _supportedPaperSizes.Count > 0 && _paperSizeIndex >= 0 && _paperSizeIndex < _supportedPaperSizes.Count
            ? _supportedPaperSizes[_paperSizeIndex]
            : "A4";
        var paperDim = PaperSizeRegistry.GetDimensions(currentPaperText);
        int zoomPct = (int)(_printZoomFactor * 100);
        int curPage = _printPreviewModel.CurrentPageNumber;
        using var titleBrush = new SolidBrush(Color.FromArgb(230, 240, 255));
        g.DrawString($"{Loc("pdf_split_zoom_title")} · P.{curPage} · {zoomPct}% · {paperDim.DisplayName}", _msgFont, titleBrush, modalRect.X + (16f * s), modalRect.Y + (12f * s));

        // 4. Close button
        var closeRect = ScalePrintRect(PrintOptionsLayout.ZoomCloseButton, s);
        using var closeBg = new SolidBrush(Color.FromArgb(180, 45, 40));
        using var closeTextBrush = new SolidBrush(Color.White);
        g.FillRectangle(closeBg, closeRect);
        using var closeFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(Loc("pdf_split_zoom_close"), _tipFont, closeTextBrush, closeRect, closeFormat);

        // 5. Image Area Viewport
        var imgViewport = ScalePrintRect(new LayoutRect((int)PrintZoomImgLeft, (int)PrintZoomImgTop, (int)PrintZoomImgW, (int)PrintZoomImgH), s);
        using var viewportBg = new SolidBrush(Color.FromArgb(20, 22, 28));
        g.FillRectangle(viewportBg, imgViewport);

        // Clip to viewport
        var prevClip = g.Clip;
        g.SetClip(imgViewport);

        // Paper dimensions & orientation
        int curPageNum = _printPreviewModel.CurrentPageNumber;
        Bitmap? bmp;
        lock (_printThumbnails)
        {
            _printThumbnails.TryGetValue(curPageNum, out bmp);
        }

        bool isLandscape = _pdfPageWidthPt > _pdfPageHeightPt || (bmp != null && bmp.Width > bmp.Height);
        float targetPaperW = isLandscape ? Math.Max(paperDim.WidthPt, paperDim.HeightPt) : Math.Min(paperDim.WidthPt, paperDim.HeightPt);
        float targetPaperH = isLandscape ? Math.Min(paperDim.WidthPt, paperDim.HeightPt) : Math.Max(paperDim.WidthPt, paperDim.HeightPt);

        // Base sheet size inside viewport
        float maxBaseW = imgViewport.Width - (32f * s);
        float maxBaseH = imgViewport.Height - (32f * s);
        float baseScale = Math.Min(maxBaseW / targetPaperW, maxBaseH / targetPaperH);
        float baseSheetW = targetPaperW * baseScale;
        float baseSheetH = targetPaperH * baseScale;

        // Apply zoom factor and pan
        float sheetW = baseSheetW * _printZoomFactor;
        float sheetH = baseSheetH * _printZoomFactor;
        float sheetX = imgViewport.X + ((imgViewport.Width - sheetW) / 2f) + (_printZoomPanX * s);
        float sheetY = imgViewport.Y + ((imgViewport.Height - sheetH) / 2f) + (_printZoomPanY * s);

        // Paper drop shadow
        using var shadowBrush = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
        g.FillRectangle(shadowBrush, sheetX + (4f * s), sheetY + (4f * s), sheetW, sheetH);

        // Paper sheet canvas
        using var sheetBrush = new SolidBrush(Color.White);
        g.FillRectangle(sheetBrush, sheetX, sheetY, sheetW, sheetH);
        using var sheetPen = new Pen(Color.FromArgb(160, 175, 195), 1.5f * s);
        g.DrawRectangle(sheetPen, sheetX, sheetY, sheetW, sheetH);

        // PDF Page drawing onto sheet
        if (bmp != null)
        {
            var pageBounds = CalculatePageDrawBounds(bmp, new RectangleF(sheetX, sheetY, sheetW, sheetH), _printFitToPage, _pdfPageWidthPt, _pdfPageHeightPt, new SizeF(targetPaperW, targetPaperH));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(bmp, pageBounds.X, pageBounds.Y, pageBounds.Width, pageBounds.Height);
        }

        g.Clip = prevClip;
        using var viewportBorderPen = new Pen(Color.FromArgb(60, 65, 75), 1f * s);
        g.DrawRectangle(viewportBorderPen, imgViewport.X, imgViewport.Y, imgViewport.Width, imgViewport.Height);

        // 6. Bottom controls: zoom in/out, fit, hint
        float zoomBtnY = PrintOptionsLayout.ZoomButton(0).Y * s;
        float zoomBtnH = PrintOptionsLayout.ZoomButton(0).Height * s;
        float zoomBtnOutX = PrintOptionsLayout.ZoomButton(0).X * s;
        float zoomBtnInX = PrintOptionsLayout.ZoomButton(1).X * s;
        float zoomBtnFitX = PrintOptionsLayout.ZoomButton(2).X * s;
        float zoomBtnW = PrintOptionsLayout.ZoomButton(0).Width * s;
        float zoomBtnFitW = PrintOptionsLayout.ZoomButton(2).Width * s;

        using var zoomBtnBg = new SolidBrush(Color.FromArgb(48, 48, 48));
        using var zoomBtnPen = new Pen(Color.FromArgb(80, 80, 80));
        using var zoomBtnText = new SolidBrush(Color.FromArgb(220, 220, 220));

        g.FillRectangle(zoomBtnBg, zoomBtnOutX, zoomBtnY, zoomBtnW, zoomBtnH);
        g.DrawRectangle(zoomBtnPen, zoomBtnOutX, zoomBtnY, zoomBtnW, zoomBtnH);
        VectorIcons.DrawMinus(g, zoomBtnOutX + (zoomBtnW / 2f), zoomBtnY + (zoomBtnH / 2f), 4 * s, zoomBtnText.Color, 1.5f * s);

        g.FillRectangle(zoomBtnBg, zoomBtnInX, zoomBtnY, zoomBtnW, zoomBtnH);
        g.DrawRectangle(zoomBtnPen, zoomBtnInX, zoomBtnY, zoomBtnW, zoomBtnH);
        VectorIcons.DrawPlus(g, zoomBtnInX + (zoomBtnW / 2f), zoomBtnY + (zoomBtnH / 2f), 4 * s, zoomBtnText.Color, 1.5f * s);

        g.FillRectangle(zoomBtnBg, zoomBtnFitX, zoomBtnY, zoomBtnFitW, zoomBtnH);
        g.DrawRectangle(zoomBtnPen, zoomBtnFitX, zoomBtnY, zoomBtnFitW, zoomBtnH);
        g.DrawString(Loc("pdf_split_zoom_fit"), _tipFont, zoomBtnText, zoomBtnFitX + (8f * s), zoomBtnY + (2f * s));

        using var zoomHintBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
        g.DrawString(Loc("pdf_split_zoom_hint"), _tipFont, zoomHintBrush, modalRect.X + (16f * s), zoomBtnY + (3f * s));
    }

    private static RectangleF CalculatePageDrawBounds(
        Bitmap bmp,
        RectangleF sheetRect,
        bool fitToPage,
        float pdfPageWidthPt, float pdfPageHeightPt,
        SizeF targetPaperSize)
    {
        float pageDrawW;
        float pageDrawH;
        float targetPaperW = Math.Max(1f, targetPaperSize.Width);
        float targetPaperH = Math.Max(1f, targetPaperSize.Height);
        if (fitToPage)
        {
            float margin = Math.Min(sheetRect.Width, sheetRect.Height) * 0.03f;
            float printAreaW = sheetRect.Width - (margin * 2f);
            float printAreaH = sheetRect.Height - (margin * 2f);
            float fitScale = Math.Min(printAreaW / bmp.Width, printAreaH / bmp.Height);
            pageDrawW = bmp.Width * fitScale;
            pageDrawH = bmp.Height * fitScale;
        }
        else
        {
            float scaleX = pdfPageWidthPt / targetPaperW;
            float scaleY = pdfPageHeightPt / targetPaperH;
            pageDrawW = sheetRect.Width * scaleX;
            pageDrawH = sheetRect.Height * scaleY;
        }

        float pageDrawX = sheetRect.X + ((sheetRect.Width - pageDrawW) / 2f);
        float pageDrawY = sheetRect.Y + ((sheetRect.Height - pageDrawH) / 2f);
        return new RectangleF(pageDrawX, pageDrawY, pageDrawW, pageDrawH);
    }
}

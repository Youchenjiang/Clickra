using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Clickra.Core;
using Clickra.Core.Modules.PdfPrint.Application;
using Clickra.Core.Modules.PdfPrint.Ports;
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

    private PdfPreviewModel _printPreviewModel = new(1);
    private enum PrintElement
    {
        None = 0,
        PrevPrinter,
        PrinterName,
        NextPrinter,
        PrevPaper,
        PaperName,
        NextPaper,
        PrevDuplex,
        DuplexName,
        NextDuplex,
        RangeAll,
        RangeCustom,
        MinusCopies,
        PlusCopies,
        FitToPage,
        Darken,
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
                    _printerDiscovery.GetSupportedPaperSizes(printer);
                    _printerDiscovery.CanDuplex(printer);
                }
            });
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

        Task.Run(() =>
        {
            Bitmap? bmp = null;
            try
            {
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
        });
    }

    private static RectangleF ScalePrintRect(LayoutRect rect, float scale) =>
        new(rect.X * scale, rect.Y * scale, rect.Width * scale, rect.Height * scale);

    private void PaintPrintOptions(Graphics g, float s)
    {
        if (_msgFont is null || _tipFont is null) return;

        using var bodyBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
        using var mutedBrush = new SolidBrush(Color.FromArgb(145, 145, 145));

        // 1. Printer Row
        DrawPrintLabel(g, s, Loc("print_options_printer"), PrintOptionsLayout.PrinterLabelY, bodyBrush);
        var prevPrinterRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.PrinterComboY, 26, PrintOptionsLayout.PrinterComboHeight);
        var printerNameRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 28, PrintOptionsLayout.PrinterComboY, PrintOptionsLayout.LeftColumnWidth - 56, PrintOptionsLayout.PrinterComboHeight);
        var nextPrinterRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + PrintOptionsLayout.LeftColumnWidth - 26, PrintOptionsLayout.PrinterComboY, 26, PrintOptionsLayout.PrinterComboHeight);

        DrawPrintButton(g, s, prevPrinterRect, "<", false, enabled: _installedPrinters.Count > 1,
            hovered: _hoveredPrintElement == PrintElement.PrevPrinter,
            pressed: _pressedPrintElement == PrintElement.PrevPrinter);
        DrawPrintButton(g, s, printerNameRect, _printPrinterName, true,
            hovered: _hoveredPrintElement == PrintElement.PrinterName,
            pressed: _pressedPrintElement == PrintElement.PrinterName);
        DrawPrintButton(g, s, nextPrinterRect, ">", false, enabled: _installedPrinters.Count > 1,
            hovered: _hoveredPrintElement == PrintElement.NextPrinter,
            pressed: _pressedPrintElement == PrintElement.NextPrinter);

        // 2. Paper Size Row
        DrawPrintLabel(g, s, Loc("print_options_paper_size"), PrintOptionsLayout.PaperLabelY, bodyBrush);
        var prevPaperRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.PaperComboY, 26, PrintOptionsLayout.PaperComboHeight);
        var paperNameRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 28, PrintOptionsLayout.PaperComboY, PrintOptionsLayout.LeftColumnWidth - 56, PrintOptionsLayout.PaperComboHeight);
        var nextPaperRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + PrintOptionsLayout.LeftColumnWidth - 26, PrintOptionsLayout.PaperComboY, 26, PrintOptionsLayout.PaperComboHeight);
        string currentPaperText = _supportedPaperSizes.Count > 0 && _paperSizeIndex >= 0 && _paperSizeIndex < _supportedPaperSizes.Count
            ? _supportedPaperSizes[_paperSizeIndex]
            : "A4";

        DrawPrintButton(g, s, prevPaperRect, "<", false, enabled: _supportedPaperSizes.Count > 1,
            hovered: _hoveredPrintElement == PrintElement.PrevPaper,
            pressed: _pressedPrintElement == PrintElement.PrevPaper);
        DrawPrintButton(g, s, paperNameRect, currentPaperText, true,
            hovered: _hoveredPrintElement == PrintElement.PaperName,
            pressed: _pressedPrintElement == PrintElement.PaperName);
        DrawPrintButton(g, s, nextPaperRect, ">", false, enabled: _supportedPaperSizes.Count > 1,
            hovered: _hoveredPrintElement == PrintElement.NextPaper,
            pressed: _pressedPrintElement == PrintElement.NextPaper);

        // 3. Duplex Row
        DrawPrintLabel(g, s, Loc("print_options_duplex"), PrintOptionsLayout.DuplexLabelY, bodyBrush);
        var prevDuplexRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.DuplexComboY, 26, PrintOptionsLayout.DuplexComboHeight);
        var duplexNameRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 28, PrintOptionsLayout.DuplexComboY, PrintOptionsLayout.LeftColumnWidth - 56, PrintOptionsLayout.DuplexComboHeight);
        var nextDuplexRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + PrintOptionsLayout.LeftColumnWidth - 26, PrintOptionsLayout.DuplexComboY, 26, PrintOptionsLayout.DuplexComboHeight);
        string currentDuplexText = !_printerCanDuplex
            ? Loc("print_options_duplex_unsupported")
            : _duplexIndex switch
            {
                1 => Loc("print_options_duplex_long"),
                2 => Loc("print_options_duplex_short"),
                _ => Loc("print_options_duplex_simplex")
            };

        DrawPrintButton(g, s, prevDuplexRect, "<", false, enabled: _printerCanDuplex,
            hovered: _hoveredPrintElement == PrintElement.PrevDuplex,
            pressed: _pressedPrintElement == PrintElement.PrevDuplex);
        DrawPrintButton(g, s, duplexNameRect, currentDuplexText, true, enabled: _printerCanDuplex,
            hovered: _hoveredPrintElement == PrintElement.DuplexName,
            pressed: _pressedPrintElement == PrintElement.DuplexName);
        DrawPrintButton(g, s, nextDuplexRect, ">", false, enabled: _printerCanDuplex,
            hovered: _hoveredPrintElement == PrintElement.NextDuplex,
            pressed: _pressedPrintElement == PrintElement.NextDuplex);

        // 4. Page Range Row
        DrawPrintLabel(g, s, Loc("print_options_range"), PrintOptionsLayout.RangeLabelY, bodyBrush);
        var allPagesRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.RangeInputY, 68, PrintOptionsLayout.RangeInputHeight);
        var customPagesRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 74, PrintOptionsLayout.RangeInputY, PrintOptionsLayout.LeftColumnWidth - 74, PrintOptionsLayout.RangeInputHeight);

        bool isAll = string.Equals(_printPageRange, "all", StringComparison.OrdinalIgnoreCase);
        DrawPrintButton(g, s, allPagesRect, Loc("print_options_range_all"), isAll,
            hovered: _hoveredPrintElement == PrintElement.RangeAll,
            pressed: _pressedPrintElement == PrintElement.RangeAll);
        DrawPrintButton(g, s, customPagesRect, isAll ? Loc("print_options_range_custom") : _printPageRange, !isAll,
            hovered: _hoveredPrintElement == PrintElement.RangeCustom,
            pressed: _pressedPrintElement == PrintElement.RangeCustom);

        // 5. Copies Row
        DrawPrintLabel(g, s, Loc("print_options_copies"), PrintOptionsLayout.CopiesLabelY, bodyBrush);
        var minusCopiesRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.CopiesInputY, 32, PrintOptionsLayout.CopiesInputHeight);
        var copiesDisplayRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 36, PrintOptionsLayout.CopiesInputY, 56, PrintOptionsLayout.CopiesInputHeight);
        var plusCopiesRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 96, PrintOptionsLayout.CopiesInputY, 32, PrintOptionsLayout.CopiesInputHeight);

        DrawPrintButton(g, s, minusCopiesRect, "-", false, enabled: _printCopies > 1,
            hovered: _hoveredPrintElement == PrintElement.MinusCopies,
            pressed: _pressedPrintElement == PrintElement.MinusCopies);
        DrawPrintDisplayBox(g, s, copiesDisplayRect, _printCopies.ToString());
        DrawPrintButton(g, s, plusCopiesRect, "+", false, enabled: _printCopies < 99,
            hovered: _hoveredPrintElement == PrintElement.PlusCopies,
            pressed: _pressedPrintElement == PrintElement.PlusCopies);

        // 6. Fit to Page Checkbox
        var fitRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.FitToPageY, PrintOptionsLayout.LeftColumnWidth, PrintOptionsLayout.FitToPageHeight);
        DrawPrintCheckbox(g, s, fitRect, Loc("print_options_fit"), _printFitToPage,
            hovered: _hoveredPrintElement == PrintElement.FitToPage);

        // 7. Darken Text Checkbox
        var darkenRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.DarkenY, PrintOptionsLayout.LeftColumnWidth, PrintOptionsLayout.DarkenHeight);
        DrawPrintCheckbox(g, s, darkenRect, Loc("print_options_darken"), _printDarken,
            hovered: _hoveredPrintElement == PrintElement.Darken);

        // 6. Preview Area
        var previewAreaRect = ScalePrintRect(PrintOptionsLayout.PreviewArea, s);
        using var previewBgBrush = new SolidBrush(Color.FromArgb(32, 32, 32));
        using var previewBorderPen = new Pen(Color.FromArgb(64, 64, 64), 1f * s);
        g.FillRectangle(previewBgBrush, previewAreaRect);
        g.DrawRectangle(previewBorderPen, previewAreaRect.X, previewAreaRect.Y, previewAreaRect.Width, previewAreaRect.Height);

        int curPage = _printPreviewModel.CurrentPageNumber;
        Bitmap? bmp;
        lock (_printThumbnails)
        {
            _printThumbnails.TryGetValue(curPage, out bmp);
        }

        if (bmp != null)
        {
            float pad = 6f * s;
            float maxW = previewAreaRect.Width - (pad * 2);
            float maxH = previewAreaRect.Height - (pad * 2);
            float scale = Math.Min(maxW / bmp.Width, maxH / bmp.Height);
            float drawW = bmp.Width * scale;
            float drawH = bmp.Height * scale;
            float drawX = previewAreaRect.X + ((previewAreaRect.Width - drawW) / 2f);
            float drawY = previewAreaRect.Y + ((previewAreaRect.Height - drawH) / 2f);
            g.DrawImage(bmp, drawX, drawY, drawW, drawH);
        }
        else
        {
            using var centerFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(Loc("print_options_loading"), _tipFont, mutedBrush, previewAreaRect, centerFormat);
        }

        // 6. Navigation below preview
        DrawPrintButton(g, s, PrintOptionsLayout.PrevPageButton, "<", false, enabled: _printPreviewModel.CanNavigatePrevious,
            hovered: _hoveredPrintElement == PrintElement.PrevPage,
            pressed: _pressedPrintElement == PrintElement.PrevPage);
        using var pageFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        string pageText = Loc("print_options_page_indicator", _printPreviewModel.CurrentPageNumber, _printPreviewModel.TotalPages);
        g.DrawString(pageText, _msgFont, bodyBrush, ScalePrintRect(PrintOptionsLayout.PageTextRect, s), pageFormat);
        DrawPrintButton(g, s, PrintOptionsLayout.NextPageButton, ">", false, enabled: _printPreviewModel.CanNavigateNext,
            hovered: _hoveredPrintElement == PrintElement.NextPage,
            pressed: _pressedPrintElement == PrintElement.NextPage);

        // 7. Separator & Action Buttons
        using var sepPen = new Pen(Color.FromArgb(58, 58, 58), 1f * s);
        g.DrawLine(sepPen, 36 * s, PrintOptionsLayout.SeparatorY * s, 484 * s, PrintOptionsLayout.SeparatorY * s);
        DrawPrintButton(g, s, PrintOptionsLayout.ConfirmButton, Loc("print_options_confirm"), true,
            hovered: _hoveredPrintElement == PrintElement.Confirm,
            pressed: _pressedPrintElement == PrintElement.Confirm);
        DrawPrintButton(g, s, PrintOptionsLayout.CancelButton, Loc("dialog_cancel"), false,
            hovered: _hoveredPrintElement == PrintElement.Cancel,
            pressed: _pressedPrintElement == PrintElement.Cancel);
    }

    private void DrawPrintLabel(Graphics g, float s, string text, int y, Brush brush)
    {
        if (_msgFont is null) return;
        var rect = ScalePrintRect(new LayoutRect(PrintOptionsLayout.LeftColumnX, y, PrintOptionsLayout.LeftColumnWidth, 20), s);
        using var format = new StringFormat { LineAlignment = StringAlignment.Center };
        g.DrawString(text, _msgFont, brush, rect, format);
    }

    private void DrawPrintButton(Graphics g, float s, LayoutRect logicalRect, string text, bool selected, bool enabled = true, bool hovered = false, bool pressed = false)
    {
        var rect = ScalePrintRect(logicalRect, s);
        using var path = UIHelper.GetRoundedRectPath(rect, 4 * s);
        Color accent = UIHelper.GetSystemColorizationColor();
        Color bg;
        Color border;

        if (!enabled)
        {
            bg = Color.FromArgb(35, 35, 35);
            border = Color.FromArgb(48, 48, 48);
        }
        else if (pressed)
        {
            bg = selected ? UIHelper.Darken(accent, 0.25f) : Color.FromArgb(68, 68, 68);
            border = selected ? accent : UIHelper.Lighten(accent, 0.2f);
        }
        else if (hovered)
        {
            bg = selected ? UIHelper.Lighten(accent, 0.15f) : Color.FromArgb(58, 58, 58);
            border = selected ? UIHelper.Lighten(accent, 0.35f) : Color.FromArgb(95, 95, 95);
        }
        else if (selected)
        {
            bg = accent;
            border = UIHelper.Lighten(accent, 0.18f);
        }
        else
        {
            bg = Color.FromArgb(45, 45, 45);
            border = Color.FromArgb(70, 70, 70);
        }

        using var fill = new SolidBrush(bg);
        using var pen = new Pen(border, 1f * s);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        Color textColor = enabled ? Color.White : Color.FromArgb(120, 120, 120);
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
        Color bg = isChecked
            ? (hovered ? UIHelper.Lighten(accent, 0.15f) : accent)
            : (hovered ? Color.FromArgb(55, 55, 55) : Color.FromArgb(40, 40, 40));
        Color border = isChecked
            ? (hovered ? UIHelper.Lighten(accent, 0.35f) : UIHelper.Lighten(accent, 0.2f))
            : (hovered ? Color.FromArgb(120, 120, 120) : Color.FromArgb(80, 80, 80));

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

    private PrintElement GetPrintElementAt(int mouseX, int mouseY)
    {
        var prevPrinterRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.PrinterComboY, 26, PrintOptionsLayout.PrinterComboHeight);
        var printerNameRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 28, PrintOptionsLayout.PrinterComboY, PrintOptionsLayout.LeftColumnWidth - 56, PrintOptionsLayout.PrinterComboHeight);
        var nextPrinterRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + PrintOptionsLayout.LeftColumnWidth - 26, PrintOptionsLayout.PrinterComboY, 26, PrintOptionsLayout.PrinterComboHeight);

        if (prevPrinterRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.PrevPrinter;
        if (printerNameRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.PrinterName;
        if (nextPrinterRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.NextPrinter;

        var prevPaperRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.PaperComboY, 26, PrintOptionsLayout.PaperComboHeight);
        var paperNameRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 28, PrintOptionsLayout.PaperComboY, PrintOptionsLayout.LeftColumnWidth - 56, PrintOptionsLayout.PaperComboHeight);
        var nextPaperRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + PrintOptionsLayout.LeftColumnWidth - 26, PrintOptionsLayout.PaperComboY, 26, PrintOptionsLayout.PaperComboHeight);

        if (prevPaperRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.PrevPaper;
        if (paperNameRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.PaperName;
        if (nextPaperRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.NextPaper;

        var prevDuplexRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.DuplexComboY, 26, PrintOptionsLayout.DuplexComboHeight);
        var duplexNameRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 28, PrintOptionsLayout.DuplexComboY, PrintOptionsLayout.LeftColumnWidth - 56, PrintOptionsLayout.DuplexComboHeight);
        var nextDuplexRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + PrintOptionsLayout.LeftColumnWidth - 26, PrintOptionsLayout.DuplexComboY, 26, PrintOptionsLayout.DuplexComboHeight);

        if (prevDuplexRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.PrevDuplex;
        if (duplexNameRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.DuplexName;
        if (nextDuplexRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.NextDuplex;

        var allPagesRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.RangeInputY, 68, PrintOptionsLayout.RangeInputHeight);
        var customPagesRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 74, PrintOptionsLayout.RangeInputY, PrintOptionsLayout.LeftColumnWidth - 74, PrintOptionsLayout.RangeInputHeight);

        if (allPagesRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.RangeAll;
        if (customPagesRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.RangeCustom;

        var minusCopiesRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.CopiesInputY, 32, PrintOptionsLayout.CopiesInputHeight);
        var plusCopiesRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 96, PrintOptionsLayout.CopiesInputY, 32, PrintOptionsLayout.CopiesInputHeight);

        if (minusCopiesRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.MinusCopies;
        if (plusCopiesRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.PlusCopies;

        var fitRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.FitToPageY, PrintOptionsLayout.LeftColumnWidth, PrintOptionsLayout.FitToPageHeight);
        if (fitRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.FitToPage;

        var darkenRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.DarkenY, PrintOptionsLayout.LeftColumnWidth, PrintOptionsLayout.DarkenHeight);
        if (darkenRect.ContainsInclusive(mouseX, mouseY)) return PrintElement.Darken;

        if (PrintOptionsLayout.PrevPageButton.ContainsInclusive(mouseX, mouseY)) return PrintElement.PrevPage;
        if (PrintOptionsLayout.NextPageButton.ContainsInclusive(mouseX, mouseY)) return PrintElement.NextPage;

        if (PrintOptionsLayout.ConfirmButton.ContainsInclusive(mouseX, mouseY)) return PrintElement.Confirm;
        if (PrintOptionsLayout.CancelButton.ContainsInclusive(mouseX, mouseY)) return PrintElement.Cancel;

        return PrintElement.None;
    }

    private bool HandlePrintOptionsClick(IntPtr hwnd, int mouseX, int mouseY)
    {
        var prevPrinterRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.PrinterComboY, 26, PrintOptionsLayout.PrinterComboHeight);
        var printerNameRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 28, PrintOptionsLayout.PrinterComboY, PrintOptionsLayout.LeftColumnWidth - 56, PrintOptionsLayout.PrinterComboHeight);
        var nextPrinterRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + PrintOptionsLayout.LeftColumnWidth - 26, PrintOptionsLayout.PrinterComboY, 26, PrintOptionsLayout.PrinterComboHeight);

        if (prevPrinterRect.ContainsInclusive(mouseX, mouseY))
        {
            if (_installedPrinters.Count > 1)
            {
                _printPrinterIndex = (_printPrinterIndex - 1 + _installedPrinters.Count) % _installedPrinters.Count;
                _printPrinterName = _installedPrinters[_printPrinterIndex];
                RefreshPrinterCapabilities(_printerDiscovery);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return true;
        }
        if (nextPrinterRect.ContainsInclusive(mouseX, mouseY) || printerNameRect.ContainsInclusive(mouseX, mouseY))
        {
            if (_installedPrinters.Count > 1)
            {
                _printPrinterIndex = (_printPrinterIndex + 1) % _installedPrinters.Count;
                _printPrinterName = _installedPrinters[_printPrinterIndex];
                RefreshPrinterCapabilities(_printerDiscovery);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return true;
        }

        var prevPaperRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.PaperComboY, 26, PrintOptionsLayout.PaperComboHeight);
        var paperNameRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 28, PrintOptionsLayout.PaperComboY, PrintOptionsLayout.LeftColumnWidth - 56, PrintOptionsLayout.PaperComboHeight);
        var nextPaperRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + PrintOptionsLayout.LeftColumnWidth - 26, PrintOptionsLayout.PaperComboY, 26, PrintOptionsLayout.PaperComboHeight);

        if (prevPaperRect.ContainsInclusive(mouseX, mouseY))
        {
            if (_supportedPaperSizes.Count > 1)
            {
                _paperSizeIndex = (_paperSizeIndex - 1 + _supportedPaperSizes.Count) % _supportedPaperSizes.Count;
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return true;
        }
        if (nextPaperRect.ContainsInclusive(mouseX, mouseY) || paperNameRect.ContainsInclusive(mouseX, mouseY))
        {
            if (_supportedPaperSizes.Count > 1)
            {
                _paperSizeIndex = (_paperSizeIndex + 1) % _supportedPaperSizes.Count;
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return true;
        }

        var prevDuplexRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.DuplexComboY, 26, PrintOptionsLayout.DuplexComboHeight);
        var duplexNameRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 28, PrintOptionsLayout.DuplexComboY, PrintOptionsLayout.LeftColumnWidth - 56, PrintOptionsLayout.DuplexComboHeight);
        var nextDuplexRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + PrintOptionsLayout.LeftColumnWidth - 26, PrintOptionsLayout.DuplexComboY, 26, PrintOptionsLayout.DuplexComboHeight);

        if (prevDuplexRect.ContainsInclusive(mouseX, mouseY))
        {
            if (_printerCanDuplex)
            {
                _duplexIndex = (_duplexIndex - 1 + 3) % 3;
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return true;
        }
        if (nextDuplexRect.ContainsInclusive(mouseX, mouseY) || duplexNameRect.ContainsInclusive(mouseX, mouseY))
        {
            if (_printerCanDuplex)
            {
                _duplexIndex = (_duplexIndex + 1) % 3;
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return true;
        }

        var allPagesRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.RangeInputY, 68, PrintOptionsLayout.RangeInputHeight);
        var customPagesRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 74, PrintOptionsLayout.RangeInputY, PrintOptionsLayout.LeftColumnWidth - 74, PrintOptionsLayout.RangeInputHeight);

        if (allPagesRect.ContainsInclusive(mouseX, mouseY))
        {
            _printPageRange = "all";
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return true;
        }
        if (customPagesRect.ContainsInclusive(mouseX, mouseY))
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

        var minusCopiesRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.CopiesInputY, 32, PrintOptionsLayout.CopiesInputHeight);
        var plusCopiesRect = new LayoutRect(PrintOptionsLayout.LeftColumnX + 96, PrintOptionsLayout.CopiesInputY, 32, PrintOptionsLayout.CopiesInputHeight);

        if (minusCopiesRect.ContainsInclusive(mouseX, mouseY))
        {
            if (_printCopies > 1)
            {
                _printCopies--;
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return true;
        }
        if (plusCopiesRect.ContainsInclusive(mouseX, mouseY))
        {
            if (_printCopies < 99)
            {
                _printCopies++;
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            return true;
        }

        var fitRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.FitToPageY, PrintOptionsLayout.LeftColumnWidth, PrintOptionsLayout.FitToPageHeight);
        if (fitRect.ContainsInclusive(mouseX, mouseY))
        {
            _printFitToPage = !_printFitToPage;
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return true;
        }

        var darkenRect = new LayoutRect(PrintOptionsLayout.LeftColumnX, PrintOptionsLayout.DarkenY, PrintOptionsLayout.LeftColumnWidth, PrintOptionsLayout.DarkenHeight);
        if (darkenRect.ContainsInclusive(mouseX, mouseY))
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

        _bufferGraphics?.Dispose();
        _bufferBmp?.Dispose();
        _bufferBmp = new Bitmap(clientW, clientH);
        _bufferGraphics = Graphics.FromImage(_bufferBmp);
        _bufferGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        _bufferGraphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var rect = new RECT { left = 0, top = 0, right = clientW, bottom = clientH };
        AdjustWindowRectEx(ref rect, WS_OVERLAPPED_FIXED, false, 0);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, rect.right - rect.left, rect.bottom - rect.top, 0x0002 | 0x0004);
        InvalidateRect(hwnd, IntPtr.Zero, false);
    }
}

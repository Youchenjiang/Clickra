using System.Drawing;
using System.Drawing.Printing;
using Clickra.Core.Modules.Pdf.Print.Application;
using Clickra.Core.Modules.Pdf.Split.Application;
using Clickra.Core.Processors;
using Clickra.Core.Rendering;

namespace Clickra.Core.Modules.Pdf.Print.Processing;

/// <summary>
/// Direct streaming PDF printer: renders PDF pages on-the-fly and sends them to the Windows
/// Print Spooler via GDI+ PrintDocument. Page placement, multi-page grids, and sheet orientations
/// are strictly calculated by PrintLayoutPlanner.
/// </summary>
public static class PdfPrintProcessor
{
    public static bool Print(
        string pdfPath,
        PrintPdfOptions options,
        Action<int, int, string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<int> pagesToPrint = ValidateAndResolvePages(pdfPath, options);

        using var printDoc = new PrintDocument
        {
            DocumentName = Path.GetFileNameWithoutExtension(pdfPath)
        };

        string effectiveLang = !string.IsNullOrWhiteSpace(options.Language) ? options.Language : "en";
        string defaultFontName = LocalizedUiFontSelector.GetTextFontName(effectiveLang);

        ConfigurePrinter(printDoc, options);
        var (portraitWidthPt, portraitHeightPt) = CalculatePortraitDimensions(printDoc);

        var pageSizes = CollectPageSizes(pdfPath, pagesToPrint);
        var sheets = PrintLayoutPlanner.Plan(pageSizes, portraitWidthPt, portraitHeightPt, options);
        if (sheets.Count == 0)
        {
            return true;
        }

        printDoc.DefaultPageSettings.Landscape = sheets[0].Landscape;
        if (options.Grayscale)
        {
            printDoc.DefaultPageSettings.Color = false;
        }

        AttachPrintEvents(printDoc, pdfPath, sheets, defaultFontName, options, portraitWidthPt, portraitHeightPt, progress, cancellationToken);

        printDoc.Print();
        progress?.Invoke(
            sheets.Count,
            sheets.Count,
            ExecutionLocalizationOptions.Text(options.Language ?? "", "core_print_sent"));
        return true;
    }

    private static List<int> ValidateAndResolvePages(string pdfPath, PrintPdfOptions options)
    {
        if (!System.IO.File.Exists(pdfPath))
            throw new FileNotFoundException("PDF file not found", pdfPath);

        int totalDocPages = PdfPageCountQuery.Get(pdfPath);
        if (totalDocPages <= 0)
            throw new InvalidOperationException($"Unable to determine page count for '{pdfPath}'.");

        List<int> pagesToPrint = ResolvePageNumbers(options.PageRange, totalDocPages);
        if (pagesToPrint.Count == 0)
            throw new InvalidOperationException(ExecutionLocalizationOptions.Text(options.Language ?? "", "core_print_no_pages"));

        return pagesToPrint;
    }

    private static (float PortraitWidthPt, float PortraitHeightPt) CalculatePortraitDimensions(PrintDocument printDoc)
    {
        PaperSize paper = printDoc.DefaultPageSettings.PaperSize;
        float paperWidthHundredths = paper.Width > 0 ? paper.Width : 827;
        float paperHeightHundredths = paper.Height > 0 ? paper.Height : 1169;
        float portraitWidthPt = Math.Min(paperWidthHundredths, paperHeightHundredths) * 0.72f;
        float portraitHeightPt = Math.Max(paperWidthHundredths, paperHeightHundredths) * 0.72f;
        return (portraitWidthPt, portraitHeightPt);
    }

    private static List<PrintPageSize> CollectPageSizes(string pdfPath, List<int> pagesToPrint)
    {
        var pageDimensions = ReadPageDimensions(pdfPath, pagesToPrint);
        var pageSizes = new List<PrintPageSize>(pagesToPrint.Count);
        foreach (int pageNum in pagesToPrint)
        {
            if (pageDimensions.TryGetValue(pageNum, out var dim) && dim.widthPt > 0 && dim.heightPt > 0)
            {
                pageSizes.Add(new PrintPageSize(pageNum, dim.widthPt, dim.heightPt));
            }
            else
            {
                pageSizes.Add(new PrintPageSize(pageNum, 595.28f, 841.89f));
            }
        }
        return pageSizes;
    }

    private static void AttachPrintEvents(
        PrintDocument printDoc,
        string pdfPath,
        IReadOnlyList<PrintSheet> sheets,
        string defaultFontName,
        PrintPdfOptions options,
        float portraitWidthPt,
        float portraitHeightPt,
        Action<int, int, string>? progress,
        CancellationToken cancellationToken)
    {
        int currentSheetIndex = 0;
        int totalSheets = sheets.Count;

        printDoc.QueryPageSettings += (sender, e) =>
        {
            if (currentSheetIndex < totalSheets)
            {
                var sheet = sheets[currentSheetIndex];
                e.PageSettings.Landscape = sheet.Landscape;
                if (options.Grayscale)
                {
                    e.PageSettings.Color = false;
                }
            }
        };

        printDoc.PrintPage += (sender, e) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                e.Cancel = true;
                e.HasMorePages = false;
                return;
            }

            if (currentSheetIndex >= totalSheets)
            {
                e.HasMorePages = false;
                return;
            }

            var sheet = sheets[currentSheetIndex];
            progress?.Invoke(
                currentSheetIndex + 1,
                totalSheets,
                ExecutionLocalizationOptions.Text(
                    options.Language ?? "",
                    "core_print_progress_sheet",
                    ("current", currentSheetIndex + 1),
                    ("total", totalSheets)));

            RenderAndDrawSheet(e, pdfPath, sheet, defaultFontName, options, portraitWidthPt, portraitHeightPt);

            currentSheetIndex++;
            e.HasMorePages = currentSheetIndex < totalSheets;
        };
    }

    private static void ConfigurePrinter(PrintDocument printDoc, PrintPdfOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PrinterName))
        {
            printDoc.PrinterSettings.PrinterName = options.PrinterName;
        }

        if (!printDoc.PrinterSettings.IsValid)
        {
            throw new InvalidOperationException(ExecutionLocalizationOptions.Text(options.Language ?? "", "core_print_printer_unavailable", ("printer", printDoc.PrinterSettings.PrinterName)));
        }

        printDoc.PrinterSettings.Copies = (short)Math.Clamp(options.Copies, 1, 999);
        printDoc.PrinterSettings.Collate = options.Collate;
        printDoc.PrintController = new StandardPrintController(); // Suppress the default WinForms printing status dialog

        if (printDoc.PrinterSettings.CanDuplex)
        {
            printDoc.PrinterSettings.Duplex = options.Duplex switch
            {
                PrintDuplexMode.DuplexLongEdge => Duplex.Vertical,
                PrintDuplexMode.DuplexShortEdge => Duplex.Horizontal,
                _ => Duplex.Simplex
            };
        }

        PaperSize? targetPaper = FindTargetPaperSize(printDoc.PrinterSettings.PaperSizes, options.PaperSize);
        if (targetPaper != null)
        {
            printDoc.DefaultPageSettings.PaperSize = targetPaper;
        }

        printDoc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        printDoc.OriginAtMargins = false;
    }

    private static PaperSize? FindTargetPaperSize(PrinterSettings.PaperSizeCollection paperSizes, string? requestedPaper)
    {
        if (!string.IsNullOrWhiteSpace(requestedPaper))
        {
            foreach (PaperSize paper in paperSizes)
            {
                if (string.Equals(paper.PaperName, requestedPaper, StringComparison.OrdinalIgnoreCase))
                {
                    return paper;
                }
            }
        }

        foreach (PaperSize paper in paperSizes)
        {
            if (paper.Kind == PaperKind.A4)
            {
                return paper;
            }
        }

        return null;
    }

    private static void RenderAndDrawSheet(
        PrintPageEventArgs e,
        string pdfPath,
        PrintSheet sheet,
        string defaultFontName,
        PrintPdfOptions options,
        float portraitWidthPt,
        float portraitHeightPt)
    {
        if (e.Graphics == null) return;

        float sheetWidthPt = sheet.Landscape ? portraitHeightPt : portraitWidthPt;
        float sheetHeightPt = sheet.Landscape ? portraitWidthPt : portraitHeightPt;

        Rectangle pageBounds = e.PageBounds;
        float scaleX = sheetWidthPt > 0 ? (float)pageBounds.Width / sheetWidthPt : 1f;
        float scaleY = sheetHeightPt > 0 ? (float)pageBounds.Height / sheetHeightPt : 1f;

        float toGdiX(float xPt) => pageBounds.X + (xPt * scaleX);
        float toGdiY(float yPt) => pageBounds.Y + (yPt * scaleY);
        float toGdiW(float wPt) => wPt * scaleX;
        float toGdiH(float hPt) => hPt * scaleY;

        foreach (var cell in sheet.Cells)
        {
            int renderWidth = (int)Math.Round((cell.Page.Width / 72f) * options.Dpi);
            renderWidth = Math.Max(100, renderWidth);

            using Bitmap? pageBmp = PdfPageThumbnailRenderer.RenderPageFromFile(pdfPath, cell.PageNumber, renderWidth, defaultFontName);
            if (pageBmp == null) continue;

            if (options.DarkenText)
            {
                PdfContrastEnhancer.Enhance(pageBmp, options.DarkenGamma);
            }

            if (options.Grayscale)
            {
                PdfContrastEnhancer.ConvertToGrayscale(pageBmp);
            }

            var gdiCell = new RectangleF(toGdiX(cell.Cell.X), toGdiY(cell.Cell.Y), toGdiW(cell.Cell.Width), toGdiH(cell.Cell.Height));
            var gdiPage = new RectangleF(toGdiX(cell.Page.X), toGdiY(cell.Page.Y), toGdiW(cell.Page.Width), toGdiH(cell.Page.Height));

            var state = e.Graphics.Save();
            try
            {
                e.Graphics.SetClip(gdiCell);
                e.Graphics.DrawImage(pageBmp, gdiPage);

                if (options.PageBorder && options.PagesPerSheet >= 2)
                {
                    using var borderPen = new Pen(Color.FromArgb(180, 180, 180), Math.Max(0.5f, 0.5f * scaleX));
                    e.Graphics.DrawRectangle(borderPen, gdiPage.X, gdiPage.Y, gdiPage.Width, gdiPage.Height);
                }
            }
            finally
            {
                e.Graphics.Restore(state);
            }
        }
    }


    /// <summary>
    /// Reads the page size in points (width, height) as the page is displayed. PdfPig already
    /// applies /Rotate to Page.Width and Page.Height (its page factory transforms the media and
    /// crop boxes by the rotation), so they are used as they are.
    /// </summary>
    public static Dictionary<int, (float widthPt, float heightPt)> ReadPageDimensions(string pdfPath, IEnumerable<int> pages)
    {
        var result = new Dictionary<int, (float widthPt, float heightPt)>();
        try
        {
            using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            foreach (int pageNum in pages)
            {
                if (pageNum >= 1 && pageNum <= doc.NumberOfPages)
                {
                    var page = doc.GetPage(pageNum);
                    result[pageNum] = ((float)page.Width, (float)page.Height);
                }
            }
        }
        catch
        {
            // Unreadable PDF falls back to empty dictionary
        }
        return result;
    }

    /// <summary>
    /// Reads page orientation (true = landscape, false = portrait) for the specified pages.
    /// </summary>
    public static Dictionary<int, bool> ReadPageOrientations(string pdfPath, IEnumerable<int> pages)
    {
        var dimensions = ReadPageDimensions(pdfPath, pages);
        return dimensions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.widthPt > kvp.Value.heightPt);
    }

    /// <summary>
    /// Parses page range specification (e.g., "1-3, 5, 8-10", "all", "") into sorted 1-based page numbers.
    /// </summary>
    public static List<int> ResolvePageNumbers(string? pageRange, int totalPages) =>
        PrintLayoutPlanner.ResolvePageNumbers(pageRange, totalPages);
}

using System.Drawing;
using System.Drawing.Printing;
using Clickra.Core.Modules.PdfPrint.Application;
using Clickra.Core.Modules.PdfSplit.Application;
using Clickra.Core.Rendering;

namespace Clickra.Core.Modules.PdfPrint.Processing;

/// <summary>
/// Direct streaming PDF printer: renders PDF pages on-the-fly and sends them to the Windows
/// Print Spooler via GDI+ PrintDocument. Avoids creating intermediate disk files and releases
/// each rendered bitmap immediately after drawing to minimize memory consumption.
/// </summary>
public static class PdfPrintProcessor
{
    public static bool Print(
        string pdfPath,
        PrintPdfOptions options,
        Action<int, int, string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("PDF file not found", pdfPath);

        int totalDocPages = PdfPageCountQuery.Get(pdfPath);
        if (totalDocPages <= 0)
            throw new InvalidOperationException($"Unable to determine page count for '{pdfPath}'.");

        List<int> pagesToPrint = ResolvePageNumbers(options.PageRange, totalDocPages);
        if (pagesToPrint.Count == 0)
            throw new InvalidOperationException("No valid pages selected for printing.");

        string docName = Path.GetFileNameWithoutExtension(pdfPath);
        using var printDoc = new PrintDocument();
        printDoc.DocumentName = docName;

        string effectiveLang = !string.IsNullOrWhiteSpace(options.Language) ? options.Language : "en";
        string defaultFontName = PdfPageThumbnailRenderer.GetTextFontName(effectiveLang);

        var pageDimensions = ReadPageDimensions(pdfPath, pagesToPrint);
        Dictionary<int, bool> pageOrientations = pageDimensions.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.widthPt > kvp.Value.heightPt);
        bool firstPageLandscape = pagesToPrint.Count > 0 &&
                                  pageOrientations.TryGetValue(pagesToPrint[0], out bool fl) && fl;

        ConfigurePrinter(printDoc, options, firstPageLandscape);

        int currentIndex = 0;
        int totalPrintCount = pagesToPrint.Count;

        printDoc.QueryPageSettings += (sender, e) =>
        {
            if (currentIndex < totalPrintCount)
            {
                int pageNum = pagesToPrint[currentIndex];
                bool isLandscape = options.Orientation switch
                {
                    PrintOrientation.Landscape => true,
                    PrintOrientation.Portrait => false,
                    _ => pageOrientations.TryGetValue(pageNum, out bool l) && l
                };
                e.PageSettings.Landscape = isLandscape;
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

            if (currentIndex >= totalPrintCount)
            {
                e.HasMorePages = false;
                return;
            }

            int pageNum = pagesToPrint[currentIndex];
            progress?.Invoke(currentIndex + 1, totalPrintCount, $"Printing page {pageNum} of {totalDocPages}...");

            RenderAndDrawPage(e, pdfPath, pageNum, defaultFontName, options, pageDimensions);

            currentIndex++;
            e.HasMorePages = currentIndex < totalPrintCount;
        };

        printDoc.Print();
        progress?.Invoke(totalPrintCount, totalPrintCount, "Print job sent successfully.");
        return true;
    }

    private static void ConfigurePrinter(PrintDocument printDoc, PrintPdfOptions options, bool firstPageLandscape)
    {
        if (!string.IsNullOrWhiteSpace(options.PrinterName))
        {
            printDoc.PrinterSettings.PrinterName = options.PrinterName;
        }

        if (!printDoc.PrinterSettings.IsValid)
        {
            throw new InvalidOperationException($"Printer '{printDoc.PrinterSettings.PrinterName}' is not valid or not installed.");
        }

        printDoc.PrinterSettings.Copies = (short)Math.Clamp(options.Copies, 1, 999);
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

        bool isLandscape = options.Orientation switch
        {
            PrintOrientation.Landscape => true,
            PrintOrientation.Portrait => false,
            _ => firstPageLandscape
        };
        printDoc.DefaultPageSettings.Landscape = isLandscape;

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

    public static Rectangle CalculateDestinationBounds(
        Rectangle pageBounds,
        float pdfPageWidthPt,
        float pdfPageHeightPt,
        bool fitToPage)
    {
        if (fitToPage)
        {
            int margin = (int)Math.Round(Math.Min(pageBounds.Width, pageBounds.Height) * 0.03f);
            int printAreaW = Math.Max(1, pageBounds.Width - (margin * 2));
            int printAreaH = Math.Max(1, pageBounds.Height - (margin * 2));
            float scale = Math.Min((float)printAreaW / pdfPageWidthPt, (float)printAreaH / pdfPageHeightPt);
            int destW = (int)Math.Round(pdfPageWidthPt * scale);
            int destH = (int)Math.Round(pdfPageHeightPt * scale);
            int destX = pageBounds.X + margin + (printAreaW - destW) / 2;
            int destY = pageBounds.Y + margin + (printAreaH - destH) / 2;
            return new Rectangle(destX, destY, destW, destH);
        }
        else
        {
            // 1 pt = 1/72 inch = 100/72 hundredths of an inch in PrintDocument physical coordinates
            int destW = (int)Math.Round(pdfPageWidthPt * (100f / 72f));
            int destH = (int)Math.Round(pdfPageHeightPt * (100f / 72f));
            int destX = pageBounds.X + (pageBounds.Width - destW) / 2;
            int destY = pageBounds.Y + (pageBounds.Height - destH) / 2;
            return new Rectangle(destX, destY, destW, destH);
        }
    }

    private static void RenderAndDrawPage(
        PrintPageEventArgs e,
        string pdfPath,
        int pageNum,
        string defaultFontName,
        PrintPdfOptions options,
        Dictionary<int, (float widthPt, float heightPt)>? pageDimensions = null)
    {
        Rectangle pageBounds = e.PageBounds;
        float pdfW = pageBounds.Width / 100f * 72f;
        float pdfH = pageBounds.Height / 100f * 72f;
        if (pageDimensions != null && pageDimensions.TryGetValue(pageNum, out var dim) && dim.widthPt > 0 && dim.heightPt > 0)
        {
            pdfW = dim.widthPt;
            pdfH = dim.heightPt;
        }

        Rectangle destRect = CalculateDestinationBounds(pageBounds, pdfW, pdfH, options.FitToPage);

        int renderWidth = options.FitToPage
            ? (int)Math.Round((destRect.Width / 100f) * options.Dpi)
            : (int)Math.Round((pdfW / 72f) * options.Dpi);
        renderWidth = Math.Max(100, renderWidth);

        using Bitmap? pageBmp = PdfPageThumbnailRenderer.RenderPageFromFile(pdfPath, pageNum, renderWidth, defaultFontName);
        if (pageBmp == null) return;

        if (options.DarkenText)
        {
            PdfContrastEnhancer.Enhance(pageBmp, options.DarkenGamma);
        }

        e.Graphics?.DrawImage(pageBmp, destRect);
    }

    /// <summary>
    /// Reads physical page dimensions in points (width, height) accounting for PDF rotation.
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
                    int rot = page.Rotation.Value;
                    bool isRotated = rot == 90 || rot == 270;
                    float visualW = (float)(isRotated ? page.Height : page.Width);
                    float visualH = (float)(isRotated ? page.Width : page.Height);
                    result[pageNum] = (visualW, visualH);
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
    public static List<int> ResolvePageNumbers(string? pageRange, int totalPages)
    {
        if (string.IsNullOrWhiteSpace(pageRange) || string.Equals(pageRange.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            var all = new List<int>(totalPages);
            for (int i = 1; i <= totalPages; i++) all.Add(i);
            return all;
        }

        var result = new HashSet<int>();
        string[] parts = pageRange.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string part in parts)
        {
            ParsePageToken(part, totalPages, result);
        }

        var list = result.ToList();
        list.Sort();
        return list;
    }

    private static void ParsePageToken(string token, int totalPages, HashSet<int> result)
    {
        if (int.TryParse(token, out int singlePage))
        {
            if (singlePage >= 1 && singlePage <= totalPages)
            {
                result.Add(singlePage);
            }
            return;
        }

        int dashIdx = token.IndexOf('-');
        if (dashIdx < 0) return;

        string[] rangeParts = token.Split('-');
        if (rangeParts.Length == 2 &&
            int.TryParse(rangeParts[0].Trim(), out int start) &&
            int.TryParse(rangeParts[1].Trim(), out int end))
        {
            int rStart = Math.Max(1, Math.Min(start, end));
            int rEnd = Math.Min(totalPages, Math.Max(start, end));
            for (int p = rStart; p <= rEnd; p++)
            {
                result.Add(p);
            }
        }
    }
}

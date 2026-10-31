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

        ConfigurePrinter(printDoc, options, out PaperSize? targetPaper);

        string effectiveLang = !string.IsNullOrWhiteSpace(options.Language) ? options.Language : "en";
        string defaultFontName = PdfPageThumbnailRenderer.GetTextFontName(effectiveLang);

        Dictionary<int, bool> pageOrientations = ReadPageOrientations(pdfPath, pagesToPrint);

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

            RenderAndDrawPage(e, pdfPath, pageNum, defaultFontName, options);

            currentIndex++;
            e.HasMorePages = currentIndex < totalPrintCount;
        };

        printDoc.Print();
        progress?.Invoke(totalPrintCount, totalPrintCount, "Print job sent successfully.");
        return true;
    }

    private static void ConfigurePrinter(PrintDocument printDoc, PrintPdfOptions options, out PaperSize? targetPaper)
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

        targetPaper = FindTargetPaperSize(printDoc.PrinterSettings.PaperSizes, options.PaperSize);
        if (targetPaper != null)
        {
            printDoc.DefaultPageSettings.PaperSize = targetPaper;
        }

        if (options.Orientation == PrintOrientation.Landscape)
        {
            printDoc.DefaultPageSettings.Landscape = true;
        }
        else if (options.Orientation == PrintOrientation.Portrait)
        {
            printDoc.DefaultPageSettings.Landscape = false;
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

    private static void RenderAndDrawPage(
        PrintPageEventArgs e,
        string pdfPath,
        int pageNum,
        string defaultFontName,
        PrintPdfOptions options)
    {
        Rectangle pageBounds = e.PageBounds;
        double paperWidthInches = Math.Max(1, pageBounds.Width) / 100.0;
        int renderWidth = (int)Math.Round(paperWidthInches * options.Dpi);

        using Bitmap? pageBmp = PdfPageThumbnailRenderer.RenderPageFromFile(pdfPath, pageNum, renderWidth, defaultFontName);
        if (pageBmp == null) return;

        if (options.DarkenText)
        {
            PdfContrastEnhancer.Enhance(pageBmp, options.DarkenGamma);
        }

        if (options.FitToPage)
        {
            float scale = Math.Min((float)pageBounds.Width / pageBmp.Width, (float)pageBounds.Height / pageBmp.Height);
            int destW = (int)Math.Round(pageBmp.Width * scale);
            int destH = (int)Math.Round(pageBmp.Height * scale);
            int destX = pageBounds.X + (pageBounds.Width - destW) / 2;
            int destY = pageBounds.Y + (pageBounds.Height - destH) / 2;
            e.Graphics?.DrawImage(pageBmp, new Rectangle(destX, destY, destW, destH));
        }
        else
        {
            int destW = (int)Math.Round(pageBmp.Width * (100f / options.Dpi));
            int destH = (int)Math.Round(pageBmp.Height * (100f / options.Dpi));
            int destX = pageBounds.X + (pageBounds.Width - destW) / 2;
            int destY = pageBounds.Y + (pageBounds.Height - destH) / 2;
            e.Graphics?.DrawImage(pageBmp, new Rectangle(destX, destY, destW, destH));
        }
    }

    /// <summary>
    /// Reads page orientation (true = landscape, false = portrait) for the specified pages.
    /// </summary>
    public static Dictionary<int, bool> ReadPageOrientations(string pdfPath, IEnumerable<int> pages)
    {
        var result = new Dictionary<int, bool>();
        try
        {
            using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            foreach (int pageNum in pages)
            {
                if (pageNum >= 1 && pageNum <= doc.NumberOfPages)
                {
                    var page = doc.GetPage(pageNum);
                    result[pageNum] = page.Width > page.Height;
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

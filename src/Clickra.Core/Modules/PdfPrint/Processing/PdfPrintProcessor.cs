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
public sealed class PdfPrintProcessor
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

        double paperWidthInches = targetPaper != null && targetPaper.Width > 0
            ? targetPaper.Width / 100.0
            : 8.27; // fallback to standard A4 width
        int renderWidth = (int)Math.Round(paperWidthInches * options.Dpi);

        string effectiveLang = !string.IsNullOrWhiteSpace(options.Language) ? options.Language : "en";
        string defaultFontName = PdfPageThumbnailRenderer.GetTextFontName(effectiveLang);

        int currentIndex = 0;
        int totalPrintCount = pagesToPrint.Count;

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

            RenderAndDrawPage(e, pdfPath, pageNum, renderWidth, defaultFontName, options);

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
        int renderWidth,
        string defaultFontName,
        PrintPdfOptions options)
    {
        using Bitmap? pageBmp = PdfPageThumbnailRenderer.RenderPageFromFile(pdfPath, pageNum, renderWidth, defaultFontName);
        if (pageBmp == null) return;

        if (options.DarkenText)
        {
            PdfContrastEnhancer.Enhance(pageBmp, options.DarkenGamma);
        }

        if (options.FitToPage)
        {
            e.Graphics?.DrawImage(pageBmp, e.PageBounds);
        }
        else
        {
            e.Graphics?.DrawImage(pageBmp, 0, 0);
        }
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

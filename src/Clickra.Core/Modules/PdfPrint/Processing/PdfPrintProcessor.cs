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
    public bool Print(
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

        // Default to A4 if available in printer settings
        foreach (PaperSize paper in printDoc.PrinterSettings.PaperSizes)
        {
            if (paper.Kind == PaperKind.A4)
            {
                printDoc.DefaultPageSettings.PaperSize = paper;
                break;
            }
        }
        printDoc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        printDoc.OriginAtMargins = false;

        int currentIndex = 0;
        int totalPrintCount = pagesToPrint.Count;
        string effectiveLang = !string.IsNullOrWhiteSpace(options.Language) ? options.Language : "en";
        string defaultFontName = PdfPageThumbnailRenderer.GetTextFontName(effectiveLang);

        printDoc.PrintPage += (sender, e) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                e.Cancel = true;
                e.HasMorePages = false;
                return;
            }

            if (currentIndex < totalPrintCount)
            {
                int pageNum = pagesToPrint[currentIndex];
                progress?.Invoke(currentIndex + 1, totalPrintCount, $"Printing page {pageNum} of {totalDocPages}...");

                // Render page directly to bitmap (DPI scale: 200 DPI gives ~1654x2338 for A4)
                int renderWidth = (int)Math.Round(8.27 * options.Dpi); // ~A4 width in inches * dpi
                using (Bitmap? pageBmp = PdfPageThumbnailRenderer.RenderPageFromFile(pdfPath, pageNum, renderWidth, defaultFontName))
                {
                    if (pageBmp != null)
                    {
                        if (options.FitToPage)
                        {
                            e.Graphics?.DrawImage(pageBmp, e.PageBounds);
                        }
                        else
                        {
                            // Draw centered or native
                            e.Graphics?.DrawImage(pageBmp, 0, 0);
                        }
                    }
                }

                currentIndex++;
                e.HasMorePages = (currentIndex < totalPrintCount);
            }
            else
            {
                e.HasMorePages = false;
            }
        };

        printDoc.Print();
        progress?.Invoke(totalPrintCount, totalPrintCount, "Print job sent successfully.");
        return true;
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
            if (int.TryParse(part, out int singlePage))
            {
                if (singlePage >= 1 && singlePage <= totalPages)
                    result.Add(singlePage);
            }
            else if (part.Contains('-'))
            {
                string[] rangeParts = part.Split('-');
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

        var list = result.ToList();
        list.Sort();
        return list;
    }
}

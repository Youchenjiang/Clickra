using System.Drawing;
using Clickra.Core;
using Clickra.Core.Modules.Pdf.Split.Application;
using Clickra.Core.Modules.Sys.Settings.Application;
using Clickra.Core.Rendering;

namespace Clickra.Core.Modules.Pdf.Print.Application;

public sealed record PrintPageSizesResult(IReadOnlyList<PrintPageSize> SelectedPages, int TotalDocPages)
{
    public void Deconstruct(out IReadOnlyList<PrintPageSize> selectedPages, out int totalDocPages)
    {
        selectedPages = SelectedPages;
        totalDocPages = TotalDocPages;
    }
}

/// <summary>
/// Application query boundary for print preview layout and page rendering.
/// Keeps UI and presentation decoupled from Processing internals.
/// </summary>
public static class PrintPreviewQuery
{
    public static PrintPageSizesResult ReadPageSizes(string file, string? pageRange)
    {
        if (string.IsNullOrWhiteSpace(file) || !System.IO.File.Exists(file))
        {
            return new PrintPageSizesResult(Array.Empty<PrintPageSize>(), 0);
        }

        int totalDocPages = PdfPageCountQuery.Get(file);
        if (totalDocPages <= 0)
        {
            return new PrintPageSizesResult(Array.Empty<PrintPageSize>(), 0);
        }

        List<int> pages = PrintLayoutPlanner.ResolvePageNumbers(pageRange, totalDocPages);
        var dimensions = Processing.PdfPrintProcessor.ReadPageDimensions(file, pages);

        var list = new List<PrintPageSize>(pages.Count);
        foreach (int pageNum in pages)
        {
            if (dimensions.TryGetValue(pageNum, out var dim) && dim.widthPt > 0 && dim.heightPt > 0)
            {
                list.Add(new PrintPageSize(pageNum, dim.widthPt, dim.heightPt));
            }
            else
            {
                // Standard A4 default in points (595.28 x 841.89)
                list.Add(new PrintPageSize(pageNum, 595.28f, 841.89f));
            }
        }

        return new PrintPageSizesResult(list, totalDocPages);
    }

    public static Bitmap? RenderPage(
        string file,
        int pageNumber,
        int widthPx,
        bool darken = false,
        bool grayscale = false,
        string? language = null)
    {
        if (string.IsNullOrWhiteSpace(file) || !System.IO.File.Exists(file) || pageNumber < 1)
        {
            return null;
        }

        string effectiveLang = !string.IsNullOrWhiteSpace(language)
            ? language
            : SettingsController.Language;
        string fontName = LocalizedUiFontSelector.GetTextFontName(effectiveLang);

        Bitmap? bmp = PdfPageThumbnailRenderer.RenderPageFromFile(file, pageNumber, Math.Max(50, widthPx), fontName);
        if (bmp == null)
        {
            return null;
        }

        if (darken)
        {
            PdfContrastEnhancer.Enhance(bmp, PdfContrastEnhancer.DefaultGamma);
        }

        if (grayscale)
        {
            PdfContrastEnhancer.ConvertToGrayscale(bmp);
        }

        return bmp;
    }
}

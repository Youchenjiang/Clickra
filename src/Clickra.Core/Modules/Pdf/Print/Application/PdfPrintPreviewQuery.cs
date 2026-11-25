using System.Drawing;

namespace Clickra.Core.Modules.Pdf.Print.Application;

/// <summary>
/// Application query boundary for rendering print preview page thumbnails.
/// Preserved for backward compatibility with existing callers.
/// </summary>
public static class PdfPrintPreviewQuery
{
    public static Bitmap? RenderPageFromFile(
        string filePath,
        int pageNumber,
        int targetWidth,
        string? fontName = null,
        bool enhanceContrast = false)
    {
        return PrintPreviewQuery.RenderPage(filePath, pageNumber, targetWidth, darken: enhanceContrast);
    }
}

using System.Drawing;
using Clickra.Core;
using Clickra.Core.Modules.Sys.Settings.Application;
using Clickra.Core.Rendering;

namespace Clickra.Core.Modules.Pdf.Print.Application;

/// <summary>
/// Application query boundary for rendering print preview page thumbnails.
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
        fontName ??= LocalizedUiFontSelector.GetTextFontName(SettingsController.Language);
        var bmp = PdfPageThumbnailRenderer.RenderPageFromFile(filePath, pageNumber, targetWidth, fontName);
        if (bmp != null && enhanceContrast)
        {
            PdfContrastEnhancer.Enhance(bmp, 2.2);
        }
        return bmp;
    }
}

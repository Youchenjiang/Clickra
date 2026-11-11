using System.Drawing;
using Clickra.Core;
using Clickra.Core.Modules.Sys.Settings.Application;
using Clickra.Core.Rendering;

namespace Clickra.Core.Modules.Pdf.Split.Application;

/// <summary>
/// Application query boundary for rendering PDF page thumbnails without exposing
/// lower-level rendering internals directly to UI callers.
/// </summary>
public static class PdfPageThumbnailQuery
{
    public static Bitmap? RenderPageFromFile(
        string filePath,
        int pageNumber,
        int targetWidth,
        string? fontName = null,
        string? password = null)
    {
        fontName ??= LocalizedUiFontSelector.GetTextFontName(SettingsController.Language);
        return PdfPageThumbnailRenderer.RenderPageFromFile(filePath, pageNumber, targetWidth, fontName, password);
    }
}

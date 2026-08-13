using Clickra.Core.Rendering;

namespace Clickra.UI
{
    internal static class LocalizedUiFontSelector
    {
        public static string GetTextFontName(string language) => PdfPageThumbnailRenderer.GetTextFontName(language);
    }
}

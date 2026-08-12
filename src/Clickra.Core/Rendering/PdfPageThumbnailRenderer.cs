using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using UglyToad.PdfPig.Content;

namespace Clickra.Core.Rendering;

/// <summary>
/// Renders PDF pages to preview bitmaps: draws embedded images at their page
/// coordinates and overlays vector words (with original colors). Shared by the CLI
/// visual splitter and the Fluent split preview so both tracks render identically.
/// </summary>
public static class PdfPageThumbnailRenderer
{
    /// <summary>
    /// Renders a thumbnail at the page's true aspect ratio by drawing embedded images at
    /// their page coordinates and overlaying vector text (with original colors). This fixes
    /// previews that previously dropped vector text and were distorted by image-only sizing.
    /// </summary>
    /// <param name="page">The PdfPig page to render.</param>
    /// <param name="targetWidth">Pixel width of the rendered bitmap. Larger values give
    /// crisper results when the bitmap is downscaled onto the screen.</param>
    /// <param name="fontName">Font family used to overlay vector words, so CJK text
    /// renders with a font that actually contains the glyphs (Segoe UI does not).</param>
    public static Bitmap? RenderPage(Page page, int targetWidth, string fontName)
    {
        double pW = page.Width > 0 ? page.Width : 595;
        double pH = page.Height > 0 ? page.Height : 842;

        // Render at high resolution so the preview is always downscaled to fit the
        // card / zoom lightbox — upscaling a small bitmap is what made text and images
        // look blurry.
        int w = targetWidth;
        int h = Math.Max(120, (int)Math.Round(w * pH / pW));

        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

        try
        {
            DrawPageImages(g, page, pW, pH, w, h);
            DrawPageWords(g, page, pW, pH, w, h, fontName);
        }
        catch { /* Ignored: unrenderable page content falls back to a blank sheet. */ }

        return bmp;
    }

    /// <summary>Returns the UI font family for the current language, so thumbnails
    /// render CJK text with a font that actually contains the glyphs (Segoe UI does not).</summary>
    public static string GetTextFontName(string language)
    {
        string normalized = Localization.NormalizeLanguageCode(language);
        if (normalized.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase))
        {
            return "Microsoft JhengHei UI";
        }

        if (normalized.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return "Microsoft YaHei UI";
        }

        if (normalized.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return "Yu Gothic UI";
        }

        if (normalized.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
        {
            return "Malgun Gothic";
        }

        return "Segoe UI";
    }

    /// <summary>Opens the PDF at <paramref name="filePath"/> and renders one 1-based
    /// page, or null when the file or page cannot be read.</summary>
    public static Bitmap? RenderPageFromFile(string filePath, int pageNumber, int targetWidth, string fontName)
    {
        try
        {
            using var pigDoc = UglyToad.PdfPig.PdfDocument.Open(filePath);
            if (pageNumber < 1 || pageNumber > pigDoc.NumberOfPages) return null;
            return RenderPage(pigDoc.GetPage(pageNumber), targetWidth, fontName);
        }
        catch { /* Ignored: an unreadable PDF must not crash the preview. */ }
        return null;
    }

    /// <summary>Draws the page's embedded images onto the thumbnail at their page coordinates.
    /// The largest image is stretched as a full-page background when it covers most of the page.</summary>
    private static void DrawPageImages(Graphics g, Page page, double pW, double pH, int w, int h)
    {
        var images = page.GetImages().ToList();
        if (images.Count == 0) return;

        var largest = images
            .OrderByDescending(img => img.BoundingBox.Width * img.BoundingBox.Height)
            .First();

        using var imgBmp = TryDecodeEmbeddedImage(largest);
        if (imgBmp == null) return;

        var bb = largest.BoundingBox;
        if (bb.Width > pW * 0.7 && bb.Height > pH * 0.7)
        {
            // Full-page background: stretch to the whole thumbnail.
            g.DrawImage(imgBmp, 0, 0, w, h);
            return;
        }

        // Local image: draw at its page coordinates.
        float x = (float)(bb.Left / pW * w);
        float y = (float)((1.0 - bb.Top / pH) * h);
        float iw = (float)(bb.Width / pW * w);
        float ih = (float)(bb.Height / pH * h);
        if (iw > 2 && ih > 2)
            g.DrawImage(imgBmp, x, y, iw, ih);
    }

    /// <summary>Decodes an embedded image to a bitmap (PNG first, then raw bytes), or null
    /// when the stream is unsupported or malformed.</summary>
    private static Bitmap? TryDecodeEmbeddedImage(IPdfImage image)
    {
        try
        {
            if (image.TryGetPng(out var pngBytes) && pngBytes.Length > 100)
            {
                using var ms = new MemoryStream(pngBytes);
                return new Bitmap(ms);
            }

            var raw = image.RawBytes.ToArray();
            if (raw.Length > 100)
            {
                using var ms = new MemoryStream(raw);
                try { return new Bitmap(ms); } catch { /* Ignored: a malformed bitmap stream is skipped. */ }
            }
        }
        catch { /* Ignored: an undecodable embedded image is skipped. */ }
        return null;
    }

    /// <summary>Overlays the page's vector words (with original colors and positions) onto
    /// the thumbnail. Every word is drawn (no word cap), so dense pages render fully.</summary>
    private static void DrawPageWords(Graphics g, Page page, double pW, double pH, int w, int h, string fontName)
    {
        foreach (var word in page.GetWords())
        {
            var rect = word.BoundingBox;
            if (rect.Width <= 0 || rect.Height <= 0) continue;

            float fh = (float)(rect.Height / pH * h);
            if (fh < 2.5f) continue;

            float bx = (float)(rect.Left / pW * w);
            float by = (float)((1.0 - rect.Top / pH) * h);

            float fontSize = Math.Max(3f, Math.Min(fh * 1.1f, 18f * w / 220f));
            TryDrawWord(g, word.Text, ResolveWordColor(word), bx, by, fontSize, fontName);
        }
    }

    /// <summary>Returns the word's original color, or the default ink color when unset.</summary>
    private static Color ResolveWordColor(Word word)
    {
        if (word.Letters.Count > 0 && word.Letters[0].Color != null)
        {
            try
            {
                var (r, gg, b) = word.Letters[0].Color.ToRGBValues();
                return Color.FromArgb(
                    (int)Math.Clamp(r * 255.0, 0, 255),
                    (int)Math.Clamp(gg * 255.0, 0, 255),
                    (int)Math.Clamp(b * 255.0, 0, 255));
            }
            catch { /* Ignored: an invalid color value must not abort the word overlay. */ }
        }
        return Color.FromArgb(30, 35, 45);
    }

    /// <summary>Draws one word at the given position; returns false when drawing failed.</summary>
    private static bool TryDrawWord(Graphics g, string text, Color color, float x, float y, float fontSize, string fontName)
    {
        try
        {
            using var brush = new SolidBrush(color);
            using var font = new Font(fontName, fontSize, GraphicsUnit.Pixel);
            g.DrawString(text, font, brush, x, y);
            return true;
        }
        catch { /* Ignored: a malformed word must not abort the overlay. */ }
        return false;
    }
}

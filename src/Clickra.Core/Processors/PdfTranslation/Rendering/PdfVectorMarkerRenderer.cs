using Clickra.Core.Models;
using PdfSharp.Drawing;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics.Colors;

namespace Clickra.Core.Processors;

/// <summary>
/// Small filled circles used as inline numbered markers are vector paths in
/// the source PDF, not text. Body masks erase those paths, so they must be
/// restored after the translated text pass and the source digit must be
/// redrawn in white on top of the restored fill.
/// </summary>
internal sealed class PdfVectorMarker
{
    public required PdfParagraph Paragraph { get; init; }
    public required char Character { get; init; }
    public required double X0 { get; init; }
    public required double Y0 { get; init; }
    public required double X1 { get; init; }
    public required double Y1 { get; init; }
    public required double Red { get; init; }
    public required double Green { get; init; }
    public required double Blue { get; init; }
}

internal static class PdfVectorMarkerRenderer
{
    public static void EraseSource(
        XGraphics gfx,
        IReadOnlyList<PdfVectorMarker> markers)
    {
        double pageHeight = gfx.PageSize.Height;
        foreach (var marker in markers)
        {
            const double padding = 1.0;
            gfx.DrawRectangle(
                XBrushes.White,
                marker.X0 - padding,
                pageHeight - marker.Y1 - padding,
                (marker.X1 - marker.X0) + padding * 2.0,
                (marker.Y1 - marker.Y0) + padding * 2.0);
        }
    }

    public static List<PdfVectorMarker> Detect(
        Page pigPage,
        IReadOnlyList<PdfParagraph> paragraphs)
    {
        var markers = new List<PdfVectorMarker>();
        foreach (var path in pigPage.Paths)
        {
            if (!path.IsFilled || path.IsClipping) continue;
            var bounds = path.GetBoundingRectangle();
            if (bounds == null) continue;

            double width = bounds.Value.Right - bounds.Value.Left;
            double height = bounds.Value.Top - bounds.Value.Bottom;
            if (width < 5.0 || width > 15.0 || height < 5.0 || height > 15.0) continue;
            if (Math.Abs(width - height) > 2.0) continue;

            var digit = paragraphs
                .SelectMany(para => para.AllLetters.Select(letter => (para, letter)))
                .Where(item => item.letter.Value.Length == 1 && char.IsDigit(item.letter.Value[0]))
                .Where(item => item.letter.X >= bounds.Value.Left - 1.5 && item.letter.X <= bounds.Value.Right + 1.5 &&
                               item.letter.Y >= bounds.Value.Bottom - 1.5 && item.letter.Y <= bounds.Value.Top + 1.5)
                .OrderBy(item => Math.Abs((item.letter.X + item.letter.Right) / 2.0 -
                                          (bounds.Value.Left + bounds.Value.Right) / 2.0))
                .ThenBy(item => Math.Abs((item.letter.Y + item.letter.Top) / 2.0 -
                                         (bounds.Value.Bottom + bounds.Value.Top) / 2.0))
                .FirstOrDefault();
            if (digit.para == null)
            {
                continue;
            }
            // Diagram/image labels are fixed source artwork. Only inline
            // markers belonging to a translatable prose paragraph may be
            // erased and reflowed; bypassed/table/diagram regions must remain
            // byte-for-byte visually unchanged.
            if (digit.para.IsBypassed || digit.para.IsDiagram || digit.para.IsTable ||
                digit.para.IsGrayPromptContent)
            {
                continue;
            }

            ExtractRgbComponents(path.FillColor, out double red, out double green, out double blue);
            markers.Add(new PdfVectorMarker
            {
                Paragraph = digit.para,
                Character = digit.letter.Value[0],
                X0 = bounds.Value.Left,
                Y0 = bounds.Value.Bottom,
                X1 = bounds.Value.Right,
                Y1 = bounds.Value.Top,
                Red = red,
                Green = green,
                Blue = blue
            });
        }

        return markers;
    }

    public static void Render(
        XGraphics gfx,
        IReadOnlyList<PdfVectorMarker> markers,
        IReadOnlyDictionary<PdfParagraph, List<RenderedChar>> renderedCharsByParagraph)
    {
        double pageHeight = gfx.PageSize.Height;
        foreach (var marker in markers)
        {
            if (!renderedCharsByParagraph.TryGetValue(marker.Paragraph, out var renderedChars)) continue;

            var (centerX, centerY) = CalculateMarkerCenter(marker, renderedChars);
            double radiusX = (marker.X1 - marker.X0) / 2.0;
            double radiusY = (marker.Y1 - marker.Y0) / 2.0;
            var fill = XColor.FromArgb(
                255,
                (int)Math.Round(Math.Clamp(marker.Red, 0, 1) * 255),
                (int)Math.Round(Math.Clamp(marker.Green, 0, 1) * 255),
                (int)Math.Round(Math.Clamp(marker.Blue, 0, 1) * 255));
            gfx.DrawEllipse(new XSolidBrush(fill), centerX - radiusX, pageHeight - centerY - radiusY,
                radiusX * 2.0, radiusY * 2.0);

            double fontSize = Math.Max(5.0, Math.Min(radiusX, radiusY) * 1.65);
            var font = new XFont("Arial", fontSize, XFontStyleEx.Bold);
            double textWidth = gfx.MeasureString(marker.Character.ToString(), font).Width;
            double baseline = pageHeight - centerY + fontSize * 0.35;
            gfx.DrawString(marker.Character.ToString(), font, XBrushes.White,
                centerX - textWidth / 2.0, baseline);
        }
    }

    private static (double CenterX, double CenterY) CalculateMarkerCenter(
        PdfVectorMarker marker, List<RenderedChar> renderedChars)
    {
        double relCenterX = marker.Paragraph.Width > 0
            ? ((marker.X0 + marker.X1) / 2.0 - marker.Paragraph.X0) / marker.Paragraph.Width
            : 0.5;
        double relCenterY = marker.Paragraph.Height > 0
            ? ((marker.Y0 + marker.Y1) / 2.0 - marker.Paragraph.Y0) / marker.Paragraph.Height
            : 0.5;
        var cleanRendered = renderedChars.Where(character => !char.IsWhiteSpace(character.Character)).ToList();
        var occurrences = PdfAnnotationOccurrenceFinder.FindTextOccurrences(
            cleanRendered, marker.Character.ToString());
        var matched = occurrences.Count > 0
            ? PdfAnnotationSpatialMatcher.PickOccurrenceBySpatialPosition(
                occurrences,
                marker.Paragraph.X0 + relCenterX * marker.Paragraph.Width,
                marker.Paragraph.Y0 + relCenterY * marker.Paragraph.Height,
                -1,
                preferVerticalAlignment: true)
            : null;
        double centerX = matched?.Count > 0
            ? matched.Average(c => (c.Left + c.Right) / 2.0)
            : (marker.X0 + marker.X1) / 2.0;
        double centerY = matched?.Count > 0
            ? matched.Average(c => (c.Bottom + c.Top) / 2.0)
            : (marker.Y0 + marker.Y1) / 2.0;
        return (centerX, centerY);
    }

    private static void ExtractRgbComponents(IColor? color, out double red, out double green, out double blue)
    {
        switch (color)
        {
            case RGBColor rgb:
                red = rgb.R; green = rgb.G; blue = rgb.B; return;
            case GrayColor gray:
                red = gray.Gray; green = gray.Gray; blue = gray.Gray; return;
            default:
                red = green = blue = 0;
                return;
        }
    }
}

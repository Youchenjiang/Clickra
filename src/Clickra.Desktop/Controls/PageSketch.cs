using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Clickra.Desktop.Controls;

/// <summary>One line of a page sketch: width as a fraction of the text area, height, gap above it.</summary>
public sealed record SketchLine(double Width, double Height, double Gap, Color Color, bool Center = false);

/// <summary>
/// Draws a document page as a few lines (title, body, link), the way the template cards on the canvas
/// do. Widths are fractions so the sketch follows the card width. The line colors are the paper's ink,
/// not theme colors: the page is always light.
/// </summary>
public sealed class PageSketch : Control
{
    public static readonly StyledProperty<IReadOnlyList<SketchLine>?> LinesProperty =
        AvaloniaProperty.Register<PageSketch, IReadOnlyList<SketchLine>?>(nameof(Lines));

    public static readonly StyledProperty<Thickness> PaddingProperty =
        AvaloniaProperty.Register<PageSketch, Thickness>(nameof(Padding));

    static PageSketch() => AffectsRender<PageSketch>(LinesProperty, PaddingProperty);

    public IReadOnlyList<SketchLine>? Lines { get => GetValue(LinesProperty); set => SetValue(LinesProperty, value); }
    public Thickness Padding { get => GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Lines is null) return;
        Thickness pad = Padding;
        double left = pad.Left;
        double available = Bounds.Width - pad.Left - pad.Right;
        if (available <= 0) return;
        double y = pad.Top;
        foreach (SketchLine line in Lines)
        {
            y += line.Gap;
            if (y >= Bounds.Height) break;
            double width = available * line.Width;
            double x = line.Center ? left + (available - width) / 2 : left;
            context.DrawRectangle(new SolidColorBrush(line.Color), null, new Rect(x, y, width, line.Height), 1, 1);
            y += line.Height;
        }
    }
}

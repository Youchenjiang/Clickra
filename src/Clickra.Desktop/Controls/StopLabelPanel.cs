using System;
using Avalonia;
using Avalonia.Controls;

namespace Clickra.Desktop.Controls;

/// <summary>
/// Places each child under its slider stop: child i sits at i / (count - 1) of the track width.
/// The first and last labels are kept inside the panel so they never overflow at the edges.
/// </summary>
public sealed class StopLabelPanel : Panel
{
    public static readonly StyledProperty<double> TrackInsetProperty =
        AvaloniaProperty.Register<StopLabelPanel, double>(nameof(TrackInset), 10);

    static StopLabelPanel()
    {
        AffectsArrange<StopLabelPanel>(TrackInsetProperty);
    }

    /// <summary>Distance from the panel edge to the first and last stop centers.</summary>
    public double TrackInset
    {
        get => GetValue(TrackInsetProperty);
        set => SetValue(TrackInsetProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0;
        foreach (Control child in Children)
        {
            child.Measure(Size.Infinity);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = Children.Count;
        double track = Math.Max(0, finalSize.Width - 2 * TrackInset);
        for (int i = 0; i < count; i++)
        {
            Control child = Children[i];
            double childWidth = child.DesiredSize.Width;
            double fraction = count == 1 ? 0.5 : (double)i / (count - 1);
            double center = TrackInset + fraction * track;
            double x = Math.Clamp(center - childWidth / 2, 0, Math.Max(0, finalSize.Width - childWidth));
            child.Arrange(new Rect(x, 0, childWidth, child.DesiredSize.Height));
        }

        return finalSize;
    }
}

using System;
using Avalonia;
using Avalonia.Controls;

namespace Clickra.Desktop.Controls;

/// <summary>
/// Vertical stack that shows only the children that fit completely in the available height (spec 5.2:
/// 轉檔 A never scrolls; when space runs short it shows fewer 最近處理 rows, 3 → 0, instead of cutting a row).
/// Children that do not fit are arranged outside the panel and clipped away.
/// </summary>
public sealed class FitStackPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<FitStackPanel, double>(nameof(Spacing), 8);

    private int _fitCount;

    static FitStackPanel()
    {
        AffectsMeasure<FitStackPanel>(SpacingProperty);
        ClipToBoundsProperty.OverrideDefaultValue<FitStackPanel>(true);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double used = 0, width = 0;
        _fitCount = 0;
        foreach (Control child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            if (!child.IsVisible) continue;
            double next = used + (_fitCount > 0 ? Spacing : 0) + child.DesiredSize.Height;
            // 1 px tolerance: with display scaling the height handed down after layout rounding can be a
            // fraction smaller than what the rows asked for, which used to drop the last row and leave a gap.
            if (next > availableSize.Height + 1) break;
            used = next;
            width = Math.Max(width, child.DesiredSize.Width);
            _fitCount++;
        }
        return new Size(width, used);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        int placed = 0;
        foreach (Control child in Children)
        {
            if (!child.IsVisible) continue;
            if (placed < _fitCount)
            {
                if (placed > 0) y += Spacing;
                child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
                y += child.DesiredSize.Height;
                placed++;
            }
            else
            {
                // Out of view (the panel clips to its bounds).
                child.Arrange(new Rect(0, finalSize.Height + 1, finalSize.Width, child.DesiredSize.Height));
            }
        }
        return finalSize;
    }
}

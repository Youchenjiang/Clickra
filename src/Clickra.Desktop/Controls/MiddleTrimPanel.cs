using System;
using Avalonia;
using Avalonia.Controls;

namespace Clickra.Desktop.Controls;

/// <summary>
/// One line of text pieces laid out left to right: [head][tail][more…]. Only the first child (the start of
/// a file name, with TextTrimming) gives up width, so the end of the name and its extension always show
/// (spec 4.2: 檔名放不下時省略中間、保留結尾與副檔名). When even that is not enough, the last child (a
/// reason such as 「檔案已損毀」) is narrowed next. Children after the second are separated by <see cref="Gap"/>.
/// </summary>
public sealed class MiddleTrimPanel : Panel
{
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<MiddleTrimPanel, double>(nameof(Gap), 8);

    private const double MinHeadWidth = 40;

    static MiddleTrimPanel()
    {
        AffectsMeasure<MiddleTrimPanel>(GapProperty);
        ClipToBoundsProperty.OverrideDefaultValue<MiddleTrimPanel>(true);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    private double[] _widths = Array.Empty<double>();

    protected override Size MeasureOverride(Size availableSize)
    {
        int n = Children.Count;
        _widths = new double[n];
        (double height, double rest) = MeasureTrailing(availableSize);

        double width = availableSize.Width;
        if (n > 0)
        {
            height = Math.Max(height, MeasureHead(width, ref rest, availableSize));
        }

        double total = 0;
        for (int i = 0; i < n; i++)
            if (Children[i].IsVisible) total += _widths[i] + (i >= 2 ? Gap : 0);
        return new Size(double.IsInfinity(width) ? total : Math.Min(total, width), height);
    }

    private (double height, double rest) MeasureTrailing(Size availableSize)
    {
        double height = 0, rest = 0;
        for (int i = 1; i < Children.Count; i++)
        {
            Control c = Children[i];
            c.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (!c.IsVisible) continue;
            _widths[i] = c.DesiredSize.Width;
            rest += _widths[i] + (i >= 2 ? Gap : 0);
            height = Math.Max(height, c.DesiredSize.Height);
        }
        return (height, rest);
    }

    private double MeasureHead(double width, ref double rest, Size availableSize)
    {
        Control head = Children[0];
        head.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        double headWant = head.DesiredSize.Width;
        if (!double.IsInfinity(width))
        {
            // Not enough room: narrow the last piece first, keeping a minimum for the head.
            double over = headWant + rest - width;
            int last = Children.Count - 1;
            if (over > 0 && last >= 2 && Children[last].IsVisible)
            {
                double cut = Math.Min(over, Math.Max(0, _widths[last] - 60));
                _widths[last] -= cut;
                rest -= cut;
                Children[last].Measure(new Size(_widths[last], availableSize.Height));
            }
            headWant = Math.Max(Math.Min(headWant, width - rest), Math.Min(MinHeadWidth, headWant));
        }
        _widths[0] = Math.Max(0, headWant);
        head.Measure(new Size(_widths[0], availableSize.Height));
        return head.DesiredSize.Height;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            Control c = Children[i];
            if (!c.IsVisible) continue;
            if (i >= 2) x += Gap;
            double w = i < _widths.Length ? _widths[i] : c.DesiredSize.Width;
            c.Arrange(new Rect(x, (finalSize.Height - c.DesiredSize.Height) / 2, w, c.DesiredSize.Height));
            x += w;
        }
        return finalSize;
    }
}

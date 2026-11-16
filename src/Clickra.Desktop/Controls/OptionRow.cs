using System;
using Avalonia;
using Avalonia.Controls;

namespace Clickra.Desktop.Controls;

/// <summary>
/// Short options side by side (spec 4.9, decision 2026-11-17 「D2」): each child is one option (its label
/// above its control) and gets an equal column. As many columns as fit at <see cref="MinItemWidth"/>
/// are used; the rest wrap to the next line, so a narrow window or a longer language just stacks them.
/// Children are read left to right, top to bottom.
/// </summary>
public sealed class OptionRow : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<OptionRow, double>(nameof(MinItemWidth), 200);
    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<OptionRow, double>(nameof(ColumnSpacing), 28);
    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<OptionRow, double>(nameof(RowSpacing), 14);

    static OptionRow() => AffectsMeasure<OptionRow>(MinItemWidthProperty, ColumnSpacingProperty, RowSpacingProperty);

    public double MinItemWidth { get => GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public double ColumnSpacing { get => GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }
    public double RowSpacing { get => GetValue(RowSpacingProperty); set => SetValue(RowSpacingProperty, value); }

    private int Columns(double width, int count)
    {
        if (count == 0) return 1;
        if (double.IsInfinity(width)) return count;
        int fit = (int)Math.Floor((width + ColumnSpacing) / (MinItemWidth + ColumnSpacing));
        return Math.Clamp(fit, 1, count);
    }

    private static int VisibleCount(Avalonia.Controls.Controls children)
    {
        int n = 0;
        foreach (Control c in children) if (c.IsVisible) n++;
        return n;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = VisibleCount(Children);
        int columns = Columns(availableSize.Width, count);
        double cellWidth = double.IsInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, (availableSize.Width - ColumnSpacing * (columns - 1)) / columns);

        double widest = 0;
        foreach (Control child in Children)
        {
            if (!child.IsVisible) continue;
            child.Measure(new Size(cellWidth, double.PositiveInfinity));
            widest = Math.Max(widest, child.DesiredSize.Width);
        }
        int rows = count == 0 ? 0 : (count + columns - 1) / columns;
        double height = 0;
        int index = 0;
        double[] rowHeights = new double[Math.Max(rows, 1)];
        foreach (Control child in Children)
        {
            if (!child.IsVisible) continue;
            int r = index++ / columns;
            rowHeights[r] = Math.Max(rowHeights[r], child.DesiredSize.Height);
        }
        for (int r = 0; r < rows; r++) height += rowHeights[r];
        height += RowSpacing * Math.Max(0, rows - 1);

        double width = double.IsInfinity(availableSize.Width)
            ? widest * columns + ColumnSpacing * (columns - 1)
            : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = VisibleCount(Children);
        int columns = Columns(finalSize.Width, count);
        double cellWidth = Math.Max(0, (finalSize.Width - ColumnSpacing * (columns - 1)) / columns);

        int rows = count == 0 ? 0 : (count + columns - 1) / columns;
        double[] rowHeights = new double[Math.Max(rows, 1)];
        int index = 0;
        foreach (Control child in Children)
        {
            if (!child.IsVisible) continue;
            int r = index++ / columns;
            rowHeights[r] = Math.Max(rowHeights[r], child.DesiredSize.Height);
        }

        index = 0;
        double y = 0;
        int currentRow = 0;
        foreach (Control child in Children)
        {
            if (!child.IsVisible) continue;
            int r = index / columns, c = index % columns;
            if (r != currentRow) { y += rowHeights[currentRow] + RowSpacing; currentRow = r; }
            child.Arrange(new Rect(c * (cellWidth + ColumnSpacing), y, cellWidth, rowHeights[r]));
            index++;
        }
        return finalSize;
    }
}

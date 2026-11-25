using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Clickra.Desktop.ViewModels;

namespace Clickra.Desktop.Controls;

public sealed class PrintSheetControl : Control
{
    public static readonly StyledProperty<PrintSheetViewModel?> SheetProperty =
        AvaloniaProperty.Register<PrintSheetControl, PrintSheetViewModel?>(nameof(Sheet));

    static PrintSheetControl()
    {
        AffectsRender<PrintSheetControl>(SheetProperty);
    }

    public PrintSheetViewModel? Sheet
    {
        get => GetValue(SheetProperty);
        set => SetValue(SheetProperty, value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.ClickCount == 2)
        {
            if (DataContext is ConvertViewModel { IsPrintAction: true } cvm)
            {
                cvm.PrintPreview.OpenZoom();
            }
            else if (DataContext is PrintPreviewViewModel pvm)
            {
                pvm.OpenZoom();
            }
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SheetProperty)
        {
            if (change.OldValue is PrintSheetViewModel oldSheet)
                oldSheet.Invalidated -= OnSheetInvalidated;
            if (change.NewValue is PrintSheetViewModel newSheet)
                newSheet.Invalidated += OnSheetInvalidated;
            InvalidateVisual();
        }
    }

    private void OnSheetInvalidated() => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        var sheet = Sheet;
        double availW = Math.Max(0, Bounds.Width);
        double availH = Math.Max(0, Bounds.Height);
        if (availW <= 10 || availH <= 10) return;

        if (sheet is null || sheet.WidthPt <= 0 || sheet.HeightPt <= 0)
        {
            RenderPlaceholder(context, availW, availH);
            return;
        }

        double scale = Math.Min(availW / sheet.WidthPt, availH / sheet.HeightPt);
        double paperW = sheet.WidthPt * scale;
        double paperH = sheet.HeightPt * scale;
        double left = (availW - paperW) / 2;
        double top = (availH - paperH) / 2;

        var paperRect = new Rect(left, top, paperW, paperH);

        // White paper background + subtle border
        context.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0xD5, 0xDD)), 1), paperRect);

        // Clip rendering to paper rectangle
        using (context.PushClip(paperRect))
        {
            RenderCells(context, sheet, left, top, scale);
        }
    }

    private static void RenderPlaceholder(DrawingContext context, double availW, double availH)
    {
        double placeholderW = Math.Min(availW * 0.7, 300);
        double placeholderH = placeholderW * 1.414;
        if (placeholderH > availH * 0.85)
        {
            placeholderH = availH * 0.85;
            placeholderW = placeholderH / 1.414;
        }
        double placeholderLeft = (availW - placeholderW) / 2;
        double placeholderTop = (availH - placeholderH) / 2;
        var rect = new Rect(placeholderLeft, placeholderTop, placeholderW, placeholderH);
        var dashPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)), 1.5, new DashStyle(new double[] { 3, 3 }, 0));
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)), dashPen, rect);

        var loadingText = new FormattedText(
            Clickra.Desktop.L10n.Strings.T("ui_print_loading_preview"),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default),
            13,
            new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)));
        context.DrawText(loadingText, new Point(rect.Center.X - loadingText.Width / 2, rect.Center.Y - loadingText.Height / 2));
    }

    private static void RenderCells(DrawingContext context, PrintSheetViewModel sheet, double left, double top, double scale)
    {
        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0xD5, 0xDD)), 0.5);
        var pageNumTypeface = new Typeface(FontFamily.Default);

        foreach (var cell in sheet.Cells)
        {
            var pageRect = new Rect(left + cell.Page.X * scale, top + cell.Page.Y * scale, cell.Page.Width * scale, cell.Page.Height * scale);
            if (cell.Thumbnail is not null)
            {
                context.DrawImage(cell.Thumbnail, pageRect);
            }
            else
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)), null, pageRect);
                var placeholderText = new FormattedText(cell.PageNumber.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, pageNumTypeface, 11, Brushes.Gray);
                context.DrawText(placeholderText, new Point(pageRect.Center.X - placeholderText.Width / 2, pageRect.Center.Y - placeholderText.Height / 2));
            }

            if (sheet.PageBorder && sheet.PagesPerSheet > 1)
            {
                context.DrawRectangle(null, borderPen, pageRect);
            }

            if (sheet.PagesPerSheet > 1)
            {
                var numText = new FormattedText(cell.PageNumber.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, pageNumTypeface, 10, new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63)));
                double numX = left + (cell.Cell.X + cell.Cell.Width) * scale - numText.Width - 4;
                double numY = top + (cell.Cell.Y + cell.Cell.Height) * scale - numText.Height - 2;
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), null, new Rect(numX - 1, numY - 1, numText.Width + 2, numText.Height + 2), 2, 2);
                context.DrawText(numText, new Point(numX, numY));
            }
        }

        // Orange dashed printable area overlay if overflow (drawn on top of cells)
        if (sheet.Overflows)
        {
            float shortSide = Math.Min(sheet.WidthPt, sheet.HeightPt);
            float margin = shortSide * 0.03f;
            var dashPen = new Pen(new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)), 1.5, new DashStyle(new double[] { 4, 4 }, 0));
            var printArea = new Rect(left + margin * scale, top + margin * scale, (sheet.WidthPt - 2 * margin) * scale, (sheet.HeightPt - 2 * margin) * scale);
            context.DrawRectangle(null, dashPen, printArea);
        }
    }
}

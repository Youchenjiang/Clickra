using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Clickra.Desktop.ViewModels;

namespace Clickra.Desktop.Views;

/// <summary>What the user picked in 「還在處理，要關閉嗎？」; null when the dialog was closed with ✕ or Esc.</summary>
public sealed record CloseChoice(CloseWhileRunning Action, bool Remember);

/// <summary>Small modal dialogs built in code. Buttons: primary at the right, cancel to its left (spec 7).</summary>
public static class Dialogs
{
    public static async Task<bool> ConfirmAsync(Window owner, string title, string body, string confirmText, bool danger)
    {
        Window dialog = CreateWindow(owner, 400);
        var cancel = new Button { Content = "取消", MinHeight = 36, Padding = new Thickness(18, 0) };
        cancel.Classes.Add("secondary");
        var confirm = new Button { Content = confirmText, MinHeight = 36, Padding = new Thickness(18, 0), FontSize = 14 };
        confirm.Classes.Add(danger ? "danger" : "primary");
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 20),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                Secondary(body),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(0, 16, 0, 0),
                    Children = { cancel, confirm },
                },
            },
        };
        bool? result = await dialog.ShowDialog<bool?>(owner);
        return result == true;
    }

    /// <summary>
    /// Spec 4.5: closing the window while files are being processed. Two large choices; ✕ or Esc keeps the
    /// window open. 「下次不再詢問」 saves the choice to Settings › 行為.
    /// </summary>
    public static Task<CloseChoice?> AskCloseAsync(Window owner, int remaining)
    {
        Window dialog = CreateWindow(owner, 420);
        var remember = new CheckBox { Content = "下次不再詢問", FontSize = 13 };
        ToolTip.SetTip(remember, "之後可以在「設定 › 行為」更改");

        Button background = ChoiceButton("在背景繼續",
            "M3,5 H21 V16 H3 Z M8,20 H16 M12,16 V20", filled: false, accent: true);
        Button stop = ChoiceButton(remaining > 0 ? $"停止（剩下 {remaining} 個不處理）" : "停止",
            "M12,3 A9,9 0 1 1 11.99,3 Z M9,9 H15 V15 H9 Z", filled: false, accent: false);
        background.Click += (_, _) => dialog.Close(new CloseChoice(CloseWhileRunning.Background, remember.IsChecked == true));
        stop.Click += (_, _) => dialog.Close(new CloseChoice(CloseWhileRunning.Stop, remember.IsChecked == true));

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "還在處理，要關閉嗎？", FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) },
                background,
                stop,
                remember,
            },
        };
        return dialog.ShowDialog<CloseChoice?>(owner);
    }

    private static Button ChoiceButton(string text, string icon, bool filled, bool accent)
    {
        // Same 24 × 24 grid and stroke as every other line icon (Styles/Controls.axaml, Viewbox.ic).
        var path = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(icon) };
        path.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, path.GetResourceObservable(accent ? "Clk.Accent" : "Clk.Text"));
        var glyph = new Viewbox
        {
            Width = 20,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Canvas { Width = 24, Height = 24, Children = { path } },
        };
        glyph.Classes.Add("ic");
        var button = new Button
        {
            MinHeight = 56,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(18, 0),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(accent ? 2 : 1),
            FontSize = 15,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children = { glyph, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        button.Classes.Add("secondary");
        if (accent) button.Bind(Button.BorderBrushProperty, button.GetResourceObservable("Clk.Accent"));
        return button;
    }

    private static TextBlock Secondary(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap };
        block.Classes.Add("secondary");
        return block;
    }

    private static Window CreateWindow(Window owner, double width)
    {
        var dialog = new Window
        {
            Title = "Clickra",
            Width = width,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = owner.FontFamily,
            FontSize = 14,
            Icon = owner.Icon,
        };
        dialog.Bind(Window.BackgroundProperty, dialog.GetResourceObservable("Clk.Bg"));
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) dialog.Close(null);
        };
        return dialog;
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Clickra.Core.Processors;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Clickra_Fluent;

/// <summary>Shared modal dialogs used by multiple Fluent pages.</summary>
internal static class FluentDialogs
{
    /// <summary>Shows one-shot Markdown presentation choices. Defaults are intentionally
    /// production-quality so users can simply press Convert without tuning anything.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S3776", Justification = "The dialog composes one cohesive XAML form and its validation callbacks; splitting it would obscure control wiring.")]
    public static async Task<Dictionary<string, object>?> PromptMarkdownPdfOptionsAsync(
        XamlRoot xamlRoot,
        Func<string, string> localize,
        Window? ownerWindow,
        Action<ContentDialog>? trackDialog = null)
    {
        string? templatePath = null;
        var style = CreateCombo(
            localize("md_options_style_default"),
            localize("md_options_style_minimal"),
            localize("md_options_style_academic"));
        var paper = CreateCombo("A4", "Letter");
        var textSize = CreateCombo(
            localize("md_options_text_small"),
            localize("md_options_text_standard"),
            localize("md_options_text_large"));
        textSize.SelectedIndex = 1;
        var codeTheme = CreateCombo(
            localize("md_options_code_dark"),
            localize("md_options_code_light"));

        var primary = new StackPanel { Spacing = 8 };
        primary.Children.Add(new TextBlock
        {
            Text = localize("md_options_hint"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 8)
        });

        ComboBox? layoutSource = CreateCombo(
            localize("md_options_layout_clickra"),
            localize("md_options_layout_word"));
        TextBlock? templateStatus = null;
        AddCompactLabeledControl(primary, localize("md_options_layout_source"), layoutSource);

        var stylePanel = new StackPanel { Spacing = 0 };
        AddCompactLabeledControl(stylePanel, localize("md_options_style"), style);
        primary.Children.Add(stylePanel);

        var templatePicker = new Grid { ColumnSpacing = 8 };
        templatePicker.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        templatePicker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var templateBrowse = new Button
        {
            Content = localize("md_options_template_browse"),
            MinWidth = 132
        };
        templateStatus = new TextBlock
        {
            Text = localize("md_options_template_none"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(templateBrowse, 0);
        Grid.SetColumn(templateStatus, 1);
        templatePicker.Children.Add(templateBrowse);
        templatePicker.Children.Add(templateStatus);
        var templatePanel = new StackPanel { Spacing = 0, Visibility = Visibility.Collapsed };
        AddCompactLabeledControl(templatePanel, localize("md_options_template"), templatePicker);
        templateBrowse.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".docx");
            if (ownerWindow is not null)
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(ownerWindow));

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            try
            {
                MarkdownTemplateSource.Load(file.Path);
                templatePath = file.Path;
                templateStatus.Text = string.Format(localize("md_options_template_selected"), Path.GetFileName(file.Path));
            }
            catch
            {
                templatePath = null;
                templateStatus.Text = localize("md_options_template_invalid");
            }
        };
        primary.Children.Add(templatePanel);

        layoutSource.SelectionChanged += (_, _) =>
        {
            bool useTemplate = layoutSource.SelectedIndex == 1;
            stylePanel.Visibility = useTemplate ? Visibility.Collapsed : Visibility.Visible;
            templatePanel.Visibility = useTemplate ? Visibility.Visible : Visibility.Collapsed;
        };
        AddCompactLabeledControl(primary, localize("md_options_paper"), paper);
        AddCompactLabeledControl(primary, localize("md_options_text_size"), textSize);
        AddCompactLabeledControl(primary, localize("md_options_code_theme"), codeTheme);

        var dialog = new ContentDialog
        {
            Title = localize("md_options_title"),
            Content = primary,
            PrimaryButtonText = localize("md_options_convert"),
            CloseButtonText = localize("dialog_cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (layoutSource?.SelectedIndex != 1) return;
            if (templatePath is null)
            {
                args.Cancel = true;
                if (templateStatus is not null) templateStatus.Text = localize("md_options_template_required");
                return;
            }
            try
            {
                MarkdownTemplateSource.Load(templatePath);
            }
            catch
            {
                args.Cancel = true;
                templatePath = null;
                if (templateStatus is not null) templateStatus.Text = localize("md_options_template_invalid");
            }
        };
        trackDialog?.Invoke(dialog);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        string theme = style.SelectedIndex switch
        {
            1 => MarkdownPdfOptions.ThemeMinimal,
            2 => MarkdownPdfOptions.ThemeAcademic,
            _ => MarkdownPdfOptions.ThemeDefault
        };
        string paperValue = paper.SelectedIndex == 1 ? MarkdownPdfOptions.PaperLetter : MarkdownPdfOptions.PaperA4;
        string textValue = textSize.SelectedIndex switch
        {
            0 => MarkdownPdfOptions.TextSmall,
            2 => MarkdownPdfOptions.TextLarge,
            _ => MarkdownPdfOptions.TextStandard
        };
        string codeValue = codeTheme.SelectedIndex == 1 ? MarkdownPdfOptions.CodeLight : MarkdownPdfOptions.CodeDark;
        string? selectedTemplatePath = layoutSource?.SelectedIndex == 1 ? templatePath : null;
        return MarkdownPdfOptions.Create(theme, paperValue, textValue, codeValue, selectedTemplatePath);
    }

    private static ComboBox CreateCombo(params string[] items)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = 0 };
        foreach (string item in items) combo.Items.Add(item);
        return combo;
    }

    private static void AddCompactLabeledControl(StackPanel panel, string label, FrameworkElement control)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(control, 1);
        row.Children.Add(labelBlock);
        row.Children.Add(control);
        panel.Children.Add(row);
    }

    /// <summary>Asks for a PDF password; returns null when the user cancels.
    /// <paramref name="trackDialog"/> lets the caller keep a reference to the live dialog
    /// so it can be dismissed programmatically (e.g. when the task gets parked).</summary>
    public static async Task<string?> PromptPasswordAsync(XamlRoot xamlRoot, Func<string, string> localize, Action<ContentDialog>? trackDialog = null)
    {
        var box = new PasswordBox { PlaceholderText = localize("fluent_pdf_password_placeholder") };
        var dialog = new ContentDialog
        {
            Title = localize("fluent_pdf_password"),
            Content = box,
            PrimaryButtonText = localize("fluent_ok"),
            CloseButtonText = localize("dialog_cancel"),
            XamlRoot = xamlRoot
        };
        trackDialog?.Invoke(dialog);
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Password : null;
    }
}

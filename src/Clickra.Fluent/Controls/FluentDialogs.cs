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
            Margin = new Thickness(0, 0, 0, 4)
        });
        AddLabeledControl(primary, localize("md_options_style"), style);
        AddLabeledControl(primary, localize("md_options_paper"), paper);
        AddLabeledControl(primary, localize("md_options_text_size"), textSize);

        var advanced = new StackPanel { Spacing = 8 };
        AddLabeledControl(advanced, localize("md_options_code_theme"), codeTheme);
        advanced.Children.Add(new TextBlock
        {
            Text = localize("md_options_template"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        var templateBrowse = new Button { Content = localize("md_options_template_browse") };
        var templateStatus = new TextBlock
        {
            Text = localize("md_options_template_none"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        };
        templateBrowse.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            if (ownerWindow is not null)
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(ownerWindow));

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            try
            {
                MarkdownTemplateFile.Load(file.Path);
                templatePath = file.Path;
                templateStatus.Text = string.Format(localize("md_options_template_selected"), Path.GetFileName(file.Path));
            }
            catch
            {
                templatePath = null;
                templateStatus.Text = localize("md_options_template_invalid");
            }
        };
        advanced.Children.Add(templateBrowse);
        advanced.Children.Add(templateStatus);
        primary.Children.Add(new Expander
        {
            Header = localize("md_options_more"),
            Content = advanced,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

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
            if (templatePath is null) return;
            try
            {
                MarkdownTemplateFile.Load(templatePath);
            }
            catch
            {
                args.Cancel = true;
                templatePath = null;
                templateStatus.Text = localize("md_options_template_invalid");
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
        return MarkdownPdfOptions.Create(theme, paperValue, textValue, codeValue, templatePath);
    }

    private static ComboBox CreateCombo(params string[] items)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = 0 };
        foreach (string item in items) combo.Items.Add(item);
        return combo;
    }

    private static void AddLabeledControl(StackPanel panel, string label, Control control)
    {
        panel.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(control);
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

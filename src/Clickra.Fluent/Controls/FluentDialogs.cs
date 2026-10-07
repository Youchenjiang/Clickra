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
        string command,
        Window? ownerWindow,
        Action<ContentDialog>? trackDialog = null)
    {
        bool isWord = command.Equals("md2word", StringComparison.OrdinalIgnoreCase);
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

        ComboBox? layoutSource = null;
        TextBlock? templateStatus = null;
        if (isWord)
        {
            layoutSource = CreateCombo(
                localize("md_options_layout_clickra"),
                localize("md_options_layout_word"));
            AddLabeledControl(primary, localize("md_options_layout_source"), layoutSource);

            var stylePanel = new StackPanel { Spacing = 4 };
            AddLabeledControl(stylePanel, localize("md_options_style"), style);
            primary.Children.Add(stylePanel);

            var templatePanel = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
            templatePanel.Children.Add(new TextBlock
            {
                Text = localize("md_options_template"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            var templateBrowse = new Button { Content = localize("md_options_template_browse") };
            templateStatus = new TextBlock
            {
                Text = localize("md_options_template_none"),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75
            };
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
            templatePanel.Children.Add(templateBrowse);
            templatePanel.Children.Add(templateStatus);
            primary.Children.Add(templatePanel);

            layoutSource.SelectionChanged += (_, _) =>
            {
                bool useTemplate = layoutSource.SelectedIndex == 1;
                stylePanel.Visibility = useTemplate ? Visibility.Collapsed : Visibility.Visible;
                templatePanel.Visibility = useTemplate ? Visibility.Visible : Visibility.Collapsed;
            };
        }
        else
        {
            AddLabeledControl(primary, localize("md_options_style"), style);
        }
        AddLabeledControl(primary, localize("md_options_paper"), paper);
        AddLabeledControl(primary, localize("md_options_text_size"), textSize);

        var advanced = new StackPanel { Spacing = 8 };
        AddLabeledControl(advanced, localize("md_options_code_theme"), codeTheme);
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
            if (!isWord || layoutSource?.SelectedIndex != 1) return;
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
        string? selectedTemplatePath = isWord && layoutSource?.SelectedIndex == 1 ? templatePath : null;
        return MarkdownPdfOptions.Create(theme, paperValue, textValue, codeValue, selectedTemplatePath);
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

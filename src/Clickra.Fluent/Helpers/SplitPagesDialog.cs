using Clickra.Core;
using Clickra.Core.Processors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace Clickra_Fluent;

/// <summary>
/// Shared 3-mode PDF split dialog: custom segments, split each page, or a fixed
/// number of pages per segment. Builds the page-range spec through
/// <see cref="PdfSplitProcessor.BuildSegmentSpec"/> so both Fluent entry points
/// (dashboard and packaged activation) share one Core source of truth.
/// </summary>
public static class SplitPagesDialog
{
    /// <summary>
    /// Prompts the user for a split mode and returns the page-range spec
    /// ("1-3; 5-7", "all", or "1-5; 6-10"), or null when the dialog is cancelled.
    /// </summary>
    /// <param name="xamlRoot">XamlRoot of the hosting page, required by ContentDialog.</param>
    /// <param name="pdfPath">Source PDF used to clamp the fixed-pages selector to the real page count.</param>
    public static async Task<string?> PromptAsync(XamlRoot xamlRoot, string pdfPath)
    {
        int totalPages = FileProcessor.GetPdfPageCount(pdfPath);
        if (totalPages <= 0) totalPages = 1;

        string L(string key) => Localization.T(key, ClickraStorage.GetSetting("Language"));

        var modeBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedIndex = 0
        };
        modeBox.Items.Add(L("pdf_split_mode_custom"));
        modeBox.Items.Add(L("pdf_split_mode_each"));
        modeBox.Items.Add(L("pdf_split_mode_fixed"));

        // Seed the custom-segments box with halves of the document, mirroring the
        // CLI visual splitter's initial state.
        int half = totalPages / 2;
        string seed = totalPages == 1 ? "1" : $"1-{half}; {half + 1}-{totalPages}";

        var rangeBox = new TextBox
        {
            Text = seed,
            PlaceholderText = L("pdf_split_prompt")
        };

        var pagesLabel = new TextBlock
        {
            Text = L("pdf_split_pages_per_segment"),
            Visibility = Visibility.Collapsed
        };

        var nBox = new NumberBox
        {
            Minimum = 1,
            Maximum = totalPages,
            Value = Math.Min(5, totalPages),
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Visibility = Visibility.Collapsed
        };

        void UpdateVisibility()
        {
            bool custom = modeBox.SelectedIndex == 0;
            bool fixedMode = modeBox.SelectedIndex == 2;
            rangeBox.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            pagesLabel.Visibility = fixedMode ? Visibility.Visible : Visibility.Collapsed;
            nBox.Visibility = fixedMode ? Visibility.Visible : Visibility.Collapsed;
        }
        modeBox.SelectionChanged += (_, _) => UpdateVisibility();

        var panel = new StackPanel { Spacing = 8, MinWidth = 320 };
        panel.Children.Add(modeBox);
        panel.Children.Add(rangeBox);
        panel.Children.Add(pagesLabel);
        panel.Children.Add(nBox);

        var dialog = new ContentDialog
        {
            Title = L("pdf_split_title"),
            Content = panel,
            PrimaryButtonText = L("fluent_ok"),
            CloseButtonText = L("dialog_cancel"),
            XamlRoot = xamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        return modeBox.SelectedIndex switch
        {
            1 => "all",
            2 => PdfSplitProcessor.BuildSegmentSpec(2, (int)Math.Round(nBox.Value), totalPages, Array.Empty<(int, int)>()),
            _ => string.IsNullOrWhiteSpace(rangeBox.Text) ? "all" : rangeBox.Text.Trim(),
        };
    }
}

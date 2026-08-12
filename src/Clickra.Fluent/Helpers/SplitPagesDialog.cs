using Clickra.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;

namespace Clickra_Fluent;

/// <summary>
/// Shared PDF split dialog used by both Fluent entry points (dashboard and packaged
/// activation). Hosts the <see cref="VisualSplitterControl"/> so users preview pages
/// and build custom segments visually; the spec is produced by the control through
/// Core's PdfSplitProcessor.BuildSegmentSpec.
/// </summary>
public static class SplitPagesDialog
{
    /// <summary>
    /// Shows the visual splitter for <paramref name="pdfPath"/> and returns the
    /// page-range spec ("1-3; 5-7", "all", or "1-5; 6-10"), or null when cancelled.
    /// </summary>
    /// <param name="xamlRoot">XamlRoot of the hosting page, required by ContentDialog.</param>
    /// <param name="pdfPath">Source PDF being split.</param>
    public static async Task<string?> PromptAsync(XamlRoot xamlRoot, string pdfPath)
    {
        string L(string key) => Localization.T(key, ClickraStorage.GetSetting("Language"));

        var splitter = new VisualSplitterControl(pdfPath);
        var dialog = new ContentDialog
        {
            Title = L("pdf_split_title"),
            Content = splitter,
            PrimaryButtonText = L("fluent_ok"),
            CloseButtonText = L("dialog_cancel"),
            MaxWidth = 640,
            XamlRoot = xamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? splitter.GetSpec() : null;
    }
}

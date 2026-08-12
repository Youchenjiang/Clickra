using Clickra.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Clickra_Fluent;

/// <summary>
/// Full-window PDF splitter surface shown over a page while the user builds the
/// page-range spec visually. Mirrors the CLI's splitter-as-window-mode design:
/// the preview gets the whole window instead of being squeezed into a dialog,
/// so pages are readable without zooming. The spec is produced by the hosted
/// <see cref="VisualSplitterControl"/> through Core's PdfSplitProcessor.
/// </summary>
public sealed partial class SplitPagesOverlay : UserControl
{
    private TaskCompletionSource<string?>? _tcs;

    public SplitPagesOverlay()
    {
        InitializeComponent();
        string L(string key) => Localization.T(key, ClickraStorage.GetSetting("Language"));
        OverlayTitle.Text = L("pdf_split_title");
        ConfirmBtn.Content = L("fluent_ok");
        CancelBtn.Content = L("dialog_cancel");
    }

    /// <summary>
    /// Loads a fresh splitter for <paramref name="pdfPath"/> and returns a task that
    /// completes with the chosen page-range spec, or null when cancelled.
    /// </summary>
    public Task<string?> ShowForAsync(string pdfPath)
    {
        _tcs = new TaskCompletionSource<string?>();
        SplitterHost.Child = new VisualSplitterControl(pdfPath);
        OverlayFile.Text = Path.GetFileName(pdfPath);
        return _tcs.Task;
    }

    private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
        => _tcs?.TrySetResult((SplitterHost.Child as VisualSplitterControl)?.GetSpec());

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
        => _tcs?.TrySetResult(null);
}

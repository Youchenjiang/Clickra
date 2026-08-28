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
    /// Shows the overlay, loads a fresh splitter for <paramref name="pdfPath"/> and
    /// returns a task that completes with the chosen page-range spec, or null when
    /// cancelled. The overlay hides itself when the task completes.
    /// </summary>
    public async Task<string?> ShowForAsync(string pdfPath)
    {
        Visibility = Visibility.Visible;
        try
        {
            _tcs = new TaskCompletionSource<string?>();
            SplitterHost.Child = new VisualSplitterControl(pdfPath);
            OverlayFile.Text = Path.GetFileName(pdfPath);
            return await _tcs.Task;
        }
        finally
        {
            Visibility = Visibility.Collapsed;
        }
    }

    private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
        => _tcs?.TrySetResult((SplitterHost.Child as VisualSplitterControl)?.GetSpec());

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
        => Cancel();

    /// <summary>取消目前的分割規格（供「暫存」流程喚醒卡住的等待，讓背景執行緒乾淨結束）。</summary>
    internal void Cancel()
        => _tcs?.TrySetResult(null);
}

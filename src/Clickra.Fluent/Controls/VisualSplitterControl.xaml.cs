using Clickra.Core;
using Clickra.Core.Processors;
using Clickra.Core.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Clickra_Fluent;

/// <summary>
/// WinUI visual PDF splitter, mirroring the CLI's visual splitter: a segment list,
/// a live page preview with inline zoom (buttons / Ctrl+wheel), page navigation
/// and "split at current page", and custom / split-each / fixed-pages modes. The
/// page-range spec is built by <see cref="PdfSplitProcessor.BuildSegmentSpec"/> so
/// both tracks share one Core source of truth.
/// </summary>
public sealed partial class VisualSplitterControl : UserControl
{
    private const int PreviewWidth = 660;
    private const int ZoomWidth = 1500;
    private readonly string _pdfPath;
    private readonly int _totalPages;
    private readonly VisualSplitModel _splitModel;

    // Split mode: 0 = custom segments, 1 = split every page, 2 = fixed pages per segment.
    private int _mode;
    private int _nPages;
    private readonly List<(int Start, int End)> _segments = new();
    private readonly List<(int Start, int End)> _customSegments = new();
    private int _selectedSegmentIndex;
    private int _currentPreviewPageIndex;

    private int _renderSeq;
    private bool _suppressSelection;
    private PdfDocument? _pdfDoc;

    // Inline preview zoom (mirrors the CLI: factor 1.0 = fit, clamped to 8x).
    private float _zoomFactor = 1f;

    public VisualSplitterControl(string pdfPath)
    {
        InitializeComponent();

        _pdfPath = pdfPath;
        _totalPages = FileProcessor.GetPdfPageCount(pdfPath);
        if (_totalPages <= 0) _totalPages = 1;
        _splitModel = new VisualSplitModel(_totalPages);
        SyncModelState();

        string L(string key) => Localization.T(key, ClickraStorage.GetSetting(ClickraSettings.Language));
        ModeCustomBtn.Content = L("pdf_split_mode_custom");
        ModeEachBtn.Content = L("pdf_split_mode_each");
        AddSegmentBtn.Content = L("pdf_split_btn_add");
        DeleteSegmentBtn.Content = L("pdf_split_btn_delete");
        ClearSegmentsBtn.Content = L("pdf_split_btn_clear");
        SplitAtPageBtn.Content = L("pdf_split_btn_split_at");
        AutomationProperties.SetName(NMinusBtn, L("pdf_split_decrease_pages"));
        AutomationProperties.SetName(NPlusBtn, L("pdf_split_increase_pages"));
        AutomationProperties.SetName(PrevPageBtn, L("pdf_split_previous_page"));
        AutomationProperties.SetName(NextPageBtn, L("pdf_split_next_page"));
        AutomationProperties.SetName(ZoomOutBtn, L("pdf_split_zoom_out"));
        AutomationProperties.SetName(ZoomInBtn, L("pdf_split_zoom_in"));
        SegmentHeader.Text = L("pdf_split_segment_header");
        ModeCustomBtn.IsChecked = true;
        RefreshModeButtons();
        RefreshNSelector();
        ZoomLevelText.Text = "100%";
        ZoomFitBtn.Content = L("pdf_split_zoom_fit");

        ModeCustomBtn.Checked += (_, _) => ApplyMode(0);
        ModeEachBtn.Checked += (_, _) => ApplyMode(1);
        ModeFixedBtn.Checked += (_, _) => ApplyMode(2);
        NMinusBtn.Click += (_, _) => AdjustNPages(-1);
        NPlusBtn.Click += (_, _) => AdjustNPages(+1);

        SegmentList.SelectionChanged += SegmentList_SelectionChanged;
        AddSegmentBtn.Click += (_, _) => AddVisualSplitSegment();
        DeleteSegmentBtn.Click += (_, _) => DeleteVisualSplitSegment();
        ClearSegmentsBtn.Click += (_, _) => ClearVisualSplitSegments();
        PrevPageBtn.Click += (_, _) => NavigatePreview(-1);
        NextPageBtn.Click += (_, _) => NavigatePreview(+1);
        SplitAtPageBtn.Click += (_, _) => SplitSegmentAtCurrentPage();
        // Re-fit the preview (keeping the zoom factor) when the window or the
        // viewport changes (e.g. scrollbars appearing, window resizing).
        SizeChanged += (_, _) => ApplyPreviewSize();
        PreviewScroll.ViewChanged += (_, _) => ApplyPreviewSize();

        ApplyMode(0);
        _ = LoadPreviewDocumentAsync();
    }

    // ---- Inline preview zoom -------------------------------------------------

    /// <summary>Renders the page at a resolution that supports the current zoom
    /// factor (fit 1x = 660px, capped at 1500px for deep zoom).</summary>
    private int RenderWidth => VisualSplitModel.ZoomRenderWidth(_zoomFactor, PreviewWidth, PreviewWidth, ZoomWidth);

    /// <summary>Sizes the preview page so factor 1.0 fits the viewport, then scales by
    /// the zoom factor (the ScrollViewer then provides panning when zoomed in).</summary>
    private void ApplyPreviewSize()
    {
        if (PreviewImage.Source is not BitmapImage bmp) return;
        double vw = PreviewScroll.ViewportWidth;
        double vh = PreviewScroll.ViewportHeight;
        if (vw <= 0 || vh <= 0) return;

        double aspect = (double)bmp.PixelWidth / bmp.PixelHeight;
        double fitW = vw, fitH = vw / aspect;
        if (fitH > vh) { fitH = vh; fitW = vh * aspect; }
        // Keep 1px of slack so layout rounding never leaves a phantom scrollbar.
        fitW = Math.Max(1, fitW - 1);
        fitH = Math.Max(1, fitH - 1);

        PreviewImage.Width = fitW * _zoomFactor;
        PreviewImage.Height = fitH * _zoomFactor;
        ZoomLevelText.Text = $"{VisualSplitModel.ZoomPercent(_zoomFactor)}%";
    }

    /// <summary>Sets the zoom factor (clamped 1x-8x). Resizes immediately with the
    /// current bitmap, then re-renders at higher resolution for crispness.</summary>
    private void SetZoomFactor(float factor)
    {
        float newFactor = VisualSplitModel.ClampZoomFactor(factor);
        if (Math.Abs(newFactor - _zoomFactor) < 0.001f) return;
        _zoomFactor = newFactor;
        ApplyPreviewSize();
        _ = UpdatePreview();
    }

    private void ZoomInBtn_Click(object sender, RoutedEventArgs e) => SetZoomFactor(VisualSplitModel.ZoomFactorAfter(_zoomFactor, +1));
    private void ZoomOutBtn_Click(object sender, RoutedEventArgs e) => SetZoomFactor(VisualSplitModel.ZoomFactorAfter(_zoomFactor, -1));
    private void ZoomFitBtn_Click(object sender, RoutedEventArgs e) => SetZoomFactor(1f);

    /// <summary>Ctrl+wheel zooms the inline preview; a plain wheel keeps scrolling the
    /// preview when zoomed in (standard viewer behaviour).</summary>
    private void PreviewImage_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        bool isCtrl = ctrlState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (!isCtrl) return;
        int delta = e.GetCurrentPoint(PreviewImage).Properties.MouseWheelDelta;
        if (delta == 0) return;
        SetZoomFactor(VisualSplitModel.ZoomFactorAfter(_zoomFactor, delta > 0 ? +1 : -1));
        e.Handled = true;
    }

    /// <summary>Loads the Windows built-in PDF renderer for true page previews; falls
    /// back to the shared Core word-overlay renderer when the document cannot be opened.</summary>
    private async Task LoadPreviewDocumentAsync()
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(_pdfPath);
            _pdfDoc = await PdfDocument.LoadFromFileAsync(file);
        }
        catch
        {
            _pdfDoc = null;
        }
        _ = UpdatePreview();
    }

    /// <summary>Refreshes the three mode button labels, keeping the fixed-pages button
    /// in sync with the current N ("Fixed pages: 5 pages", mirroring the CLI mode bar).</summary>
    private void RefreshModeButtons()
    {
        string lang = ClickraStorage.GetSetting(ClickraSettings.Language);
        ModeFixedBtn.Content = string.Format(Localization.T("pdf_split_mode_fixed_n", lang),
            Localization.T("pdf_split_mode_fixed", lang), _nPages);
    }

    /// <summary>Refreshes the pages-per-segment stepper label ("Every 5 pages").</summary>
    private void RefreshNSelector()
    {
        NLabel.Text = $"{Localization.T("pdf_split_pages_per_segment", ClickraStorage.GetSetting(ClickraSettings.Language))} {_nPages}";
    }

    /// <summary>Adjusts N in fixed-pages mode (clamped to 1..total pages) and rebuilds the segments.</summary>
    private void AdjustNPages(int delta)
    {
        if (!_splitModel.AdjustPagesPerSegment(delta)) return;
        SyncModelState();
        RefreshModeButtons();
        RefreshNSelector();
        if (_mode == VisualSplitModel.ModeFixedPages)
        {
            RefreshSegmentList();
            _ = UpdatePreview();
        }
    }

    /// <summary>Builds the page-range spec for the active mode via
    /// <see cref="PdfSplitProcessor.BuildSegmentSpec"/> (always non-null; an empty
    /// custom list falls back to "all", matching the CLI splitter).</summary>
    public string GetSpec() => _splitModel.BuildSpec();

    private void ApplyMode(int mode)
    {
        _splitModel.SetMode(mode);
        SyncModelState();

        bool fixedMode = _mode == VisualSplitModel.ModeFixedPages;
        NSelector.Visibility = fixedMode ? Visibility.Visible : Visibility.Collapsed;
        if (fixedMode) RefreshNSelector();
        RefreshModeButtons();
        RefreshSegmentList();
        _ = UpdatePreview();
    }

    private void RefreshSegmentList()
    {
        _suppressSelection = true;
        SegmentList.Items.Clear();
        string lang = ClickraStorage.GetSetting(ClickraSettings.Language);
        for (int i = 0; i < _segments.Count; i++)
        {
            var seg = _segments[i];
            int pageCnt = seg.End - seg.Start + 1;
            string pageLabel = seg.Start == seg.End ? $"P.{seg.Start}" : $"P.{seg.Start}-{seg.End}";
            SegmentList.Items.Add(string.Format(Localization.T("pdf_split_segment_item", lang), i + 1, pageLabel, pageCnt));
        }
        SegmentList.SelectedIndex = _selectedSegmentIndex;
        _suppressSelection = false;
    }

    private void SegmentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || SegmentList.SelectedIndex < 0) return;
        _splitModel.SelectSegment(SegmentList.SelectedIndex);
        SyncModelState();
        _ = UpdatePreview();
    }

    private int GetCurrentPageNumber()
    {
        if (_selectedSegmentIndex >= 0 && _selectedSegmentIndex < _segments.Count)
        {
            return _segments[_selectedSegmentIndex].Start + _currentPreviewPageIndex;
        }
        return 1;
    }

    private void NavigatePreview(int delta)
    {
        _splitModel.NavigatePreview(delta);
        SyncModelState();
        _ = UpdatePreview();
    }

    /// <summary>Splits the selected segment at the currently previewed page into two
    /// adjacent segments, switching to custom mode (mirrors the CLI split action).</summary>
    private void SplitSegmentAtCurrentPage()
    {
        if (!_splitModel.SplitSelectedAtPreviewPage()) return;
        SyncModelState();
        SelectCustomMode(ModeCustomBtn);

        RefreshSegmentList();
        _ = UpdatePreview();
    }

    /// <summary>Switches the mode toggle to custom segments. The null-safe
    /// <c>is not true</c> treats the toggle's uninitialized state as unchecked.</summary>
    private static void SelectCustomMode(ToggleButton modeCustomBtn)
    {
        if (modeCustomBtn.IsChecked is not true) modeCustomBtn.IsChecked = true; // NOSONAR:S1125 — literal required for nullable IsChecked.
    }

    /// <summary>Adds the first page gap not covered by any custom segment as a new
    /// segment and selects it (switching to custom mode).</summary>
    private void AddVisualSplitSegment()
    {
        _splitModel.AddSegment();
        SyncModelState();
        SelectCustomMode(ModeCustomBtn);
        RefreshSegmentList();
        _ = UpdatePreview();
    }

    /// <summary>Removes the selected custom segment (keeping at least one).</summary>
    private void DeleteVisualSplitSegment()
    {
        if (!_splitModel.DeleteSelectedSegment()) return;
        SyncModelState();
        SelectCustomMode(ModeCustomBtn);
        RefreshSegmentList();
        _ = UpdatePreview();
    }

    /// <summary>Clears all custom segments and switches to custom mode.</summary>
    private void ClearVisualSplitSegments()
    {
        _splitModel.ClearSegments();
        SyncModelState();
        SelectCustomMode(ModeCustomBtn);
        RefreshSegmentList();
        _ = UpdatePreview();
    }

    private void SyncModelState()
    {
        _mode = _splitModel.Mode;
        _nPages = _splitModel.PagesPerSegment;
        _segments.Clear();
        _segments.AddRange(_splitModel.Segments);
        _customSegments.Clear();
        _customSegments.AddRange(_splitModel.CustomSegments);
        _selectedSegmentIndex = _splitModel.SelectedSegmentIndex;
        _currentPreviewPageIndex = _splitModel.PreviewPageIndex;
    }

    /// <summary>Renders the current preview page at fit width and swaps it into the
    /// preview image, discarding stale renders via a sequence guard. Uses the Windows
    /// built-in PDF renderer for true page quality.</summary>
    private async Task UpdatePreview()
    {
        int page = GetCurrentPageNumber();

        // "P.5 (Page 2/3)": absolute page inside the segment-relative position.
        int pageCnt = 1;
        if (_selectedSegmentIndex >= 0 && _selectedSegmentIndex < _segments.Count)
        {
            var seg = _segments[_selectedSegmentIndex];
            pageCnt = seg.End - seg.Start + 1;
        }
        string lang = ClickraStorage.GetSetting(ClickraSettings.Language);
        PageLabel.Text = string.Format(Localization.T("pdf_split_page_preview_format", lang),
            page, Math.Min(_currentPreviewPageIndex + 1, pageCnt), pageCnt);

        // Output badge: [PDF] filename (N pages) of the selected segment.
        string outName = Path.GetFileNameWithoutExtension(_pdfPath);
        OutputBadgeText.Text = string.Format(Localization.T("pdf_split_badge_format", lang), outName, pageCnt);

        int seq = ++_renderSeq;
        BitmapImage? source;
        if (_pdfDoc != null)
        {
            source = await RenderPageAsync(_pdfDoc, page, RenderWidth);
        }
        else
        {
            // Windows PDF renderer unavailable (e.g. encrypted file): fall back to the
            // shared Core word-overlay renderer.
            string fontName = PdfPageThumbnailRenderer.GetTextFontName(ClickraStorage.GetSetting(ClickraSettings.Language));
            var bmp = await Task.Run(() => PdfPageThumbnailRenderer.RenderPageFromFile(_pdfPath, page, RenderWidth, fontName));
            source = bmp == null ? null : await ToBitmapImageAsync(bmp);
            bmp?.Dispose();
        }

        if (seq != _renderSeq || source == null) return;
        PreviewImage.Source = source;
        ApplyPreviewSize();
    }

    private static async Task<BitmapImage?> RenderPageAsync(PdfDocument doc, int pageNumber, int targetWidth)
    {
        try
        {
            using var page = doc.GetPage((uint)(pageNumber - 1));
            var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = (uint)targetWidth });
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<BitmapImage?> ToBitmapImageAsync(System.Drawing.Bitmap bmp)
    {
        try
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var image = new BitmapImage();
            await image.SetSourceAsync(ms.AsRandomAccessStream());
            return image;
        }
        catch
        {
            return null;
        }
    }
}

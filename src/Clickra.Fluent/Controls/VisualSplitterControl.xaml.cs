using Clickra.Core;
using Clickra.Core.Processors;
using Clickra.Core.Rendering;
using Microsoft.UI.Xaml;
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
using Windows.System;

namespace Clickra_Fluent;

/// <summary>
/// WinUI visual PDF splitter, mirroring the CLI's visual splitter: a segment list,
/// a live page preview with page navigation and "split at current page", custom /
/// split-each / fixed-pages modes, and a zoomed inspection view. The page-range spec
/// is built by <see cref="PdfSplitProcessor.BuildSegmentSpec"/> so both tracks share
/// one Core source of truth.
/// </summary>
public sealed partial class VisualSplitterControl : UserControl
{
    private const int PreviewWidth = 660;
    private const int ZoomWidth = 1500;

    private readonly string _pdfPath;
    private readonly int _totalPages;

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

    // Zoom lightbox state (mirrors the CLI: factor 1.0 = fit, clamped to 8x).
    private bool _zoomOpen;
    private float _zoomFactor = 1f;
    private bool _zoomDragging;
    private double _zoomDragStartX, _zoomDragStartY;
    private double _zoomDragOffsetX, _zoomDragOffsetY;
    private int _zoomRenderSeq;

    public VisualSplitterControl(string pdfPath)
    {
        InitializeComponent();

        _pdfPath = pdfPath;
        _totalPages = FileProcessor.GetPdfPageCount(pdfPath);
        if (_totalPages <= 0) _totalPages = 1;
        _nPages = Math.Min(5, _totalPages);

        string L(string key) => Localization.T(key, ClickraStorage.GetSetting("Language"));
        ModeCustomBtn.Content = L("pdf_split_mode_custom");
        ModeEachBtn.Content = L("pdf_split_mode_each");
        ModeCustomBtn.IsChecked = true;
        RefreshModeButtons();
        RefreshNSelector();
        ZoomTagText.Text = L("pdf_split_zoom_tag");
        ZoomHint.Text = L("pdf_split_zoom_hint");
        ZoomCloseBtn.Content = L("pdf_split_zoom_close");
        ZoomFitBtn.Content = L("pdf_split_zoom_fit");

        // Seed custom segments with halves of the document, mirroring the CLI splitter.
        if (_totalPages == 1)
        {
            _customSegments.Add((1, 1));
        }
        else
        {
            int half = _totalPages / 2;
            _customSegments.Add((1, half));
            _customSegments.Add((half + 1, _totalPages));
        }

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
        PreviewBox.PointerPressed += PreviewBox_PointerPressed;
        ZoomTag.PointerPressed += ZoomTag_PointerPressed;

        ApplyMode(0);
        _ = LoadPreviewDocumentAsync();
    }

    // ---- Zoom lightbox ----------------------------------------------------

    private void PreviewBox_PointerPressed(object sender, PointerRoutedEventArgs e) => OpenZoom();
    private void ZoomTag_PointerPressed(object sender, PointerRoutedEventArgs e) => OpenZoom();

    private void OpenZoom()
    {
        if (_zoomOpen) return;
        _zoomOpen = true;
        _zoomFactor = 1f;
        _zoomDragging = false;
        ZoomOverlay.Visibility = Visibility.Visible;
        ZoomOverlay.Focus(FocusState.Programmatic);
        UpdateZoom();
    }

    private void CloseZoom()
    {
        if (!_zoomOpen) return;
        _zoomOpen = false;
        _zoomDragging = false;
        ZoomOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>Renders the current page at high resolution for the lightbox and refreshes
    /// the title and size, honoring the current zoom factor.</summary>
    private async void UpdateZoom()
    {
        int page = GetCurrentPageNumber();
        int seq = ++_zoomRenderSeq;
        BitmapImage? source;
        if (_pdfDoc != null)
        {
            source = await RenderPageAsync(_pdfDoc, page, ZoomWidth);
        }
        else
        {
            string fontName = PdfPageThumbnailRenderer.GetTextFontName(ClickraStorage.GetSetting("Language"));
            var bmp = await Task.Run(() => PdfPageThumbnailRenderer.RenderPageFromFile(_pdfPath, page, ZoomWidth, fontName));
            source = bmp == null ? null : await ToBitmapImageAsync(bmp);
            bmp?.Dispose();
        }

        if (!_zoomOpen || seq != _zoomRenderSeq || source == null) return;
        ZoomImage.Source = source;
        ApplyZoomSize();
    }

    /// <summary>Sizes the zoomed page so factor 1.0 fits the lightbox viewport, then
    /// scales by the zoom factor (the ScrollViewer then provides panning).</summary>
    private void ApplyZoomSize()
    {
        if (ZoomImage.Source is not BitmapImage bmp) return;
        double vw = ZoomScroll.ViewportWidth;
        double vh = ZoomScroll.ViewportHeight;
        if (vw <= 0 || vh <= 0) return;

        double aspect = (double)bmp.PixelWidth / bmp.PixelHeight;
        double fitW = vw, fitH = vw / aspect;
        if (fitH > vh) { fitH = vh; fitW = vh * aspect; }

        ZoomImage.Width = fitW * _zoomFactor;
        ZoomImage.Height = fitH * _zoomFactor;

        int pct = (int)(_zoomFactor * 100);
        ZoomTitle.Text = $"{Localization.T("pdf_split_zoom_title", ClickraStorage.GetSetting("Language"))} P.{GetCurrentPageNumber()} \u00B7 {pct}%";
    }

    /// <summary>Sets the zoom factor (clamped 1x-8x), keeping the lightbox viewport centered.</summary>
    private void SetZoomFactor(float factor)
    {
        float newFactor = Math.Clamp(factor, 1f, 8f);
        if (newFactor == _zoomFactor) return;
        _zoomFactor = newFactor;
        ApplyZoomSize();
    }

    private void ZoomInBtn_Click(object sender, RoutedEventArgs e) => SetZoomFactor(_zoomFactor * 1.25f);
    private void ZoomOutBtn_Click(object sender, RoutedEventArgs e) => SetZoomFactor(_zoomFactor / 1.25f);
    private void ZoomFitBtn_Click(object sender, RoutedEventArgs e) => SetZoomFactor(1f);
    private void ZoomCloseBtn_Click(object sender, RoutedEventArgs e) => CloseZoom();

    /// <summary>Wheel zooms the lightbox anchored at the cursor (mirrors the CLI).</summary>
    private void ZoomOverlay_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!_zoomOpen) return;
        int delta = e.GetCurrentPoint(ZoomScroll).Properties.MouseWheelDelta;
        if (delta == 0) return;

        var pt = e.GetCurrentPoint(ZoomScroll).Position;
        double oldW = ZoomImage.Width;
        float factor = delta > 0 ? 1.25f : 0.8f;
        float newFactor = Math.Clamp(_zoomFactor * factor, 1f, 8f);
        if (newFactor == _zoomFactor) return;

        double ratio = newFactor / _zoomFactor;
        _zoomFactor = newFactor;
        double anchorX = ZoomScroll.HorizontalOffset + pt.X;
        double anchorY = ZoomScroll.VerticalOffset + pt.Y;
        ApplyZoomSize();
        ZoomScroll.ChangeView(anchorX * ratio - pt.X, anchorY * ratio - pt.Y, null, true);
        e.Handled = true;
    }

    /// <summary>Drag-to-pan the zoomed page; a press outside the modal closes the lightbox.</summary>
    private void ZoomOverlay_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_zoomOpen) return;
        var pt = e.GetCurrentPoint(ZoomOverlay).Position;
        var origin = ZoomModal.TransformToVisual(ZoomOverlay).TransformPoint(new Windows.Foundation.Point(0, 0));
        var rect = new Windows.Foundation.Rect(origin.X, origin.Y, ZoomModal.ActualWidth, ZoomModal.ActualHeight);
        if (!rect.Contains(pt)) CloseZoom();
    }

    private void ZoomImage_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_zoomOpen) return;
        _zoomDragging = true;
        var pt = e.GetCurrentPoint(ZoomScroll);
        _zoomDragStartX = pt.Position.X;
        _zoomDragStartY = pt.Position.Y;
        _zoomDragOffsetX = ZoomScroll.HorizontalOffset;
        _zoomDragOffsetY = ZoomScroll.VerticalOffset;
        ZoomImage.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ZoomImage_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_zoomDragging) return;
        var pt = e.GetCurrentPoint(ZoomScroll);
        double dx = pt.Position.X - _zoomDragStartX;
        double dy = pt.Position.Y - _zoomDragStartY;
        ZoomScroll.ChangeView(_zoomDragOffsetX - dx, _zoomDragOffsetY - dy, null, true);
    }

    private void ZoomImage_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_zoomDragging) return;
        _zoomDragging = false;
        ZoomImage.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    /// <summary>Lightbox keyboard shortcuts: Esc/Space/Enter close, +/− zoom, 0 resets (mirrors the CLI).</summary>
    private void ZoomOverlay_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_zoomOpen) return;
        switch (e.Key)
        {
            case VirtualKey.Escape:
            case VirtualKey.Space:
            case VirtualKey.Enter:
                CloseZoom();
                e.Handled = true;
                break;
            case VirtualKey.Add:
            case VirtualKey.Number1:
                SetZoomFactor(_zoomFactor * 1.25f);
                e.Handled = true;
                break;
            case VirtualKey.Subtract:
                SetZoomFactor(_zoomFactor / 1.25f);
                e.Handled = true;
                break;
            case VirtualKey.Number0:
                SetZoomFactor(1f);
                e.Handled = true;
                break;
        }
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
        UpdatePreview();
    }

    /// <summary>Refreshes the three mode button labels, keeping the fixed-pages button
    /// in sync with the current N ("固定頁數: 5頁", mirroring the CLI mode bar).</summary>
    private void RefreshModeButtons()
    {
        ModeFixedBtn.Content = $"{Localization.T("pdf_split_mode_fixed", ClickraStorage.GetSetting("Language"))}: {_nPages}頁";
    }

    /// <summary>Refreshes the pages-per-segment stepper label ("每 5 頁").</summary>
    private void RefreshNSelector()
    {
        NLabel.Text = $"{Localization.T("pdf_split_pages_per_segment", ClickraStorage.GetSetting("Language"))} {_nPages}";
    }

    /// <summary>Adjusts N in fixed-pages mode (clamped to 1..total pages) and rebuilds the segments.</summary>
    private void AdjustNPages(int delta)
    {
        int n = Math.Clamp(_nPages + delta, 1, _totalPages);
        if (n == _nPages) return;
        _nPages = n;
        RefreshModeButtons();
        RefreshNSelector();
        if (_mode == 2) ApplyMode(2);
    }

    /// <summary>Builds the page-range spec for the active mode via
    /// <see cref="PdfSplitProcessor.BuildSegmentSpec"/> (always non-null; an empty
    /// custom list falls back to "all", matching the CLI splitter).</summary>
    public string GetSpec() =>
        PdfSplitProcessor.BuildSegmentSpec(_mode, _nPages, _totalPages, _segments);

    private void ApplyMode(int mode)
    {
        _mode = mode;
        _currentPreviewPageIndex = 0;

        bool fixedMode = mode == 2;
        NSelector.Visibility = fixedMode ? Visibility.Visible : Visibility.Collapsed;
        if (fixedMode) RefreshNSelector();
        RefreshModeButtons();

        switch (mode)
        {
            case 1: // split every page
                _segments.Clear();
                _customSegments.Clear();
                for (int p = 1; p <= _totalPages; p++)
                {
                    _segments.Add((p, p));
                    _customSegments.Add((p, p));
                }
                break;
            case 2: // fixed pages per segment
                _segments.Clear();
                _customSegments.Clear();
                int n = Math.Max(1, _nPages);
                for (int p = 1; p <= _totalPages; p += n)
                {
                    int end = Math.Min(p + n - 1, _totalPages);
                    _segments.Add((p, end));
                    _customSegments.Add((p, end));
                }
                break;
            default: // custom segments
                _segments.Clear();
                _segments.AddRange(_customSegments);
                if (_segments.Count == 0 && _totalPages > 0)
                {
                    _customSegments.Add((1, _totalPages));
                    _segments.Add((1, _totalPages));
                }
                break;
        }

        _selectedSegmentIndex = _segments.Count > 0 ? 0 : -1;
        RefreshSegmentList();
        UpdatePreview();
    }

    private void RefreshSegmentList()
    {
        _suppressSelection = true;
        SegmentList.Items.Clear();
        for (int i = 0; i < _segments.Count; i++)
        {
            var seg = _segments[i];
            int pageCnt = seg.End - seg.Start + 1;
            string pageLabel = seg.Start == seg.End ? $"P.{seg.Start}" : $"P.{seg.Start}-{seg.End}";
            SegmentList.Items.Add($"區段 {i + 1}: {pageLabel} ({pageCnt}頁)");
        }
        SegmentList.SelectedIndex = _selectedSegmentIndex;
        _suppressSelection = false;
    }

    private void SegmentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || SegmentList.SelectedIndex < 0) return;
        _selectedSegmentIndex = SegmentList.SelectedIndex;
        _currentPreviewPageIndex = 0;
        UpdatePreview();
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
        if (_selectedSegmentIndex < 0 || _selectedSegmentIndex >= _segments.Count) return;
        var seg = _segments[_selectedSegmentIndex];
        int pageCnt = seg.End - seg.Start + 1;
        _currentPreviewPageIndex = Math.Clamp(_currentPreviewPageIndex + delta, 0, pageCnt - 1);
        UpdatePreview();
    }

    /// <summary>Splits the selected segment at the currently previewed page into two
    /// adjacent segments, switching to custom mode (mirrors the CLI "切開" action).</summary>
    private void SplitSegmentAtCurrentPage()
    {
        if (_selectedSegmentIndex < 0 || _selectedSegmentIndex >= _customSegments.Count) return;

        var seg = _customSegments[_selectedSegmentIndex];
        int pageCnt = seg.End - seg.Start + 1;
        if (pageCnt <= 1) return;

        int previewIdx = Math.Clamp(_currentPreviewPageIndex, 0, pageCnt - 1);
        int splitPage = seg.Start + previewIdx;

        var first = (seg.Start, splitPage);
        var second = (splitPage + 1, seg.End);

        _customSegments.RemoveAt(_selectedSegmentIndex);
        _customSegments.Insert(_selectedSegmentIndex, second);
        _customSegments.Insert(_selectedSegmentIndex, first);
        _segments.Clear();
        _segments.AddRange(_customSegments);
        _currentPreviewPageIndex = 0;

        _mode = 0;
        if (!(ModeCustomBtn.IsChecked == true)) ModeCustomBtn.IsChecked = true;

        RefreshSegmentList();
        UpdatePreview();
    }

    /// <summary>Adds the first page gap not covered by any custom segment as a new
    /// segment and selects it (switching to custom mode).</summary>
    private void AddVisualSplitSegment()
    {
        if (!(ModeCustomBtn.IsChecked == true)) ModeCustomBtn.IsChecked = true;
        _mode = 0;

        if (_customSegments.Count == 0)
        {
            _customSegments.Add((1, _totalPages));
            _segments.Clear();
            _segments.AddRange(_customSegments);
            _selectedSegmentIndex = 0;
            _currentPreviewPageIndex = 0;
            RefreshSegmentList();
            UpdatePreview();
            return;
        }

        var covered = new HashSet<int>();
        foreach (var s in _customSegments)
            for (int p = s.Start; p <= s.End; p++)
                covered.Add(p);

        int gapStart = -1, gapEnd = -1;
        for (int p = 1; p <= _totalPages; p++)
        {
            if (!covered.Contains(p))
            {
                if (gapStart < 0) gapStart = p;
                gapEnd = p;
            }
            else if (gapStart > 0)
            {
                break;
            }
        }

        if (gapStart < 0) return;

        _customSegments.Add((gapStart, gapEnd));
        _customSegments.Sort((a, b) => a.Start.CompareTo(b.Start));
        _segments.Clear();
        _segments.AddRange(_customSegments);
        _selectedSegmentIndex = _segments.FindIndex(s => s.Start == gapStart && s.End == gapEnd);
        _currentPreviewPageIndex = 0;
        RefreshSegmentList();
        UpdatePreview();
    }

    /// <summary>Removes the selected custom segment (keeping at least one).</summary>
    private void DeleteVisualSplitSegment()
    {
        if (_customSegments.Count <= 1) return;
        if (_selectedSegmentIndex < 0 || _selectedSegmentIndex >= _customSegments.Count) return;

        if (!(ModeCustomBtn.IsChecked == true)) ModeCustomBtn.IsChecked = true;
        _mode = 0;

        _customSegments.RemoveAt(_selectedSegmentIndex);
        _segments.Clear();
        _segments.AddRange(_customSegments);
        if (_selectedSegmentIndex >= _segments.Count)
            _selectedSegmentIndex = _segments.Count - 1;
        _currentPreviewPageIndex = 0;
        RefreshSegmentList();
        UpdatePreview();
    }

    /// <summary>Clears all custom segments and switches to custom mode.</summary>
    private void ClearVisualSplitSegments()
    {
        if (!(ModeCustomBtn.IsChecked == true)) ModeCustomBtn.IsChecked = true;
        _mode = 0;
        _customSegments.Clear();
        _segments.Clear();
        _selectedSegmentIndex = -1;
        _currentPreviewPageIndex = 0;
        RefreshSegmentList();
        UpdatePreview();
    }

    /// <summary>Renders the current preview page at fit width and swaps it into the
    /// preview image, discarding stale renders via a sequence guard. Uses the Windows
    /// built-in PDF renderer for true page quality.</summary>
    private async void UpdatePreview()
    {
        int page = GetCurrentPageNumber();

        // "P.5 (第 2/3 頁)": absolute page inside the segment-relative position.
        int pageCnt = 1;
        if (_selectedSegmentIndex >= 0 && _selectedSegmentIndex < _segments.Count)
        {
            var seg = _segments[_selectedSegmentIndex];
            pageCnt = seg.End - seg.Start + 1;
        }
        PageLabel.Text = $"P.{page} (第 {Math.Min(_currentPreviewPageIndex + 1, pageCnt)}/{pageCnt} 頁)";

        // Output badge: [PDF] filename (N pages) of the selected segment.
        string outName = Path.GetFileNameWithoutExtension(_pdfPath);
        OutputBadgeText.Text = $"[PDF] {outName} ({pageCnt}頁)";

        int seq = ++_renderSeq;
        BitmapImage? source;
        if (_pdfDoc != null)
        {
            source = await RenderPageAsync(_pdfDoc, page, PreviewWidth);
        }
        else
        {
            // Windows PDF renderer unavailable (e.g. encrypted file): fall back to the
            // shared Core word-overlay renderer.
            string fontName = PdfPageThumbnailRenderer.GetTextFontName(ClickraStorage.GetSetting("Language"));
            var bmp = await Task.Run(() => PdfPageThumbnailRenderer.RenderPageFromFile(_pdfPath, page, PreviewWidth, fontName));
            source = bmp == null ? null : await ToBitmapImageAsync(bmp);
            bmp?.Dispose();
        }

        if (seq != _renderSeq || source == null) return;
        FitImage.Source = source;
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

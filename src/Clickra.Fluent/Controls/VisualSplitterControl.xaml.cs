using Clickra.Core;
using Clickra.Core.Processors;
using Clickra.Core.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

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

    public VisualSplitterControl(string pdfPath)
    {
        InitializeComponent();

        _pdfPath = pdfPath;
        _totalPages = FileProcessor.GetPdfPageCount(pdfPath);
        if (_totalPages <= 0) _totalPages = 1;
        _nPages = Math.Min(5, _totalPages);

        string L(string key) => Localization.T(key, ClickraStorage.GetSetting("Language"));
        ModeCombo.Items.Add(L("pdf_split_mode_custom"));
        ModeCombo.Items.Add(L("pdf_split_mode_each"));
        ModeCombo.Items.Add(L("pdf_split_mode_fixed"));
        ModeCombo.SelectedIndex = 0;
        NLabel.Text = L("pdf_split_pages_per_segment");
        NBox.Minimum = 1;
        NBox.Maximum = _totalPages;
        NBox.Value = _nPages;

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

        ModeCombo.SelectionChanged += (_, _) => ApplyMode(ModeCombo.SelectedIndex);
        NBox.ValueChanged += (_, _) =>
        {
            if (NBox.Value >= 1)
            {
                _nPages = (int)Math.Round(NBox.Value);
                ApplyMode(_mode);
            }
        };

        SegmentList.SelectionChanged += SegmentList_SelectionChanged;
        AddSegmentBtn.Click += (_, _) => AddVisualSplitSegment();
        DeleteSegmentBtn.Click += (_, _) => DeleteVisualSplitSegment();
        ClearSegmentsBtn.Click += (_, _) => ClearVisualSplitSegments();
        PrevPageBtn.Click += (_, _) => NavigatePreview(-1);
        NextPageBtn.Click += (_, _) => NavigatePreview(+1);
        SplitAtPageBtn.Click += (_, _) => SplitSegmentAtCurrentPage();
        ZoomToggle.Checked += (_, _) => UpdatePreview();
        ZoomToggle.Unchecked += (_, _) => UpdatePreview();

        ApplyMode(0);
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
        NLabel.Visibility = fixedMode ? Visibility.Visible : Visibility.Collapsed;
        NBox.Visibility = fixedMode ? Visibility.Visible : Visibility.Collapsed;

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
            SegmentList.Items.Add($"{pageLabel} ({pageCnt}頁)");
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
        if (ModeCombo.SelectedIndex != 0) ModeCombo.SelectedIndex = 0;

        RefreshSegmentList();
        UpdatePreview();
    }

    /// <summary>Adds the first page gap not covered by any custom segment as a new
    /// segment and selects it (switching to custom mode).</summary>
    private void AddVisualSplitSegment()
    {
        if (ModeCombo.SelectedIndex != 0) ModeCombo.SelectedIndex = 0;
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

        if (ModeCombo.SelectedIndex != 0) ModeCombo.SelectedIndex = 0;
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
        if (ModeCombo.SelectedIndex != 0) ModeCombo.SelectedIndex = 0;
        _mode = 0;
        _customSegments.Clear();
        _segments.Clear();
        _selectedSegmentIndex = -1;
        _currentPreviewPageIndex = 0;
        RefreshSegmentList();
        UpdatePreview();
    }

    /// <summary>Renders the current preview page (fit or zoomed) off-thread and swaps it
    /// into the preview image, discarding stale renders via a sequence guard.</summary>
    private async void UpdatePreview()
    {
        int page = GetCurrentPageNumber();
        PageLabel.Text = $"P.{page} / {_totalPages}";

        bool zoomed = ZoomToggle.IsChecked == true;
        int targetW = zoomed ? ZoomWidth : PreviewWidth;
        PreviewScroll.HorizontalScrollBarVisibility = zoomed ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        PreviewScroll.VerticalScrollBarVisibility = zoomed ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        PreviewImage.Width = zoomed ? ZoomWidth : PreviewWidth;
        PreviewImage.Height = double.NaN;

        string fontName = PdfPageThumbnailRenderer.GetTextFontName(ClickraStorage.GetSetting("Language"));
        int seq = ++_renderSeq;

        var bmp = await Task.Run(() => PdfPageThumbnailRenderer.RenderPageFromFile(_pdfPath, page, targetW, fontName));
        if (bmp == null)
        {
            if (seq == _renderSeq) PreviewImage.Source = null;
            return;
        }

        if (seq != _renderSeq)
        {
            bmp.Dispose();
            return;
        }

        var source = await ToBitmapImageAsync(bmp);
        bmp.Dispose();
        if (seq != _renderSeq || source == null) return;
        PreviewImage.Source = source;
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

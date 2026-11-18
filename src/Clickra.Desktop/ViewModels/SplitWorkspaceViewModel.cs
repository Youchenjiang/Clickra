using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Clickra.Core.Modules.Pdf.Split.Application;

namespace Clickra.Desktop.ViewModels;

/// <summary>One page tile: thumbnail, number, which part it belongs to, and the gap after it.</summary>
public sealed class SplitPageViewModel(SplitWorkspaceViewModel owner, int number) : ViewModelBase
{
    private IImage? _thumbnail;
    private int _part;
    private bool _cutAfter;
    private double _aspect = 297.0 / 210.0; // height ÷ width; A4 portrait until the page is drawn

    /// <summary>Every tile is 108 high and as wide as the page's shape (spec 4.6): A4 portrait about 76, a 16:9 slide
    /// 192, so a landscape slide is not shrunk into a portrait box. Rows stay level because the height is fixed.</summary>
    public const double TileHeight = 108;
    public double TileWidth => Math.Clamp(Math.Round(TileHeight / _aspect), 60, 192);

    public int Number { get; } = number;
    public IImage? Thumbnail
    {
        get => _thumbnail;
        set
        {
            _thumbnail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasThumbnail));
            if (value is { Size: { Width: > 0, Height: > 0 } size })
            {
                Aspect = size.Height / size.Width;
                owner.AdoptAspect(this); // pages not drawn yet take this shape, so the row does not jump page by page
            }
        }
    }

    internal double Aspect
    {
        get => _aspect;
        set { if (Math.Abs(_aspect - value) < 0.001) return; _aspect = value; OnPropertyChanged(nameof(TileWidth)); }
    }
    public bool HasThumbnail => _thumbnail is not null;
    /// <summary>1-based part number; bands alternate two colours so neighbouring parts are told apart.</summary>
    public int Part { get => _part; set { _part = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsOddPart)); OnPropertyChanged(nameof(PartText)); } }
    public bool IsOddPart => _part % 2 == 1;
    public string PartText => $"第 {_part} 份";
    public bool CutAfter { get => _cutAfter; set { _cutAfter = value; OnPropertyChanged(); OnPropertyChanged(nameof(GapTip)); } }
    public bool HasGap => Number < owner.TotalPages;
    /// <summary>Every gap can be clicked on a single file; in 每一頁／每 N 頁 that switches to custom cuts first.</summary>
    public bool CanEditGap => owner.IsSingleFile;
    public string GapTip => CutAfter ? $"取消第 {Number} 頁後面的切點" : $"在第 {Number} 頁後面切開";

    public void ToggleCut() => owner.ToggleCut(this);
    public void OpenPreview() => owner.OpenPreview(this);
    internal void RefreshGap() => OnPropertyChanged(nameof(CanEditGap));
}

/// <summary>
/// PDF 分割的工作區 (spec 4.6): three ways to split, page thumbnails with clickable gaps for custom cuts,
/// a colour band showing which part each page goes to, and the number of parts on the start button.
/// Custom cuts work on one file; with several PDFs only 「每一頁一份」 and 「每 N 頁一份」 apply.
/// </summary>
public sealed class SplitWorkspaceViewModel : ViewModelBase
{
    private int _mode = 1;
    private int _pagesPerPart = 2;
    private int _totalPages;
    private int _fileCount;
    private string _message = "";
    private CancellationTokenSource? _thumbnailsCts;
    private readonly SortedSet<int> _cuts = []; // cut after these page numbers

    public ObservableCollection<SplitPageViewModel> Pages { get; } = [];

    public int TotalPages => _totalPages;
    public bool IsSingleFile => _fileCount == 1;

    /// <summary>0 = 自訂切點, 1 = 每一頁一份, 2 = 每 N 頁一份.</summary>
    public int Mode
    {
        get => _mode;
        set
        {
            if (value is < 0 or > 2 || value == _mode) return;
            if (value == 0 && !IsSingleFile) return; // custom cuts need one file
            _mode = value;
            ConvertedToCustom = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustom));
            OnPropertyChanged(nameof(IsFixed));
            OnPropertyChanged(nameof(Hint));
            foreach (SplitPageViewModel page in Pages) page.RefreshGap();
            Recompute();
        }
    }
    public bool IsCustom => _mode == 0;
    public bool IsFixed => _mode == 2;

    public int PagesPerPart
    {
        get => _pagesPerPart;
        set
        {
            int clamped = Math.Clamp(value, 1, Math.Max(1, _totalPages));
            if (clamped == _pagesPerPart) return;
            _pagesPerPart = clamped;
            OnPropertyChanged();
            Recompute();
        }
    }

    public string Hint => _mode switch
    {
        0 => "點兩頁之間的縫隙加上或取消切點；點兩下頁面放大",
        1 => IsSingleFile ? "每一頁存成一個 PDF；點縫隙可以增減切點，點兩下頁面放大" : $"{_fileCount} 個檔案的每一頁各存成一個 PDF",
        _ => IsSingleFile ? "點縫隙可以增減切點，點兩下頁面放大" : $"{_fileCount} 個檔案都照這個頁數分割",
    };

    /// <summary>Shown instead of the thumbnails (several files, or the PDF cannot be read).</summary>
    public string Message { get => _message; private set { _message = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => _message.Length > 0;

    public int PartCount { get; private set; }

    /// <summary>Custom mode with no cut yet would give one part, the same file again.</summary>
    public bool IsReady => !(IsCustom && _cuts.Count == 0);
    public string BlockingText => IsCustom && _cuts.Count == 0 ? "先在頁面之間加上切點" : "";
    public string StartText => IsSingleFile && PartCount > 0 ? $"開始分割成 {PartCount} 份" : "開始分割";

    public void SetFiles(IReadOnlyList<string> files)
    {
        _thumbnailsCts?.Cancel();
        ClosePreview();
        ConvertedToCustom = false;
        _file = files.Count == 1 ? files[0] : "";
        _fileCount = files.Count;
        _cuts.Clear();
        Pages.Clear();
        _mode = IsSingleFile ? 0 : 1;
        _pagesPerPart = 2;
        _totalPages = 0;
        Message = "";

        if (IsSingleFile)
        {
            try { _totalPages = PdfPageCountQuery.Get(files[0]); }
            catch { _totalPages = 0; }
            if (_totalPages <= 0)
            {
                Message = PdfPageCountQuery.IsPasswordProtected(files[0])
                    ? "這個 PDF 有密碼，無法預覽頁面；仍可用「每一頁一份」或「每 N 頁一份」"
                    : "讀不到這個 PDF 的頁面";
                _mode = 1;
            }
            for (int i = 1; i <= _totalPages; i++) Pages.Add(new SplitPageViewModel(this, i));
            if (_totalPages > 0) LoadThumbnails(files[0]);
        }
        else
        {
            Message = $"放入了 {files.Count} 個 PDF，每個檔案各自分割；自訂切點一次只能用在一個檔案";
        }

        OnPropertyChanged(string.Empty);
        Recompute();
    }

    /// <summary>
    /// Adds or removes the cut after a page. In 每一頁一份／每 N 頁一份 the current cuts are kept as custom cuts
    /// first, so one exception (e.g. "these two pages stay together") does not mean starting over.
    /// </summary>
    internal void ToggleCut(SplitPageViewModel page)
    {
        if (!IsSingleFile || !page.HasGap) return;
        if (!IsCustom)
        {
            _cuts.Clear();
            foreach (SplitPageViewModel p in Pages) if (p.CutAfter) _cuts.Add(p.Number);
            _mode = 0;
            ConvertedToCustom = true;
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(IsCustom));
            OnPropertyChanged(nameof(IsFixed));
            OnPropertyChanged(nameof(Hint));
        }
        if (!_cuts.Remove(page.Number)) _cuts.Add(page.Number);
        Recompute();
        RefreshPreviewTexts();
    }

    private bool _convertedToCustom;
    /// <summary>Shown once after a gap click switched from 每 N 頁 to custom cuts.</summary>
    public bool ConvertedToCustom { get => _convertedToCustom; private set { _convertedToCustom = value; OnPropertyChanged(); } }

    // ----- 放大預覽 (spec 4.6, canvas ZoomFull): double-click a page; it covers the whole content area -----
    // ← → turn pages, Esc closes, Ctrl+wheel zooms. Zoom 1 = 整頁 (the whole page fits, the default); the steps are
    // ×1.25 like the 3.x preview, up to ×8; 符合寬度 is the zoom where the page fills the width.
    private string _file = "";
    private SplitPageViewModel? _preview;
    private IImage? _previewImage;
    private CancellationTokenSource? _previewCts;
    private double _zoom = 1;
    private double _viewportWidth, _viewportHeight, _renderScaling = 1;
    private int _renderedWidth;
    private const int PreviewBaseRenderWidth = 1600;
    private const int PreviewMaxRenderWidth = 4800;
    private const double ZoomStep = 1.25, ZoomMax = 8;

    public bool IsPreviewOpen => _preview is not null;
    public IImage? PreviewImage
    {
        get => _previewImage;
        private set { _previewImage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPreviewImage)); RefreshPreviewSize(); }
    }
    public bool HasPreviewImage => _previewImage is not null;
    public string PreviewTitle => _preview is null ? "" : $"第 {_preview.Number} 頁，共 {_totalPages} 頁";
    /// <summary>The start button is hidden under the preview, so the part count is said here.</summary>
    public string PreviewPartText => _preview is null ? "" : $"屬於第 {_preview.Part} 份，共 {PartCount} 份";
    public bool PreviewHasGap => _preview?.HasGap == true;
    public bool PreviewIsCut => _preview?.CutAfter == true;

    /// <summary>Height ÷ width of the page; taken from the image so a landscape slide stays landscape.</summary>
    private double PageAspect
    {
        get
        {
            Size size = _previewImage?.Size ?? _preview?.Thumbnail?.Size ?? default;
            return size.Width > 0 && size.Height > 0 ? size.Height / size.Width : 297.0 / 210.0;
        }
    }

    /// <summary>Width of the page at zoom 1: the whole page fits the viewport.</summary>
    private double WholePageWidth =>
        _viewportWidth <= 0 || _viewportHeight <= 0 ? 0 : Math.Min(_viewportWidth, _viewportHeight / PageAspect);
    private double FitWidthZoom => WholePageWidth <= 0 ? 1 : Math.Max(1, _viewportWidth / WholePageWidth);

    /// <summary>The white page is exactly the size of the page (no minimum width), so A4 portrait stays portrait.</summary>
    public double PreviewPageWidth => Math.Max(1, WholePageWidth * _zoom);
    public double PreviewPageHeight => Math.Max(1, WholePageWidth * _zoom * PageAspect);

    public string PreviewZoomText =>
        Math.Abs(_zoom - 1) < 0.01 ? "整頁"
        : Math.Abs(_zoom - FitWidthZoom) < 0.01 ? "符合寬度"
        : $"{Math.Round(_zoom * 100)}%";

    /// <summary>The middle zoom button: 整頁 ⇄ 符合寬度.</summary>
    public void TogglePreviewFit() => SetZoom(Math.Abs(_zoom - 1) < 0.01 ? FitWidthZoom : 1);
    public void ZoomInPreview() => SetZoom(StepZoom(+1));
    public void ZoomOutPreview() => SetZoom(StepZoom(-1));

    /// <summary>×1.25 per step, stopping at 符合寬度 on the way when it lies between two steps.</summary>
    private double StepZoom(int direction)
    {
        double next = direction > 0 ? _zoom * ZoomStep : _zoom / ZoomStep;
        double fit = FitWidthZoom;
        if (direction > 0 && _zoom < fit - 0.01 && next > fit + 0.01) next = fit;
        if (direction < 0 && _zoom > fit + 0.01 && next < fit - 0.01) next = fit;
        return Math.Clamp(next, 1, ZoomMax);
    }

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 1, ZoomMax);
        RefreshPreviewSize();
    }

    /// <summary>Called by the view when the preview area is resized (size without the page margin, in DIP).</summary>
    internal void SetPreviewViewport(double width, double height, double renderScaling)
    {
        bool wasFit = Math.Abs(_zoom - FitWidthZoom) < 0.01 && Math.Abs(_zoom - 1) >= 0.01;
        _viewportWidth = Math.Max(0, width);
        _viewportHeight = Math.Max(0, height);
        _renderScaling = renderScaling > 0 ? renderScaling : 1;
        if (wasFit) _zoom = FitWidthZoom; // 符合寬度 stays 符合寬度 when the window is resized
        RefreshPreviewSize();
    }

    private void RefreshPreviewSize()
    {
        OnPropertyChanged(nameof(PreviewPageWidth));
        OnPropertyChanged(nameof(PreviewPageHeight));
        OnPropertyChanged(nameof(PreviewZoomText));
        EnsurePreviewResolution();
    }

    internal void OpenPreview(SplitPageViewModel page)
    {
        bool wasOpen = _preview is not null;
        _preview = page;
        if (!wasOpen) _zoom = 1; // opens at 整頁; turning pages keeps the zoom
        _renderedWidth = 0;
        PreviewImage = page.Thumbnail; // the small one at once, the sharp one when it is ready
        OnPropertyChanged(nameof(IsPreviewOpen));
        RefreshPreviewTexts();
        RenderPreview(PreviewBaseRenderWidth);
    }

    /// <summary>Renders again, larger, when zooming in would show the current image blurred.</summary>
    private void EnsurePreviewResolution()
    {
        if (_preview is null || _renderedWidth <= 0) return;
        double needed = PreviewPageWidth * _renderScaling;
        if (needed <= _renderedWidth * 1.05 || _renderedWidth >= PreviewMaxRenderWidth) return;
        int width = (int)Math.Min(PreviewMaxRenderWidth, Math.Ceiling(needed / 400) * 400);
        RenderPreview(width);
    }

    private void RenderPreview(int width)
    {
        if (_preview is null) return;
        _previewCts?.Cancel();
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        string file = _file;
        int number = _preview.Number;
        _ = Task.Run(() =>
        {
            IImage? image = Render(file, number, width, null);
            if (image is null || cts.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (cts.IsCancellationRequested || _preview?.Number != number) return;
                _renderedWidth = width;
                PreviewImage = image;
            });
        });
    }

    public void PreviousPage() => MovePreview(-1);
    public void NextPage() => MovePreview(1);
    public void ClosePreview()
    {
        _previewCts?.Cancel();
        _preview = null;
        _renderedWidth = 0;
        PreviewImage = null;
        OnPropertyChanged(nameof(IsPreviewOpen));
    }
    public void TogglePreviewCut()
    {
        if (_preview is not null) ToggleCut(_preview);
        RefreshPreviewTexts();
    }

    private void MovePreview(int step)
    {
        if (_preview is null) return;
        int index = Math.Clamp(_preview.Number - 1 + step, 0, Pages.Count - 1);
        if (Pages[index] != _preview) OpenPreview(Pages[index]);
    }

    private void RefreshPreviewTexts()
    {
        OnPropertyChanged(nameof(PreviewTitle));
        OnPropertyChanged(nameof(PreviewPartText));
        OnPropertyChanged(nameof(PreviewHasGap));
        OnPropertyChanged(nameof(PreviewIsCut));
    }

    public Dictionary<string, object> BuildOptions() => new(StringComparer.Ordinal)
    {
        [SplitPdfUseCase.PagesOptionKey] = PdfSplitSpecBuilder.BuildSegmentSpec(_mode, _pagesPerPart, _totalPages, Segments()),
    };

    private List<(int Start, int End)> Segments()
    {
        var segments = new List<(int, int)>();
        int start = 1;
        foreach (int cut in _cuts)
        {
            segments.Add((start, cut));
            start = cut + 1;
        }
        if (_totalPages >= start) segments.Add((start, _totalPages));
        return segments;
    }

    private void Recompute()
    {
        int part = 1;
        for (int i = 0; i < Pages.Count; i++)
        {
            SplitPageViewModel page = Pages[i];
            bool cut = _mode switch
            {
                0 => _cuts.Contains(page.Number),
                1 => true,
                _ => page.Number % _pagesPerPart == 0,
            };
            page.Part = part;
            page.CutAfter = cut && page.HasGap;
            if (cut) part++;
        }

        PartCount = _totalPages <= 0 ? 0 : _mode switch
        {
            0 => _cuts.Count + 1,
            1 => _totalPages,
            _ => (_totalPages + _pagesPerPart - 1) / _pagesPerPart,
        };
        OnPropertyChanged(nameof(PartCount));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(BlockingText));
        OnPropertyChanged(nameof(StartText));
    }

    /// <summary>Gives the shape of a drawn page to the following pages that are not drawn yet.</summary>
    internal void AdoptAspect(SplitPageViewModel drawn)
    {
        for (int i = drawn.Number; i < Pages.Count; i++)
            if (!Pages[i].HasThumbnail) Pages[i].Aspect = drawn.Aspect;
    }

    /// <summary>Thumbnails are rendered one by one in the background (spec 4.6: only this area scrolls).</summary>
    private void LoadThumbnails(string file)
    {
        var cts = new CancellationTokenSource();
        _thumbnailsCts = cts;
        List<SplitPageViewModel> pages = Pages.ToList();
        _ = Task.Run(() =>
        {
            foreach (SplitPageViewModel page in pages)
            {
                if (cts.IsCancellationRequested) return;
                // Like the 3.x splitter: draw at 660 px (the word overlay needs room for readable text, at 160 px
            // the words shrink to 3 px and run into each other), then scale down to the tile.
            IImage? image = Render(file, page.Number, ThumbnailRenderWidth, ThumbnailDecodeHeight);
                if (image is null) return; // rendering is not available here: keep the numbered tiles
                Dispatcher.UIThread.Post(() => page.Thumbnail = image);
            }
        });
    }

    private const int ThumbnailRenderWidth = 660;
    private const int ThumbnailDecodeHeight = 216; // the 108 px tile at 200 % scaling

    /// <summary>Renders a page at <paramref name="size"/> px wide and, when <paramref name="decodeHeight"/> is given, keeps
    /// a high-quality copy scaled to that height (the tile's), so dozens of tiles do not hold full-size bitmaps and a
    /// landscape slide keeps enough pixels for its wider tile.</summary>
    private static IImage? Render(string file, int pageNumber, int size, int? decodeHeight)
    {
        try
        {
#pragma warning disable CA1416 // thumbnails use System.Drawing, available on Windows; elsewhere the tiles stay numbered
            using System.Drawing.Bitmap? bitmap = PdfPageThumbnailQuery.RenderPageFromFile(file, pageNumber, size);
            if (bitmap is null) return null;
            int? decodeWidth = decodeHeight is int h && bitmap.Height > 0
                ? Math.Min(bitmap.Width, (int)Math.Ceiling((double)h * bitmap.Width / bitmap.Height))
                : null;
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
#pragma warning restore CA1416
            stream.Position = 0;
            return decodeWidth is int width
                ? Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, width, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality)
                : new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}

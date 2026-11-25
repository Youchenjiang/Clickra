using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using Clickra.Core.Modules.Pdf.Print.Application;
using Clickra.Desktop.L10n;

namespace Clickra.Desktop.ViewModels;

public sealed class PrintCellViewModel
{
    public int PageNumber { get; }
    public PrintRect Cell { get; }
    public PrintRect Page { get; }
    public IImage? Thumbnail { get; set; }

    public PrintCellViewModel(int pageNumber, PrintRect cell, PrintRect page)
    {
        PageNumber = pageNumber;
        Cell = cell;
        Page = page;
    }
}

public sealed class PrintSheetViewModel
{
    public event Action? Invalidated;
    public float WidthPt { get; }
    public float HeightPt { get; }
    public bool Landscape { get; }
    public bool IsBack { get; }
    public bool Overflows { get; }
    public bool PageBorder { get; }
    public int PagesPerSheet { get; }
    public IReadOnlyList<PrintCellViewModel> Cells { get; }

    public PrintSheetViewModel(PrintSheet sheet, float widthPt, float heightPt, bool pageBorder, int pagesPerSheet)
    {
        WidthPt = widthPt;
        HeightPt = heightPt;
        Landscape = sheet.Landscape;
        IsBack = sheet.IsBack;
        Overflows = sheet.Cells.Any(c => c.Overflows);
        PageBorder = pageBorder;
        PagesPerSheet = pagesPerSheet;
        Cells = sheet.Cells.Select(c => new PrintCellViewModel(c.PageNumber, c.Cell, c.Page)).ToList();
    }

    public void NotifyInvalidated() => Invalidated?.Invoke();
}

public sealed class PrintPreviewFileItem
{
    public string FilePath { get; }
    public string FileName { get; }
    public int TotalPages { get; }
    public int SelectedPagesCount { get; }
    public IReadOnlyList<PrintSheetViewModel> Sheets { get; }
    public IReadOnlyList<PrintSheet> PlannedSheets { get; }

    public PrintPreviewFileItem(
        string filePath,
        int totalPages,
        int selectedPagesCount,
        IReadOnlyList<PrintSheetViewModel> sheets,
        IReadOnlyList<PrintSheet> plannedSheets)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        TotalPages = totalPages;
        SelectedPagesCount = selectedPagesCount;
        Sheets = sheets;
        PlannedSheets = plannedSheets;
    }
}

public sealed class PrintPreviewViewModel : ViewModelBase
{
    private IReadOnlyList<string> _filePaths = Array.Empty<string>();
    private IReadOnlyList<PrintPreviewFileItem> _files = Array.Empty<PrintPreviewFileItem>();
    private int _currentFileIndex;
    private int _currentSheetIndex;

    private int _copies = 1;
    private int _pagesPerSheet = 1;
    private bool _isDuplex;
    private bool _hasPrinters = true;
    private string _paperName = "A4";
    private bool _darkenText;
    private bool _grayscale;

    private CancellationTokenSource? _renderCts;
    private readonly Dictionary<(string File, int Page, bool Darken, bool Grayscale), IImage> _thumbnailCache = new();
    private readonly LinkedList<(string File, int Page, bool Darken, bool Grayscale)> _thumbnailLruList = new();
    private readonly object _thumbnailLock = new();
    private const int MaxThumbnailCacheSize = 48;

    private static readonly ConcurrentDictionary<(string Path, DateTime LastWriteUtc), IReadOnlyList<PrintPageSize>> _pageSizesCache = new();
    private CancellationTokenSource? _updateCts;

    public ZoomPreviewViewModel Zoom { get; } = new();
    public bool IsPreviewOpen => Zoom.IsOpen;
    private CancellationTokenSource? _zoomCts;
    private int _zoomVersion;

    public Func<string, string?, PrintPageSizesResult>? CustomPageSizesReader { get; set; }

    public bool HasMultipleFiles => _files.Count > 1;
    public int FileCount => _files.Count;

    public int CurrentFileIndex
    {
        get => _currentFileIndex;
        private set
        {
            if (_currentFileIndex != value)
            {
                _currentFileIndex = value;
                _currentSheetIndex = 0;
                NotifyFileAndSheetChanged();
                TriggerThumbnailRendering();
            }
        }
    }

    public PrintPreviewFileItem? CurrentFile =>
        _currentFileIndex >= 0 && _currentFileIndex < _files.Count ? _files[_currentFileIndex] : null;

    public string CurrentFileName => CurrentFile?.FileName ?? "";

    public bool CanPreviousFile => _currentFileIndex > 0;
    public bool CanNextFile => _currentFileIndex < _files.Count - 1;

    public void PreviousFile()
    {
        if (CanPreviousFile) CurrentFileIndex--;
    }

    public void NextFile()
    {
        if (CanNextFile) CurrentFileIndex++;
    }

    public string FileIndicatorText =>
        _files.Count == 0 ? "" : Strings.F("ui_print_file_of", ("no", _currentFileIndex + 1), ("count", _files.Count));

    public string FileDetailsText
    {
        get
        {
            var cur = CurrentFile;
            if (cur is null) return "";
            if (cur.SelectedPagesCount == 0)
            {
                return Strings.Count("ui_print_file_skipped", cur.TotalPages);
            }
            if (cur.SelectedPagesCount < cur.TotalPages)
            {
                return Strings.F("ui_print_file_pages_selected", ("count", cur.TotalPages), ("selected", cur.SelectedPagesCount));
            }
            return Strings.Count("ui_print_file_pages", cur.TotalPages);
        }
    }

    public bool IsCurrentFileSkipped => CurrentFile is not null && CurrentFile.SelectedPagesCount == 0;

    public int CurrentSheetIndex
    {
        get => _currentSheetIndex;
        private set
        {
            if (_currentSheetIndex != value)
            {
                _currentSheetIndex = value;
                NotifySheetChanged();
                TriggerThumbnailRendering();
            }
        }
    }

    public IReadOnlyList<PrintSheetViewModel> CurrentSheets => CurrentFile?.Sheets ?? Array.Empty<PrintSheetViewModel>();
    public int SheetCount => CurrentSheets.Count;

    public PrintSheetViewModel? CurrentSheet =>
        _currentSheetIndex >= 0 && _currentSheetIndex < CurrentSheets.Count ? CurrentSheets[_currentSheetIndex] : null;

    public bool CanPreviousSheet => _currentSheetIndex > 0;
    public bool CanNextSheet => _currentSheetIndex < SheetCount - 1;

    public void PreviousSheet()
    {
        if (CanPreviousSheet) CurrentSheetIndex--;
    }

    public void NextSheet()
    {
        if (CanNextSheet) CurrentSheetIndex++;
    }

    public string SheetIndicatorText
    {
        get
        {
            if (SheetCount == 0 || CurrentSheet is null) return "";
            if (!_isDuplex)
            {
                return Strings.F("ui_print_sheet_of", ("no", _currentSheetIndex + 1), ("count", SheetCount));
            }

            int paperNo = (_currentSheetIndex / 2) + 1;
            int paperCount = (SheetCount + 1) / 2;
            string key = CurrentSheet.IsBack ? "ui_print_sheet_back_of" : "ui_print_sheet_front_of";
            return Strings.F(key, ("no", paperNo), ("count", paperCount));
        }
    }

    public string SheetDetailsText
    {
        get
        {
            var sheet = CurrentSheet;
            if (sheet is null) return "";
            if (sheet.Overflows)
            {
                return Strings.T("ui_print_outside_not_printed");
            }
            if (_pagesPerSheet > 1)
            {
                string key = sheet.Landscape ? "ui_print_sheet_landscape_multi" : "ui_print_sheet_portrait_multi";
                return Strings.F(key, ("paper", _paperName), ("count", _pagesPerSheet));
            }
            else
            {
                string key = sheet.Landscape ? "ui_print_sheet_landscape" : "ui_print_sheet_portrait";
                return Strings.F(key, ("paper", _paperName));
            }
        }
    }

    public bool IsSheetOverflow => CurrentSheet?.Overflows == true;

    private string _summaryText = "";
    public string SummaryText => _summaryText;

    private string _skippedWarningText = "";
    public string SkippedWarningText => _skippedWarningText;
    public bool HasSkippedWarning => !string.IsNullOrEmpty(_skippedWarningText);

    private bool _canPrint;
    public bool CanPrint => _canPrint;

    private string _blockingText = "";
    public string BlockingText => _blockingText;

    public void Update(IReadOnlyList<string> filePaths, ActionOptionsViewModel options)
    {
        var newFiles = filePaths ?? Array.Empty<string>();
        bool filesChanged = !_filePaths.SequenceEqual(newFiles);
        _filePaths = newFiles;

        if (filesChanged)
        {
            ClearThumbnailCache();
        }

        _hasPrinters = options.HasPrinters;
        _copies = Math.Max(1, (int)options.Copies);
        _pagesPerSheet = options.PagesPerSheet;
        bool effectiveDuplex = options.CanDuplex && options.DuplexIndex > 0;
        _isDuplex = effectiveDuplex;
        _paperName = options.PaperIndex >= 0 && options.PaperIndex < options.PaperSizes.Count
            ? options.PaperSizes[options.PaperIndex]
            : "A4";

        bool newDarken = options.DarkenText;
        bool newGrayscale = options.Grayscale;
        if (newDarken != _darkenText || newGrayscale != _grayscale)
        {
            ClearThumbnailCache();
            _darkenText = newDarken;
            _grayscale = newGrayscale;
        }

        var paperDim = PaperSizeRegistry.GetDimensions(_paperName);
        float portW = Math.Min(paperDim.WidthPt, paperDim.HeightPt);
        float portH = Math.Max(paperDim.WidthPt, paperDim.HeightPt);

        var duplexMode = effectiveDuplex
            ? (PrintDuplexMode)Math.Clamp(options.DuplexIndex, 0, 2)
            : PrintDuplexMode.Simplex;
        var orientationMode = (PrintOrientation)Math.Clamp(options.OrientationIndex, 0, 2);

        var printOptions = new PrintPdfOptions(
            printerName: null,
            pageRange: options.PrintPages,
            copies: _copies,
            dpi: 300,
            fitToPage: options.ScaleModeIndex == 0,
            language: null,
            darkenText: _darkenText,
            darkenGamma: 2.2,
            duplex: duplexMode,
            paperSize: _paperName,
            orientation: orientationMode,
            pagesPerSheet: _pagesPerSheet,
            pageOrder: options.PageOrder,
            pageBorder: options.PageBorder,
            scale: options.ScaleMode,
            scalePercent: (int)options.ScalePercent,
            grayscale: _grayscale,
            collate: options.Collate);

        // Cancel previous background update if still in-flight
        _updateCts?.Cancel();
        _updateCts?.Dispose();
        var cts = new CancellationTokenSource();
        _updateCts = cts;
        var token = cts.Token;

        // Check if all files have their page sizes cached or if custom reader is supplied
        bool allCached = true;
        if (CustomPageSizesReader == null)
        {
            foreach (string file in _filePaths)
            {
                if (!File.Exists(file)) continue;
                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (!_pageSizesCache.ContainsKey((file, lastWrite)))
                {
                    allCached = false;
                    break;
                }
            }
        }

        if (allCached || CustomPageSizesReader != null)
        {
            ApplyLayoutPlan(_filePaths, printOptions, portW, portH, options.PageBorder, effectiveDuplex);
        }
        else
        {
            // Initial read or missing cache: do on background thread
            var capturedFilePaths = _filePaths.ToList();
            Task.Run(() =>
            {
                foreach (string file in capturedFilePaths)
                {
                    if (token.IsCancellationRequested) return;
                    if (File.Exists(file))
                    {
                        var lastWrite = File.GetLastWriteTimeUtc(file);
                        _pageSizesCache.GetOrAdd((file, lastWrite), _ => PrintPreviewQuery.ReadAllPageSizes(file));
                    }
                }

                if (!token.IsCancellationRequested)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            ApplyLayoutPlan(capturedFilePaths, printOptions, portW, portH, options.PageBorder, effectiveDuplex);
                        }
                    });
                }
            }, token);
        }
    }

    private void ApplyLayoutPlan(
        IReadOnlyList<string> filePaths,
        PrintPdfOptions printOptions,
        float portW,
        float portH,
        bool pageBorder,
        bool effectiveDuplex)
    {
        var fileItems = new List<PrintPreviewFileItem>(filePaths.Count);
        int totalSelectedPages = 0;
        int totalPaperSheets = 0;
        var skippedFiles = new List<string>();

        foreach (string file in filePaths)
        {
            PrintPageSizesResult result;
            if (CustomPageSizesReader != null)
            {
                result = CustomPageSizesReader(file, printOptions.PageRange);
            }
            else
            {
                IReadOnlyList<PrintPageSize> allPages;
                if (File.Exists(file))
                {
                    var lastWrite = File.GetLastWriteTimeUtc(file);
                    allPages = _pageSizesCache.GetOrAdd((file, lastWrite), _ => PrintPreviewQuery.ReadAllPageSizes(file));
                }
                else
                {
                    allPages = Array.Empty<PrintPageSize>();
                }

                if (allPages.Count == 0)
                {
                    result = new PrintPageSizesResult(Array.Empty<PrintPageSize>(), 0);
                }
                else
                {
                    List<int> resolvedPages = PrintLayoutPlanner.ResolvePageNumbers(printOptions.PageRange, allPages.Count);
                    var selected = new List<PrintPageSize>(resolvedPages.Count);
                    foreach (int pNum in resolvedPages)
                    {
                        if (pNum >= 1 && pNum <= allPages.Count)
                            selected.Add(allPages[pNum - 1]);
                        else
                            selected.Add(new PrintPageSize(pNum, 595.28f, 841.89f));
                    }
                    result = new PrintPageSizesResult(selected, allPages.Count);
                }
            }

            var (pages, totalDocPages) = result;
            var plannedSheets = PrintLayoutPlanner.Plan(pages, portW, portH, printOptions);

            var sheetVms = new List<PrintSheetViewModel>(plannedSheets.Count);
            foreach (var s in plannedSheets)
            {
                float w = s.Landscape ? portH : portW;
                float h = s.Landscape ? portW : portH;
                sheetVms.Add(new PrintSheetViewModel(s, w, h, pageBorder, _pagesPerSheet));
            }

            int selCount = pages.Count;
            totalSelectedPages += selCount;
            if (selCount == 0)
            {
                skippedFiles.Add(Path.GetFileName(file));
            }

            int filePaperCount = PrintLayoutPlanner.PaperCount(plannedSheets, printOptions.Duplex);
            totalPaperSheets += filePaperCount * _copies;

            fileItems.Add(new PrintPreviewFileItem(file, totalDocPages, selCount, sheetVms, plannedSheets));
        }

        _files = fileItems;
        if (_currentFileIndex >= _files.Count) _currentFileIndex = Math.Max(0, _files.Count - 1);
        if (_currentSheetIndex >= SheetCount) _currentSheetIndex = Math.Max(0, SheetCount - 1);

        // Build details tokens
        var detailsList = new List<string>();
        if (_files.Count >= 2)
        {
            detailsList.Add(Strings.Count("ui_print_summary_files", _files.Count));
        }
        detailsList.Add(Strings.Count("ui_print_summary_pages", totalSelectedPages));
        if (_pagesPerSheet >= 2)
        {
            detailsList.Add(Strings.F("ui_print_summary_per_sheet", ("count", _pagesPerSheet)));
        }
        if (effectiveDuplex)
        {
            detailsList.Add(Strings.T("ui_print_summary_two_sided"));
        }
        if (_copies >= 2)
        {
            detailsList.Add(Strings.Count("ui_print_summary_copies", _copies));
        }

        string detailsJoined = Strings.List(detailsList);
        _summaryText = Strings.Count("ui_print_summary", totalPaperSheets, ("details", detailsJoined), ("sheets", totalPaperSheets));

        if (skippedFiles.Count == 1)
        {
            _skippedWarningText = Strings.F("ui_print_skipped_one", ("name", skippedFiles[0]));
        }
        else if (skippedFiles.Count > 1)
        {
            _skippedWarningText = Strings.F("ui_print_skipped_many", ("names", Strings.ShortList(skippedFiles)));
        }
        else
        {
            _skippedWarningText = "";
        }

        _canPrint = totalSelectedPages > 0 && _hasPrinters;
        _blockingText = totalSelectedPages == 0 && _files.Count > 0 ? Strings.T("ui_print_no_selected_pages") : "";

        NotifyAllProperties();
        TriggerThumbnailRendering();
    }

    private void NotifyAllProperties()
    {
        OnPropertyChanged(nameof(HasMultipleFiles));
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(CurrentFileIndex));
        OnPropertyChanged(nameof(CurrentFile));
        OnPropertyChanged(nameof(CurrentFileName));
        OnPropertyChanged(nameof(CanPreviousFile));
        OnPropertyChanged(nameof(CanNextFile));
        OnPropertyChanged(nameof(FileIndicatorText));
        OnPropertyChanged(nameof(FileDetailsText));
        OnPropertyChanged(nameof(IsCurrentFileSkipped));
        OnPropertyChanged(nameof(CurrentSheetIndex));
        OnPropertyChanged(nameof(CurrentSheets));
        OnPropertyChanged(nameof(SheetCount));
        OnPropertyChanged(nameof(CurrentSheet));
        OnPropertyChanged(nameof(CanPreviousSheet));
        OnPropertyChanged(nameof(CanNextSheet));
        OnPropertyChanged(nameof(SheetIndicatorText));
        OnPropertyChanged(nameof(SheetDetailsText));
        OnPropertyChanged(nameof(IsSheetOverflow));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(SkippedWarningText));
        OnPropertyChanged(nameof(HasSkippedWarning));
        OnPropertyChanged(nameof(CanPrint));
        OnPropertyChanged(nameof(BlockingText));
    }

    private void NotifyFileAndSheetChanged()
    {
        OnPropertyChanged(nameof(CurrentFileIndex));
        OnPropertyChanged(nameof(CurrentFile));
        OnPropertyChanged(nameof(CurrentFileName));
        OnPropertyChanged(nameof(CanPreviousFile));
        OnPropertyChanged(nameof(CanNextFile));
        OnPropertyChanged(nameof(FileIndicatorText));
        OnPropertyChanged(nameof(FileDetailsText));
        OnPropertyChanged(nameof(IsCurrentFileSkipped));
        NotifySheetChanged();
    }

    private void NotifySheetChanged()
    {
        OnPropertyChanged(nameof(CurrentSheetIndex));
        OnPropertyChanged(nameof(CurrentSheets));
        OnPropertyChanged(nameof(SheetCount));
        OnPropertyChanged(nameof(CurrentSheet));
        OnPropertyChanged(nameof(CanPreviousSheet));
        OnPropertyChanged(nameof(CanNextSheet));
        OnPropertyChanged(nameof(SheetIndicatorText));
        OnPropertyChanged(nameof(SheetDetailsText));
        OnPropertyChanged(nameof(IsSheetOverflow));
    }

    private void ClearThumbnailCache()
    {
        lock (_thumbnailLock)
        {
            _thumbnailCache.Clear();
            _thumbnailLruList.Clear();
        }
    }

    private void PutThumbnail((string File, int Page, bool Darken, bool Grayscale) key, IImage image)
    {
        lock (_thumbnailLock)
        {
            if (_thumbnailCache.ContainsKey(key))
            {
                _thumbnailLruList.Remove(key);
                _thumbnailLruList.AddLast(key);
                _thumbnailCache[key] = image;
                return;
            }

            if (_thumbnailCache.Count >= MaxThumbnailCacheSize)
            {
                var oldest = _thumbnailLruList.First?.Value;
                if (oldest.HasValue)
                {
                    _thumbnailLruList.RemoveFirst();
                    _thumbnailCache.Remove(oldest.Value);
                }
            }

            _thumbnailLruList.AddLast(key);
            _thumbnailCache[key] = image;
        }
    }

    private bool TryGetThumbnail((string File, int Page, bool Darken, bool Grayscale) key, out IImage? image)
    {
        lock (_thumbnailLock)
        {
            if (_thumbnailCache.TryGetValue(key, out var cached))
            {
                _thumbnailLruList.Remove(key);
                _thumbnailLruList.AddLast(key);
                image = cached;
                return true;
            }
        }
        image = null;
        return false;
    }

    private void TriggerThumbnailRendering()
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;

        var file = CurrentFile;
        var sheet = CurrentSheet;
        if (file is null || sheet is null) return;

        // Check which cells need rendering
        var needRendering = new List<PrintCellViewModel>();
        foreach (var cell in sheet.Cells)
        {
            var key = (file.FilePath, cell.PageNumber, _darkenText, _grayscale);
            if (TryGetThumbnail(key, out var cached))
            {
                cell.Thumbnail = cached;
            }
            else
            {
                needRendering.Add(cell);
            }
        }

        sheet.NotifyInvalidated();
        if (needRendering.Count == 0) return;

        var cts = new CancellationTokenSource();
        _renderCts = cts;
        var token = cts.Token;
        string filePath = file.FilePath;
        bool darken = _darkenText;
        bool grayscale = _grayscale;

        Task.Run(() =>
        {
            foreach (var cell in needRendering)
            {
                if (token.IsCancellationRequested) break;
                try
                {
#pragma warning disable CA1416
                    using System.Drawing.Bitmap? bmp = PrintPreviewQuery.RenderPage(filePath, cell.PageNumber, 600, darken, grayscale);
                    if (bmp is null) continue;

                    using var ms = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    var avaloniaBmp = new Avalonia.Media.Imaging.Bitmap(ms);
                    var key = (filePath, cell.PageNumber, darken, grayscale);
                    PutThumbnail(key, avaloniaBmp);

                    if (!token.IsCancellationRequested)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (!token.IsCancellationRequested)
                            {
                                cell.Thumbnail = avaloniaBmp;
                                sheet.NotifyInvalidated();
                            }
                        });
                    }
#pragma warning restore CA1416
                }
                catch
                {
                    // Ignore decoding failures
                }
            }
        }, token);
    }

    public void OpenZoom()
    {
        var sheet = CurrentSheet;
        if (sheet is null || sheet.WidthPt <= 0 || sheet.HeightPt <= 0) return;

        Zoom.Aspect = (double)sheet.HeightPt / sheet.WidthPt;
        Zoom.Title = SheetIndicatorText;
        Zoom.SubTitle = SheetDetailsText;
        Zoom.CanPrevious = CanPreviousSheet;
        Zoom.CanNext = CanNextSheet;
        Zoom.HasCustomAction = false;
        Zoom.OnPrevious = () =>
        {
            PreviousSheet();
            RefreshZoom();
        };
        Zoom.OnNext = () =>
        {
            NextSheet();
            RefreshZoom();
        };
        Zoom.OnClose = () =>
        {
            _zoomCts?.Cancel();
            _zoomCts?.Dispose();
            _zoomCts = null;
            Zoom.Image = null;
            Zoom.IsOpen = false;
            OnPropertyChanged(nameof(IsPreviewOpen));
        };

        Zoom.IsOpen = true;
        OnPropertyChanged(nameof(IsPreviewOpen));
        RefreshZoom();
    }

    private void RefreshZoom()
    {
        var sheet = CurrentSheet;
        if (sheet is null || !Zoom.IsOpen) return;

        _zoomCts?.Cancel();
        _zoomCts?.Dispose();
        var cts = new CancellationTokenSource();
        _zoomCts = cts;
        int version = ++_zoomVersion;

        Zoom.Image = null;
        Zoom.Aspect = (double)sheet.HeightPt / sheet.WidthPt;
        Zoom.Title = SheetIndicatorText;
        Zoom.SubTitle = SheetDetailsText;
        Zoom.CanPrevious = CanPreviousSheet;
        Zoom.CanNext = CanNextSheet;

        string? filePath = CurrentFile?.FilePath;
        bool darken = _darkenText;
        bool grayscale = _grayscale;

        Task.Run(() =>
        {
            try
            {
                if (cts.IsCancellationRequested) return;

#pragma warning disable CA1416
                using var bmp = RenderSheetBitmap(sheet, 1600, filePath, darken, grayscale);
                if (bmp is null || cts.IsCancellationRequested) return;
                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                var avaloniaBmp = new Avalonia.Media.Imaging.Bitmap(ms);
                Dispatcher.UIThread.Post(() =>
                {
                    if (Zoom.IsOpen && version == _zoomVersion && CurrentSheet == sheet && !cts.IsCancellationRequested)
                    {
                        Zoom.Image = avaloniaBmp;
                    }
                });
#pragma warning restore CA1416
            }
            catch
            {
                // Ignore rendering failures
            }
        }, cts.Token);
    }

#pragma warning disable CA1416
    private static System.Drawing.Bitmap? RenderSheetBitmap(
        PrintSheetViewModel sheet,
        int targetWidth,
        string? filePath,
        bool darken,
        bool grayscale)
    {
        if (sheet.WidthPt <= 0 || sheet.HeightPt <= 0) return null;
        int targetHeight = (int)Math.Max(1, Math.Round(targetWidth * sheet.HeightPt / sheet.WidthPt));
        var bmp = new System.Drawing.Bitmap(targetWidth, targetHeight);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.White);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

        float scale = (float)targetWidth / sheet.WidthPt;

        // Clip drawing to paper bounds
        var paperClipRect = new System.Drawing.RectangleF(0, 0, targetWidth, targetHeight);
        g.SetClip(paperClipRect);

        using var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0xD0, 0xD5, 0xDD), 1);
        using var font = new System.Drawing.Font("Segoe UI", Math.Max(9, 12 * scale / 2));
        using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x4B, 0x55, 0x63));
        using var numBgBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF));

        foreach (var cell in sheet.Cells)
        {
            var pRect = new System.Drawing.RectangleF(cell.Page.X * scale, cell.Page.Y * scale, cell.Page.Width * scale, cell.Page.Height * scale);
            if (!string.IsNullOrEmpty(filePath))
            {
                using var pageBmp = PrintPreviewQuery.RenderPage(filePath, cell.PageNumber, (int)Math.Max(100, pRect.Width * 1.5f), darken, grayscale);
                if (pageBmp != null)
                {
                    g.DrawImage(pageBmp, pRect);
                }
            }

            if (sheet.PageBorder && sheet.PagesPerSheet > 1)
            {
                g.DrawRectangle(borderPen, pRect.X, pRect.Y, pRect.Width, pRect.Height);
            }

            if (sheet.PagesPerSheet > 1)
            {
                string numStr = cell.PageNumber.ToString();
                var numSize = g.MeasureString(numStr, font);
                float numX = (cell.Cell.X + cell.Cell.Width) * scale - numSize.Width - 4;
                float numY = (cell.Cell.Y + cell.Cell.Height) * scale - numSize.Height - 2;
                g.FillRectangle(numBgBrush, numX - 2, numY - 1, numSize.Width + 4, numSize.Height + 2);
                g.DrawString(numStr, font, textBrush, numX, numY);
            }
        }

        // Orange dashed printable area overlay if overflow (drawn on top of cells)
        if (sheet.Overflows)
        {
            float shortSide = Math.Min(sheet.WidthPt, sheet.HeightPt);
            float margin = shortSide * 0.03f;
            using var dashPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0xF5, 0x9E, 0x0B), 2)
            {
                DashPattern = new float[] { 4, 4 }
            };
            g.DrawRectangle(dashPen, margin * scale, margin * scale, (sheet.WidthPt - 2 * margin) * scale, (sheet.HeightPt - 2 * margin) * scale);
        }

        return bmp;
    }
#pragma warning restore CA1416
}

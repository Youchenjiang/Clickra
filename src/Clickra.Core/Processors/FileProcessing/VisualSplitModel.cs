using System;
using System.Collections.Generic;

namespace Clickra.Core.Processors;

/// <summary>
/// Pure state model shared by the Win32 and WinUI visual PDF splitters.
/// UI surfaces own rendering and input wiring; segment state transitions live here.
/// </summary>
public sealed class VisualSplitModel
{
    public const int ModeCustom = 0;
    public const int ModeEachPage = 1;
    public const int ModeFixedPages = 2;

    private readonly List<(int Start, int End)> _segments = new();
    private readonly List<(int Start, int End)> _customSegments = new();

    public VisualSplitModel(int totalPages)
    {
        TotalPages = Math.Max(1, totalPages);
        PagesPerSegment = Math.Min(5, TotalPages);

        if (TotalPages == 1)
        {
            _customSegments.Add((1, 1));
        }
        else
        {
            int half = TotalPages / 2;
            _customSegments.Add((1, half));
            _customSegments.Add((half + 1, TotalPages));
        }

        ApplyMode(ModeCustom);
    }

    public int TotalPages { get; }
    public int Mode { get; private set; }
    public int PagesPerSegment { get; private set; }
    public int SelectedSegmentIndex { get; private set; }
    public int PreviewPageIndex { get; private set; }
    public IReadOnlyList<(int Start, int End)> Segments => _segments;
    public IReadOnlyList<(int Start, int End)> CustomSegments => _customSegments;

    public int CurrentPageNumber
    {
        get
        {
            if (SelectedSegmentIndex < 0 || SelectedSegmentIndex >= _segments.Count)
                return 1;

            return _segments[SelectedSegmentIndex].Start + PreviewPageIndex;
        }
    }

    public void SetMode(int mode) => ApplyMode(mode);

    public bool AdjustPagesPerSegment(int delta)
    {
        int next = Math.Clamp(PagesPerSegment + delta, 1, TotalPages);
        if (next == PagesPerSegment) return false;

        PagesPerSegment = next;
        if (Mode == ModeFixedPages) ApplyMode(ModeFixedPages);
        return true;
    }

    public bool SelectSegment(int index)
    {
        if (index < 0 || index >= _segments.Count) return false;

        SelectedSegmentIndex = index;
        PreviewPageIndex = 0;
        return true;
    }

    public bool NavigatePreview(int delta)
    {
        if (SelectedSegmentIndex < 0 || SelectedSegmentIndex >= _segments.Count) return false;

        var segment = _segments[SelectedSegmentIndex];
        int pageCount = segment.End - segment.Start + 1;
        int next = Math.Clamp(PreviewPageIndex + delta, 0, pageCount - 1);
        if (next == PreviewPageIndex) return false;

        PreviewPageIndex = next;
        return true;
    }

    public bool SplitSelectedAtPreviewPage()
    {
        if (SelectedSegmentIndex < 0 || SelectedSegmentIndex >= _customSegments.Count) return false;

        var segment = _customSegments[SelectedSegmentIndex];
        int pageCount = segment.End - segment.Start + 1;
        if (pageCount <= 1) return false;

        int previewIndex = Math.Clamp(PreviewPageIndex, 0, pageCount - 1);
        int splitPage = segment.Start + previewIndex;
        if (splitPage >= segment.End) return false;

        var first = (segment.Start, splitPage);
        var second = (splitPage + 1, segment.End);
        _customSegments.RemoveAt(SelectedSegmentIndex);
        _customSegments.Insert(SelectedSegmentIndex, second);
        _customSegments.Insert(SelectedSegmentIndex, first);
        CopyCustomSegmentsToVisible();
        PreviewPageIndex = 0;
        Mode = ModeCustom;
        return true;
    }

    public bool AddSegment()
    {
        Mode = ModeCustom;

        if (_customSegments.Count == 0)
        {
            _customSegments.Add((1, TotalPages));
            CopyCustomSegmentsToVisible();
            SelectedSegmentIndex = 0;
            PreviewPageIndex = 0;
            return true;
        }

        var covered = new HashSet<int>();
        foreach (var segment in _customSegments)
        {
            for (int page = segment.Start; page <= segment.End; page++)
                covered.Add(page);
        }

        int gapStart = -1;
        int gapEnd = -1;
        for (int page = 1; page <= TotalPages; page++)
        {
            if (!covered.Contains(page))
            {
                if (gapStart < 0) gapStart = page;
                gapEnd = page;
            }
            else if (gapStart > 0)
            {
                break;
            }
        }

        if (gapStart < 0) return false;

        _customSegments.Add((gapStart, gapEnd));
        _customSegments.Sort((a, b) => a.Start.CompareTo(b.Start));
        CopyCustomSegmentsToVisible();
        SelectedSegmentIndex = _segments.FindIndex(s => s.Start == gapStart && s.End == gapEnd);
        PreviewPageIndex = 0;
        return true;
    }

    public bool DeleteSelectedSegment()
    {
        if (_customSegments.Count <= 1) return false;
        if (SelectedSegmentIndex < 0 || SelectedSegmentIndex >= _customSegments.Count) return false;

        Mode = ModeCustom;
        _customSegments.RemoveAt(SelectedSegmentIndex);
        CopyCustomSegmentsToVisible();
        if (SelectedSegmentIndex >= _segments.Count)
            SelectedSegmentIndex = _segments.Count - 1;
        PreviewPageIndex = 0;
        return true;
    }

    public void ClearSegments()
    {
        Mode = ModeCustom;
        _customSegments.Clear();
        _segments.Clear();
        SelectedSegmentIndex = -1;
        PreviewPageIndex = 0;
    }

    public string BuildSpec() =>
        PdfSplitProcessor.BuildSegmentSpec(Mode, PagesPerSegment, TotalPages, _segments);

    private void ApplyMode(int mode)
    {
        Mode = mode is >= ModeCustom and <= ModeFixedPages ? mode : ModeCustom;
        PreviewPageIndex = 0;

        switch (Mode)
        {
            case ModeEachPage:
                _segments.Clear();
                _customSegments.Clear();
                for (int page = 1; page <= TotalPages; page++)
                {
                    _segments.Add((page, page));
                    _customSegments.Add((page, page));
                }
                break;
            case ModeFixedPages:
                _segments.Clear();
                _customSegments.Clear();
                int size = Math.Max(1, PagesPerSegment);
                for (int page = 1; page <= TotalPages; page += size)
                {
                    int end = Math.Min(page + size - 1, TotalPages);
                    _segments.Add((page, end));
                    _customSegments.Add((page, end));
                }
                break;
            default:
                CopyCustomSegmentsToVisible();
                if (_segments.Count == 0)
                {
                    _customSegments.Add((1, TotalPages));
                    CopyCustomSegmentsToVisible();
                }
                break;
        }

        SelectedSegmentIndex = _segments.Count > 0 ? 0 : -1;
    }

    private void CopyCustomSegmentsToVisible()
    {
        _segments.Clear();
        _segments.AddRange(_customSegments);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

/// <summary>
/// 視覺化分割介面的操作序列與頁碼規格（Page Spec）黃金案例表：
/// 用同一組操作腳本序列同時驅動 Win32 CLI 分割器邏輯與 Fluent WinUI 分割器邏輯，
/// 嚴密驗證兩者在所有邊界情境下產生完全相同的輸出。
/// </summary>
static partial class TestSuite
{
    public interface IVisualSplitterDriver
    {
        int TotalPages { get; }
        int Mode { get; }
        int NPages { get; }
        int SelectedSegmentIndex { get; }
        int CurrentPreviewPageIndex { get; }
        IReadOnlyList<(int Start, int End)> Segments { get; }
        IReadOnlyList<(int Start, int End)> CustomSegments { get; }
        string GetSpec();

        void SetMode(int mode);
        void AdjustN(int delta);
        void SelectSegment(int index);
        void NavigatePreview(int delta);
        void SplitAtCurrentPage();
        void AddSegment();
        void DeleteSegment();
        void ClearSegments();
    }

    /// <summary>
    /// 忠實模擬 Win32 CLI ProgressWindow.VisualSplitter.cs 與 ProgressWindow.Controls.cs
    /// 的狀態機與使用者互動邏輯。
    /// </summary>
    public sealed class CliVisualSplitterDriver : IVisualSplitterDriver
    {
        private readonly int _visualSplitTotalPages;
        private int _visualSplitMode;
        private int _visualSplitNPages;
        private List<(int Start, int End)> _visualSplitSegments = new();
        private readonly List<(int Start, int End)> _visualSplitCustomSegments = new();
        private int _visualSplitSelectedSegmentIndex;
        private int _visualSplitCurrentPreviewPageIndex;

        public int TotalPages => _visualSplitTotalPages;
        public int Mode => _visualSplitMode;
        public int NPages => _visualSplitNPages;
        public int SelectedSegmentIndex => _visualSplitSelectedSegmentIndex;
        public int CurrentPreviewPageIndex => _visualSplitCurrentPreviewPageIndex;
        public IReadOnlyList<(int Start, int End)> Segments => _visualSplitSegments;
        public IReadOnlyList<(int Start, int End)> CustomSegments => _visualSplitCustomSegments;

        public CliVisualSplitterDriver(int totalPages)
        {
            if (totalPages <= 0) totalPages = 1;
            _visualSplitTotalPages = totalPages;
            _visualSplitMode = 0;
            _visualSplitNPages = Math.Min(5, totalPages);
            _visualSplitSegments.Clear();
            _visualSplitCustomSegments.Clear();

            if (totalPages == 1)
            {
                _visualSplitCustomSegments.Add((1, 1));
            }
            else
            {
                int half = totalPages / 2;
                _visualSplitCustomSegments.Add((1, half));
                _visualSplitCustomSegments.Add((half + 1, totalPages));
            }

            _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
            _visualSplitSelectedSegmentIndex = 0;
            _visualSplitCurrentPreviewPageIndex = 0;
        }

        public void SetMode(int mode)
        {
            _visualSplitMode = mode;
            ApplyVisualSplitMode();
        }

        private void ApplyVisualSplitMode()
        {
            _visualSplitCurrentPreviewPageIndex = 0;
            if (_visualSplitMode < 0 || _visualSplitMode > 2) _visualSplitMode = 0;
            switch (_visualSplitMode)
            {
                case 0:
                    _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
                    if (_visualSplitSegments.Count == 0 && _visualSplitTotalPages > 0)
                    {
                        _visualSplitCustomSegments.Add((1, _visualSplitTotalPages));
                        _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
                    }
                    _visualSplitSelectedSegmentIndex = _visualSplitSegments.Count > 0 ? 0 : -1;
                    break;
                case 1:
                    _visualSplitSegments.Clear();
                    _visualSplitCustomSegments.Clear();
                    for (int p = 1; p <= _visualSplitTotalPages; p++)
                    {
                        _visualSplitSegments.Add((p, p));
                        _visualSplitCustomSegments.Add((p, p));
                    }
                    _visualSplitSelectedSegmentIndex = _visualSplitSegments.Count > 0 ? 0 : -1;
                    break;
                case 2:
                    _visualSplitSegments.Clear();
                    _visualSplitCustomSegments.Clear();
                    int n = Math.Max(1, _visualSplitNPages);
                    for (int p = 1; p <= _visualSplitTotalPages; p += n)
                    {
                        int end = Math.Min(p + n - 1, _visualSplitTotalPages);
                        _visualSplitSegments.Add((p, end));
                        _visualSplitCustomSegments.Add((p, end));
                    }
                    _visualSplitSelectedSegmentIndex = _visualSplitSegments.Count > 0 ? 0 : -1;
                    break;
            }
        }

        public void AdjustN(int delta)
        {
            int n = Math.Clamp(_visualSplitNPages + delta, 1, _visualSplitTotalPages);
            if (n == _visualSplitNPages) return;
            _visualSplitNPages = n;
            ApplyVisualSplitMode();
        }

        public void SelectSegment(int index)
        {
            if (index >= 0 && index < _visualSplitSegments.Count)
            {
                _visualSplitSelectedSegmentIndex = index;
                _visualSplitCurrentPreviewPageIndex = 0;
            }
        }

        public void NavigatePreview(int delta)
        {
            if (_visualSplitSelectedSegmentIndex < 0 || _visualSplitSelectedSegmentIndex >= _visualSplitSegments.Count)
                return;

            var seg = _visualSplitSegments[_visualSplitSelectedSegmentIndex];
            int segCnt = seg.End - seg.Start + 1;
            _visualSplitCurrentPreviewPageIndex = Math.Clamp(_visualSplitCurrentPreviewPageIndex + delta, 0, segCnt - 1);
        }

        public void SplitAtCurrentPage()
        {
            if (_visualSplitSelectedSegmentIndex < 0 || _visualSplitSelectedSegmentIndex >= _visualSplitCustomSegments.Count)
                return;

            var seg = _visualSplitCustomSegments[_visualSplitSelectedSegmentIndex];
            int pageCnt = seg.End - seg.Start + 1;
            if (pageCnt <= 1) return;

            int previewIdx = Math.Max(0, Math.Min(_visualSplitCurrentPreviewPageIndex, pageCnt - 1));
            int splitPage = seg.Start + previewIdx;
            if (splitPage >= seg.End) return;

            _visualSplitMode = 0;
            var first = (seg.Start, splitPage);
            var second = (splitPage + 1, seg.End);

            _visualSplitCustomSegments.RemoveAt(_visualSplitSelectedSegmentIndex);
            _visualSplitCustomSegments.Insert(_visualSplitSelectedSegmentIndex, second);
            _visualSplitCustomSegments.Insert(_visualSplitSelectedSegmentIndex, first);
            _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
            _visualSplitCurrentPreviewPageIndex = 0;
        }

        public void AddSegment()
        {
            _visualSplitMode = 0;

            if (_visualSplitCustomSegments.Count == 0)
            {
                _visualSplitCustomSegments.Add((1, _visualSplitTotalPages));
                _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
                _visualSplitSelectedSegmentIndex = 0;
                _visualSplitCurrentPreviewPageIndex = 0;
                return;
            }

            var covered = new HashSet<int>();
            foreach (var s in _visualSplitCustomSegments)
                for (int p = s.Start; p <= s.End; p++)
                    covered.Add(p);

            int gapStart = -1, gapEnd = -1;
            for (int p = 1; p <= _visualSplitTotalPages; p++)
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

            _visualSplitCustomSegments.Add((gapStart, gapEnd));
            _visualSplitCustomSegments.Sort((a, b) => a.Start.CompareTo(b.Start));
            _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
            _visualSplitSelectedSegmentIndex = _visualSplitSegments.FindIndex(s => s.Start == gapStart && s.End == gapEnd);
            _visualSplitCurrentPreviewPageIndex = 0;
        }

        public void DeleteSegment()
        {
            if (_visualSplitCustomSegments.Count <= 1) return;
            if (_visualSplitSelectedSegmentIndex < 0 || _visualSplitSelectedSegmentIndex >= _visualSplitCustomSegments.Count)
                return;

            _visualSplitMode = 0;
            _visualSplitCustomSegments.RemoveAt(_visualSplitSelectedSegmentIndex);
            _visualSplitSegments = new List<(int, int)>(_visualSplitCustomSegments);
            if (_visualSplitSelectedSegmentIndex >= _visualSplitSegments.Count)
                _visualSplitSelectedSegmentIndex = _visualSplitSegments.Count - 1;
            _visualSplitCurrentPreviewPageIndex = 0;
        }

        public void ClearSegments()
        {
            _visualSplitMode = 0;
            _visualSplitCustomSegments.Clear();
            _visualSplitSegments.Clear();
            _visualSplitSelectedSegmentIndex = -1;
            _visualSplitCurrentPreviewPageIndex = 0;
        }

        public string GetSpec() =>
            PdfSplitProcessor.BuildSegmentSpec(_visualSplitMode, _visualSplitNPages, _visualSplitTotalPages, _visualSplitSegments);
    }

    /// <summary>
    /// 忠實模擬 WinUI Fluent VisualSplitterControl.xaml.cs 的狀態機與使用者互動邏輯。
    /// </summary>
    public sealed class FluentVisualSplitterDriver : IVisualSplitterDriver
    {
        private readonly int _totalPages;
        private int _mode;
        private int _nPages;
        private readonly List<(int Start, int End)> _segments = new();
        private readonly List<(int Start, int End)> _customSegments = new();
        private int _selectedSegmentIndex;
        private int _currentPreviewPageIndex;

        public int TotalPages => _totalPages;
        public int Mode => _mode;
        public int NPages => _nPages;
        public int SelectedSegmentIndex => _selectedSegmentIndex;
        public int CurrentPreviewPageIndex => _currentPreviewPageIndex;
        public IReadOnlyList<(int Start, int End)> Segments => _segments;
        public IReadOnlyList<(int Start, int End)> CustomSegments => _customSegments;

        public FluentVisualSplitterDriver(int totalPages)
        {
            if (totalPages <= 0) totalPages = 1;
            _totalPages = totalPages;
            _nPages = Math.Min(5, _totalPages);

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

            ApplyMode(0);
        }

        private void SelectCustomMode()
        {
            _mode = 0;
        }

        public void SetMode(int mode)
        {
            ApplyMode(mode);
        }

        private void ApplyMode(int mode)
        {
            _mode = mode;
            _currentPreviewPageIndex = 0;

            switch (mode)
            {
                case 1:
                    _segments.Clear();
                    _customSegments.Clear();
                    for (int p = 1; p <= _totalPages; p++)
                    {
                        _segments.Add((p, p));
                        _customSegments.Add((p, p));
                    }
                    break;
                case 2:
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
                default:
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
        }

        public void AdjustN(int delta)
        {
            int n = Math.Clamp(_nPages + delta, 1, _totalPages);
            if (n == _nPages) return;
            _nPages = n;
            if (_mode == 2) ApplyMode(2);
        }

        public void SelectSegment(int index)
        {
            if (index < 0 || index >= _segments.Count) return;
            _selectedSegmentIndex = index;
            _currentPreviewPageIndex = 0;
        }

        public void NavigatePreview(int delta)
        {
            if (_selectedSegmentIndex < 0 || _selectedSegmentIndex >= _segments.Count) return;
            var seg = _segments[_selectedSegmentIndex];
            int pageCnt = seg.End - seg.Start + 1;
            _currentPreviewPageIndex = Math.Clamp(_currentPreviewPageIndex + delta, 0, pageCnt - 1);
        }

        public void SplitAtCurrentPage()
        {
            if (_selectedSegmentIndex < 0 || _selectedSegmentIndex >= _customSegments.Count) return;

            var seg = _customSegments[_selectedSegmentIndex];
            int pageCnt = seg.End - seg.Start + 1;
            if (pageCnt <= 1) return;

            int previewIdx = Math.Clamp(_currentPreviewPageIndex, 0, pageCnt - 1);
            int splitPage = seg.Start + previewIdx;
            if (splitPage >= seg.End) return;

            var first = (seg.Start, splitPage);
            var second = (splitPage + 1, seg.End);

            _customSegments.RemoveAt(_selectedSegmentIndex);
            _customSegments.Insert(_selectedSegmentIndex, second);
            _customSegments.Insert(_selectedSegmentIndex, first);
            _segments.Clear();
            _segments.AddRange(_customSegments);
            _currentPreviewPageIndex = 0;

            _mode = 0;
            SelectCustomMode();
        }

        public void AddSegment()
        {
            SelectCustomMode();

            if (_customSegments.Count == 0)
            {
                _customSegments.Add((1, _totalPages));
                _segments.Clear();
                _segments.AddRange(_customSegments);
                _selectedSegmentIndex = 0;
                _currentPreviewPageIndex = 0;
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
        }

        public void DeleteSegment()
        {
            if (_customSegments.Count <= 1) return;
            if (_selectedSegmentIndex < 0 || _selectedSegmentIndex >= _customSegments.Count) return;

            SelectCustomMode();

            _customSegments.RemoveAt(_selectedSegmentIndex);
            _segments.Clear();
            _segments.AddRange(_customSegments);
            if (_selectedSegmentIndex >= _segments.Count)
                _selectedSegmentIndex = _segments.Count - 1;
            _currentPreviewPageIndex = 0;
        }

        public void ClearSegments()
        {
            SelectCustomMode();
            _customSegments.Clear();
            _segments.Clear();
            _selectedSegmentIndex = -1;
            _currentPreviewPageIndex = 0;
        }

        public string GetSpec() =>
            PdfSplitProcessor.BuildSegmentSpec(_mode, _nPages, _totalPages, _segments);
    }

    public sealed record GoldenTestCase(
        string Name,
        int TotalPages,
        Action<IVisualSplitterDriver> Script,
        string ExpectedSpec
    );

    public static void RegisterVisualSplitterParityTests(TestRunner runner)
    {
        var goldenCases = new List<GoldenTestCase>
        {
            new("Init halves: 10 pages", 10, _ => { }, "1-5; 6-10"),
            new("Init odd: 7 pages", 7, _ => { }, "1-3; 4-7"),
            new("Init single: 1 page", 1, _ => { }, "1"),
            new("Init pair: 2 pages", 2, _ => { }, "1; 2"),
            new("Init clamp non-positive: 0 pages clamped to 1", 0, _ => { }, "1"),
            new("Switch mode: split every page on 5 pages", 5, d => d.SetMode(1), "all"),
            new("Fixed pages: default N=5 on 10 pages", 10, d => d.SetMode(2), "1-5; 6-10"),
            new("Fixed pages: step down N from 5 to 4 on 10 pages", 10, d =>
            {
                d.SetMode(2);
                d.AdjustN(-1);
            }, "1-4; 5-8; 9-10"),
            new("Fixed pages: step down N from 5 to 3 on 10 pages", 10, d =>
            {
                d.SetMode(2);
                d.AdjustN(-1);
                d.AdjustN(-1);
            }, "1-3; 4-6; 7-9; 10"),
            new("Fixed pages: step down to minimum floor N=1 on 6 pages", 6, d =>
            {
                d.SetMode(2);
                for (int i = 0; i < 10; i++) d.AdjustN(-1);
            }, "1; 2; 3; 4; 5; 6"),
            new("Fixed pages: step up to maximum ceiling on 7 pages", 7, d =>
            {
                d.SetMode(2);
                for (int i = 0; i < 20; i++) d.AdjustN(+1);
            }, "1-7"),
            new("Navigate preview and split at page 3 on 10 pages", 10, d =>
            {
                d.NavigatePreview(+2);
                d.SplitAtCurrentPage();
            }, "1-3; 4-5; 6-10"),
            new("Repeated split on consecutive segments", 10, d =>
            {
                d.NavigatePreview(+2);
                d.SplitAtCurrentPage(); // 1-3, 4-5, 6-10 (selected: 0)
                d.SelectSegment(1);    // 4-5
                d.SplitAtCurrentPage(); // 1-3, 4, 5, 6-10
            }, "1-3; 4; 5; 6-10"),
            new("Split on single-page segment is safe no-op", 10, d =>
            {
                d.NavigatePreview(+2);
                d.SplitAtCurrentPage();
                d.SelectSegment(1);
                d.SplitAtCurrentPage(); // 4 is now a 1-page segment
                d.SplitAtCurrentPage(); // split again on 1-page segment
            }, "1-3; 4; 5; 6-10"),
            new("Split at trailing boundary of segment is safe no-op", 10, d =>
            {
                d.NavigatePreview(+10); // clamped to page 5 (last page of 1-5)
                d.SplitAtCurrentPage();
            }, "1-5; 6-10"),
            new("Delete first segment when multiple exist", 10, d =>
            {
                d.DeleteSegment();
            }, "6-10"),
            new("Delete segment when only 1 exists is safe no-op", 10, d =>
            {
                d.DeleteSegment();
                d.DeleteSegment();
            }, "6-10"),
            new("Delete middle segment and add segment fills lowest gap", 10, d =>
            {
                d.NavigatePreview(+2);
                d.SplitAtCurrentPage(); // 1-3, 4-5, 6-10
                d.DeleteSegment();      // deletes 1-3 -> 4-5, 6-10
                d.AddSegment();         // fills gap 1-3 -> 1-3, 4-5, 6-10
            }, "1-3; 4-5; 6-10"),
            new("Add segment when fully covered is safe no-op", 10, d =>
            {
                d.AddSegment();
            }, "1-5; 6-10"),
            new("Clear segments produces all spec", 10, d =>
            {
                d.ClearSegments();
            }, "all"),
            new("Clear segments then add segment restores full document", 10, d =>
            {
                d.ClearSegments();
                d.AddSegment();
            }, "1-10"),
            new("Fixed mode split automatically switches to custom mode", 12, d =>
            {
                d.SetMode(2);          // N=5 -> 1-5, 6-10, 11-12
                d.AdjustN(-1);         // N=4 -> 1-4, 5-8, 9-12
                d.SelectSegment(1);    // 5-8
                d.NavigatePreview(+1); // page 6
                d.SplitAtCurrentPage();// 5-8 splits at 6 -> 5-6, 7-8
            }, "1-4; 5-6; 7-8; 9-12"),
            new("Split each mode split on 1-page segment is safe no-op staying in each mode", 4, d =>
            {
                d.SetMode(1);          // all
                d.SplitAtCurrentPage();// single page cannot split, stays in mode 1
            }, "all"),
            new("Clear then set mode 1 re-creates every page", 3, d =>
            {
                d.ClearSegments();
                d.SetMode(1);
            }, "all"),
            new("Clear then set mode 2 re-creates fixed segments", 8, d =>
            {
                d.ClearSegments();
                d.AdjustN(-2);         // N=3
                d.SetMode(2);
            }, "1-3; 4-6; 7-8"),
            new("Out-of-bounds selection is rejected without state corruption", 10, d =>
            {
                d.SelectSegment(99);
                d.SelectSegment(-1);
            }, "1-5; 6-10"),
            new("Complex multi-step sequence parity", 15, d =>
            {
                d.SetMode(2);          // 1-5, 6-10, 11-15
                d.AdjustN(-2);         // N=3: 1-3, 4-6, 7-9, 10-12, 13-15
                d.SelectSegment(2);    // 7-9
                d.NavigatePreview(+1); // page 8
                d.SplitAtCurrentPage();// 7-8, 9
                d.SelectSegment(0);    // 1-3
                d.DeleteSegment();     // removes 1-3
                d.AddSegment();        // re-adds gap 1-3
            }, "1-3; 4-6; 7-8; 9; 10-12; 13-15")
        };

        foreach (var tc in goldenCases)
        {
            runner.Run($"Visual splitter parity: {tc.Name}", () =>
            {
                var cli = new CliVisualSplitterDriver(tc.TotalPages);
                var fluent = new FluentVisualSplitterDriver(tc.TotalPages);

                tc.Script(cli);
                tc.Script(fluent);

                string cliSpec = cli.GetSpec();
                string fluentSpec = fluent.GetSpec();

                Assert.Equal(tc.ExpectedSpec, cliSpec);
                Assert.Equal(tc.ExpectedSpec, fluentSpec);
                Assert.Equal(cliSpec, fluentSpec);
                Assert.Equal(cli.Mode, fluent.Mode);
                Assert.Equal(cli.Segments.Count, fluent.Segments.Count);
                Assert.Equal(cli.SelectedSegmentIndex, fluent.SelectedSegmentIndex);
                Assert.Equal(cli.CurrentPreviewPageIndex, fluent.CurrentPreviewPageIndex);
            });
        }

        runner.Run("Visual splitter source code contracts enforce parity invariants", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

            string cliControlsPath = Path.Combine(root, "src", "Clickra.CLI", "Progress", "ProgressWindow.Controls.cs");
            string cliSplitterPath = Path.Combine(root, "src", "Clickra.CLI", "Progress", "ProgressWindow.VisualSplitter.cs");
            string fluentSplitterPath = Path.Combine(root, "src", "Clickra.Fluent", "Controls", "VisualSplitterControl.xaml.cs");

            Assert.True(File.Exists(cliControlsPath), "CLI Controls file must exist.");
            Assert.True(File.Exists(cliSplitterPath), "CLI VisualSplitter file must exist.");
            Assert.True(File.Exists(fluentSplitterPath), "Fluent VisualSplitter file must exist.");

            string cliControls = File.ReadAllText(cliControlsPath);
            string cliSplitter = File.ReadAllText(cliSplitterPath);
            string fluentSplitter = File.ReadAllText(fluentSplitterPath);

            // 1. Both must use floor 1 for N stepper
            Assert.True(cliControls.Contains("Math.Max(1, _visualSplitNPages - 1)"),
                "CLI must allow stepping down N to 1 (floor 1).");
            Assert.False(cliControls.Contains("Math.Max(2, _visualSplitNPages - 1)"),
                "CLI must not clamp N to floor 2.");
            Assert.True(fluentSplitter.Contains("Math.Clamp(_nPages + delta, 1, _totalPages)"),
                "Fluent must clamp N stepper to [1, totalPages].");

            // 2. Both must protect against splitting at trailing boundary (splitPage >= seg.End)
            Assert.True(cliSplitter.Contains("if (splitPage >= seg.End) return;"),
                "CLI splitter must guard against splitting at segment end.");
            Assert.True(fluentSplitter.Contains("if (splitPage >= seg.End) return;"),
                "Fluent splitter must guard against splitting at segment end.");

            // 3. Both must delegate spec generation to PdfSplitProcessor.BuildSegmentSpec
            Assert.True(cliSplitter.Contains("PdfSplitProcessor.BuildSegmentSpec"),
                "CLI splitter must delegate spec generation to PdfSplitProcessor.BuildSegmentSpec.");
            Assert.True(fluentSplitter.Contains("PdfSplitProcessor.BuildSegmentSpec"),
                "Fluent splitter must delegate spec generation to PdfSplitProcessor.BuildSegmentSpec.");
        });
    }
}

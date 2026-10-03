using System;
using System.Collections.Generic;
using System.IO;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

/// <summary>
/// Golden behavior table for the shared production visual-splitter state model.
/// CLI and Fluent parity is guaranteed by both UI surfaces delegating state transitions to this model.
/// </summary>
static partial class TestSuite
{
    private const string TenPageHalvesSpec = "1-5; 6-10";

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

    public sealed class ProductionVisualSplitterDriver : IVisualSplitterDriver
    {
        private readonly VisualSplitModel _model;

        public ProductionVisualSplitterDriver(int totalPages) => _model = new VisualSplitModel(totalPages);

        public int TotalPages => _model.TotalPages;
        public int Mode => _model.Mode;
        public int NPages => _model.PagesPerSegment;
        public int SelectedSegmentIndex => _model.SelectedSegmentIndex;
        public int CurrentPreviewPageIndex => _model.PreviewPageIndex;
        public IReadOnlyList<(int Start, int End)> Segments => _model.Segments;
        public IReadOnlyList<(int Start, int End)> CustomSegments => _model.CustomSegments;
        public string GetSpec() => _model.BuildSpec();

        public void SetMode(int mode) => _model.SetMode(mode);
        public void AdjustN(int delta) => _model.AdjustPagesPerSegment(delta);
        public void SelectSegment(int index) => _model.SelectSegment(index);
        public void NavigatePreview(int delta) => _model.NavigatePreview(delta);
        public void SplitAtCurrentPage() => _model.SplitSelectedAtPreviewPage();
        public void AddSegment() => _model.AddSegment();
        public void DeleteSegment() => _model.DeleteSelectedSegment();
        public void ClearSegments() => _model.ClearSegments();
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
            new("Init halves: 10 pages", 10, _ => { }, TenPageHalvesSpec),
            new("Init odd: 7 pages", 7, _ => { }, "1-3; 4-7"),
            new("Init single: 1 page", 1, _ => { }, "1"),
            new("Init pair: 2 pages", 2, _ => { }, "1; 2"),
            new("Init clamp non-positive: 0 pages clamped to 1", 0, _ => { }, "1"),
            new("Switch mode: split every page on 5 pages", 5, d => d.SetMode(1), "all"),
            new("Fixed pages: default N=5 on 10 pages", 10, d => d.SetMode(2), TenPageHalvesSpec),
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
            }, TenPageHalvesSpec),
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
            }, TenPageHalvesSpec),
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
            }, TenPageHalvesSpec),
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
            runner.Run($"Visual splitter production model: {tc.Name}", () =>
            {
                var driver = new ProductionVisualSplitterDriver(tc.TotalPages);

                tc.Script(driver);

                Assert.Equal(tc.ExpectedSpec, driver.GetSpec());
                Assert.True(driver.Mode is >= VisualSplitModel.ModeCustom and <= VisualSplitModel.ModeFixedPages,
                    "Production model mode must stay inside the supported range.");
            });
        }

        runner.Run("Visual splitter production model: no-gap add still switches fixed mode to custom", () =>
        {
            var driver = new ProductionVisualSplitterDriver(10);
            driver.SetMode(VisualSplitModel.ModeFixedPages);

            driver.AddSegment();

            Assert.Equal(VisualSplitModel.ModeCustom, driver.Mode);
            Assert.Equal(TenPageHalvesSpec, driver.GetSpec());
        });

        runner.Run("Visual splitter production zoom: clamp and step contract", () =>
        {
            Assert.True(Math.Abs(VisualSplitModel.ClampZoomFactor(0.25f) - VisualSplitModel.ZoomMinFactor) < 0.0001f,
                "Zoom must not go below fit.");
            Assert.True(Math.Abs(VisualSplitModel.ClampZoomFactor(99f) - VisualSplitModel.ZoomMaxFactor) < 0.0001f,
                "Zoom must not exceed the maximum factor.");
            Assert.True(Math.Abs(VisualSplitModel.ZoomFactorAfter(1f, +1) - 1.25f) < 0.0001f,
                "One zoom-in step must multiply by 1.25.");
            Assert.True(Math.Abs(VisualSplitModel.ZoomFactorAfter(1.25f, -1) - 1f) < 0.0001f,
                "One zoom-out step must return 1.25x to fit.");
            Assert.Equal(125, VisualSplitModel.ZoomPercent(1.25f));
        });

        runner.Run("Visual splitter production zoom: render width follows clamped factor", () =>
        {
            Assert.Equal(660, VisualSplitModel.ZoomRenderWidth(1f, 660, 660, 1500));
            Assert.Equal(1320, VisualSplitModel.ZoomRenderWidth(2f, 660, 660, 1500));
            Assert.Equal(1500, VisualSplitModel.ZoomRenderWidth(8f, 660, 660, 1500));
            Assert.Equal(660, VisualSplitModel.ZoomRenderWidth(0.25f, 660, 660, 1500));
        });
        runner.RunGuard("Visual splitter UI surfaces delegate parity state to production model", () =>
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

            string[] cliModelCalls =
            {
                "new VisualSplitModel(totalPages)",
                "_visualSplitModel.SetMode(",
                "_visualSplitModel.AdjustPagesPerSegment(",
                "_visualSplitModel.SelectSegment(",
                "_visualSplitModel.NavigatePreview(",
                "_visualSplitModel.SplitSelectedAtPreviewPage()",
                "_visualSplitModel.AddSegment()",
                "_visualSplitModel.DeleteSelectedSegment()",
                "_visualSplitModel.ClearSegments()",
                "_visualSplitModel.BuildSpec()"
            };
            foreach (string call in cliModelCalls)
            {
                Assert.True(cliControls.Contains(call) || cliSplitter.Contains(call),
                    $"CLI visual splitter must delegate production state through {call}.");
            }

            string[] fluentModelCalls =
            {
                "new VisualSplitModel(_totalPages)",
                "_splitModel.SetMode(",
                "_splitModel.AdjustPagesPerSegment(",
                "_splitModel.SelectSegment(",
                "_splitModel.NavigatePreview(",
                "_splitModel.SplitSelectedAtPreviewPage()",
                "_splitModel.AddSegment()",
                "_splitModel.DeleteSelectedSegment()",
                "_splitModel.ClearSegments()",
                "_splitModel.BuildSpec()"
            };
            foreach (string call in fluentModelCalls)
            {
                Assert.True(fluentSplitter.Contains(call),
                    $"Fluent visual splitter must delegate production state through {call}.");
            }

            Assert.True(cliControls.Contains("VisualSplitModel.ZoomFactorAfter(", StringComparison.Ordinal),
                "CLI zoom stepping must delegate to the shared production zoom contract.");
            Assert.True(cliSplitter.Contains("VisualSplitModel.ClampZoomFactor(", StringComparison.Ordinal),
                "CLI zoom clamping must delegate to the shared production zoom contract.");
            Assert.True(fluentSplitter.Contains("VisualSplitModel.ZoomFactorAfter(", StringComparison.Ordinal),
                "Fluent zoom stepping must delegate to the shared production zoom contract.");
            Assert.True(fluentSplitter.Contains("VisualSplitModel.ClampZoomFactor(", StringComparison.Ordinal),
                "Fluent zoom clamping must delegate to the shared production zoom contract.");
            Assert.True(fluentSplitter.Contains("VisualSplitModel.ZoomRenderWidth(", StringComparison.Ordinal),
                "Fluent preview resolution must delegate to the shared production zoom contract.");
        });
    }
}

using Clickra.Core.Modules.Pdf.Print.Application;

namespace Clickra.Core.Tests;

/// <summary>
/// Print batch totals shown under the desktop print preview (docs/specs/print_preview_plan.md, sections 5 and 7):
/// each file is laid out on its own, starts on fresh paper and is counted on its own when printing two-sided.
/// </summary>
static partial class TestSuite
{
    private static IReadOnlyList<PrintPageSize> BatchPages(int count, bool landscape) =>
        Enumerable.Range(1, count)
            .Select(n => landscape ? new PrintPageSize(n, 842f, 595f) : new PrintPageSize(n, 595f, 842f))
            .ToList();

    private static IReadOnlyList<(string Name, IReadOnlyList<PrintPageSize> AllPages)> ThreeBatchFiles() => new[]
    {
        ("form.pdf", BatchPages(1, landscape: true)),
        ("report.pdf", BatchPages(12, landscape: false)),
        ("receipt.pdf", BatchPages(2, landscape: false)),
    };

    private static PrintPdfOptions BatchOptions(string? range = null, string duplex = "Simplex", int copies = 1)
    {
        var dict = new Dictionary<string, object>
        {
            [PrintPdfOptions.DuplexKey] = duplex,
            [PrintPdfOptions.CopiesKey] = copies,
        };
        if (range is not null) dict[PrintPdfOptions.PageRangeKey] = range;
        return PrintPdfOptions.FromDictionary(dict);
    }

    public static void RegisterPrintBatchTests(TestRunner runner)
    {
        runner.Run("Print batch: one-sided, each file counts its own sheets", () =>
        {
            var plan = PrintBatchPlanner.Plan(ThreeBatchFiles(), 595.28f, 841.89f, BatchOptions());
            Assert.Equal(1, plan.Files[0].Sheets.Count);
            Assert.Equal(12, plan.Files[1].Sheets.Count);
            Assert.Equal(2, plan.Files[2].Sheets.Count);
            Assert.True(plan.Files[0].Sheets[0].Landscape, "The landscape form prints on landscape paper under Auto.");
            Assert.Equal(15, plan.TotalSelectedPages);
            Assert.Equal(15, plan.TotalPaper);
            Assert.Equal(0, plan.SkippedNames.Count);
        });

        runner.Run("Print batch: two-sided paper is counted per file", () =>
        {
            var plan = PrintBatchPlanner.Plan(ThreeBatchFiles(), 595.28f, 841.89f, BatchOptions(duplex: "DuplexLongEdge"));
            Assert.Equal(1, plan.Files[0].PaperCount);
            Assert.Equal(6, plan.Files[1].PaperCount);
            Assert.Equal(1, plan.Files[2].PaperCount);
            Assert.Equal(8, plan.TotalPaper);

            var first = PrintBatchPlanner.DescribeSide(plan.Files[1].Sheets, 0, PrintDuplexMode.DuplexLongEdge);
            var second = PrintBatchPlanner.DescribeSide(plan.Files[1].Sheets, 1, PrintDuplexMode.DuplexLongEdge);
            var last = PrintBatchPlanner.DescribeSide(plan.Files[1].Sheets, 11, PrintDuplexMode.DuplexLongEdge);
            Assert.True(first == new PrintSidePosition(1, 6, false), $"Side 1 should be sheet 1 front of 6, got {first}.");
            Assert.True(second == new PrintSidePosition(1, 6, true), $"Side 2 should be sheet 1 back of 6, got {second}.");
            Assert.True(last == new PrintSidePosition(6, 6, true), $"Side 12 should be sheet 6 back of 6, got {last}.");
        });

        runner.Run("Print batch: one-sided sides are numbered as sheets", () =>
        {
            var plan = PrintBatchPlanner.Plan(ThreeBatchFiles(), 595.28f, 841.89f, BatchOptions());
            var side = PrintBatchPlanner.DescribeSide(plan.Files[1].Sheets, 3, PrintDuplexMode.Simplex);
            Assert.True(side == new PrintSidePosition(4, 12, false), $"Side 4 should be sheet 4 of 12, got {side}.");
        });

        runner.Run("Print batch: a page range that misses a file skips it", () =>
        {
            var plan = PrintBatchPlanner.Plan(ThreeBatchFiles(), 595.28f, 841.89f, BatchOptions(range: "3-5"));
            Assert.True(plan.Files[0].Skipped && plan.Files[2].Skipped, "form.pdf (1 page) and receipt.pdf (2 pages) have no page 3-5.");
            Assert.False(plan.Files[1].Skipped, "report.pdf prints pages 3-5.");
            Assert.Equal(3, plan.Files[1].SelectedPages.Count);
            Assert.Equal(3, plan.Files[1].SelectedPages[0].PageNumber);
            Assert.Equal(2, plan.SkippedNames.Count);
            Assert.Equal("form.pdf", plan.SkippedNames[0]);
            Assert.Equal("receipt.pdf", plan.SkippedNames[1]);
            Assert.Equal(3, plan.TotalSelectedPages);
            Assert.Equal(3, plan.TotalPaper);
        });

        runner.Run("Print batch: copies multiply the paper", () =>
        {
            var plan = PrintBatchPlanner.Plan(ThreeBatchFiles(), 595.28f, 841.89f, BatchOptions(duplex: "DuplexLongEdge", copies: 2));
            Assert.Equal(16, plan.TotalPaper);
            Assert.Equal(15, plan.TotalSelectedPages);
        });
    }
}

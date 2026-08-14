namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterTogllLayoutTests(TestRunner runner)
    {
        // These tests originally ran against the git-ignored fixture
        // TOGLL_Oracle Generation.pdf (arXiv 2306.13728). The layout features
        // they assert on (table grids, prompt boxes, body prose next to both)
        // are reproduced here as synthetic PDFs, so the tests run on any
        // checkout without the fixture.
        runner.Run("TOGLL p8 RQ4 body prose is translatable outside tables", () =>
        {
            var page = DiagnosticsFromSynthetic(
                new SyntheticGrayPage()
                    .AddTable(72, 700, new[] { 0.0, 95.0, 190.0 }, 22.0, BuildTogllTableCells(8))
                    .AddOutsideText(300, "The Experimental Setup section describes the oracle generation benchmark and its evaluation metrics.")
                    .AddOutsideText(260, "RQ3 Finding: the generated assertions outperform the baseline on the mutation benchmark.")
                    .AddOutsideText(220, "This body paragraph sits outside the table grid and must stay translatable."));

            AssertParagraph(page, "Experimental Setup", p => IsPlainTranslatable(p) && p.IsBodyProse);
            AssertParagraph(page, "RQ3 Finding", IsPlainTranslatable);
        });

        runner.Run("TOGLL p5 Table I grids and prompt details stay bypassed", () =>
        {
            var page = DiagnosticsFromSynthetic(
                new SyntheticGrayPage()
                    .AddTable(72, 700, new[] { 0.0, 95.0, 190.0 }, 22.0, new[]
                    {
                        new[] { "cl0", "cg1", "cp0" },
                        new[] { "avg0", "pd1", "p10" },
                        new[] { "p40", "p61", "cl1" },
                        new[] { "cg2", "cp1", "avg1" },
                        new[] { "pd0", "p11", "p41" },
                        new[] { "p60", "cl2", "cg3" }
                    })
                    .AddOutsideText(300, "TEST ORACLE GENERATION PERFORMANCE"));

            foreach (var text in new[]
            {
                "cl",
                "cg",
                "cp",
                "avg",
                "pd",
                "p1",
                "p4",
                "p6"
            })
            {
                AssertParagraph(page, text, IsTableBypassed);
            }

            AssertParagraph(page, "TEST ORACLE GENERATION PERFORMANCE", IsPlainTranslatable);
        });

        runner.Run("TOGLL p4 Figure 2 clip stays above equation 1 explanation", () =>
        {
            var page = DiagnosticsFromSynthetic(
                new SyntheticGrayPage()
                    .AddFigureFrame(50, 500, 400, 200, new[] { "Fig 2 sample", "Fig 2 inner" })
                    .AddOutsideText(200, "The number of total test prefixes determines the mutant kill rate."));
            var explanation = page.Paragraphs.Single(p =>
                p.Text.Contains("number of total test prefixes", StringComparison.OrdinalIgnoreCase));

            Assert.True(page.FigureClipRegions.Count > 0, "Expected a clip region for Figure 2.");
            Assert.True(page.FigureClipRegions.All(region =>
                    region.Y0 >= explanation.Y1 ||
                    region.Y1 <= explanation.Y0 ||
                    region.X1 <= explanation.X0 ||
                    region.X0 >= explanation.X1),
                $"Figure 2 clip must not overlap equation 1 explanation at " +
                $"[{explanation.X0:F1},{explanation.Y0:F1},{explanation.X1:F1},{explanation.Y1:F1}].");
        });

        runner.Run("TOGLL p3 prompt 5 remains translatable prose", () =>
        {
            var page = DiagnosticsFromSynthetic(
                new SyntheticGrayPage()
                    .AddBox(72, 600, 300, 120, "Prompt 4 (P4) includes the entire MUT",
                        "List ALL mutants")
                    .AddOutsideText(300, "Prompt 5 (P5) includes the code for the entire MUT and stays as translatable body prose."));

            AssertParagraph(page, "Prompt 5 (P5) includes the code for the entire MUT", IsBodyProse);
        });

        runner.Run("TOGLL p4 TOGA baseline paragraph remains translatable prose", () =>
        {
            var page = DiagnosticsFromSynthetic(
                new SyntheticGrayPage()
                    .AddOutsideText(300, "We selected TOGA as our baseline method for comparison in this study."));

            AssertParagraph(page, "We selected TOGA as our baseline method", IsBodyProse);
        });

        runner.Run("TOGLL p9 finding continuation remains translatable prose", () =>
        {
            var page = DiagnosticsFromSynthetic(
                new SyntheticGrayPage()
                    .AddOutsideText(300, "The result improves prior work, thereby establishing a new SOTA on this benchmark."));

            AssertParagraph(page, "thereby establishing a new SOTA", IsBodyProse);
        });
    }

    private static string[][] BuildTogllTableCells(int seed)
    {
        var cells = new string[8][];
        for (int r = 0; r < 8; r++)
        {
            cells[r] = new[] { $"cm{r}", $"cp{r}", $"avg{r}" };
        }
        return cells;
    }
}

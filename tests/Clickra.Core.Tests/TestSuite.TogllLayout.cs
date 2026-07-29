namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterTogllLayoutTests(TestRunner runner)
    {
        runner.Run("TOGLL p8 RQ4 body prose is translatable outside tables", () =>
        {
            var page = Diagnostics("TOGLL_Oracle Generation.pdf", 8);

            AssertParagraph(page, "Experimental Setup", p =>
                !p.IsTable && !p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent && p.IsBodyProse);
            AssertParagraph(page, "RQ3 Finding", p =>
                !p.IsTable && !p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent);
        });

        runner.Run("TOGLL p5 Table I grids and prompt details stay bypassed", () =>
        {
            var page = Diagnostics("TOGLL_Oracle Generation.pdf", 5);

            foreach (var text in new[]
            {
                "Code LLM",
                "CodeGPT-110M",
                "CodeParrot-110M",
                "Avg:",
                "Prompt Details",
                "P1: prefix",
                "P4: prefix + [sep] + doc. + [sep] + mutsig",
                "P6: prefix + [sep] + doc. + [sep] + mut"
            })
            {
                AssertParagraph(page, text, p =>
                    p.IsTable && p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent);
            }

            AssertParagraph(page, "TEST ORACLE GENERATION PERFORMANCE", p =>
                !p.IsTable && !p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent);
        });

        runner.Run("TOGLL p4 Figure 2 clip stays above equation 1 explanation", () =>
        {
            var page = Diagnostics("TOGLL_Oracle Generation.pdf", 4);
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
            var page = Diagnostics("TOGLL_Oracle Generation.pdf", 3);

            AssertParagraph(page, "Prompt 5 (P5) includes the code for the entire MUT", p =>
                !p.IsBypassed && !p.IsCode && !p.IsDiagram && p.IsBodyProse);
        });

        runner.Run("TOGLL p4 TOGA baseline paragraph remains translatable prose", () =>
        {
            var page = Diagnostics("TOGLL_Oracle Generation.pdf", 4);

            AssertParagraph(page, "We selected TOGA as our baseline method", p =>
                !p.IsBypassed && !p.IsCode && !p.IsDiagram && p.IsBodyProse);
        });

        runner.Run("TOGLL p9 finding continuation remains translatable prose", () =>
        {
            var page = Diagnostics("TOGLL_Oracle Generation.pdf", 9);

            AssertParagraph(page, "thereby establishing a new SOTA", p =>
                !p.IsBypassed && !p.IsCode && !p.IsDiagram && p.IsBodyProse);
        });
    }
}

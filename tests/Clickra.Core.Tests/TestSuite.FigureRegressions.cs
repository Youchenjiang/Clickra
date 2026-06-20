using Clickra.Core.Processors;

static partial class TestSuite
{
    public static void RegisterFigureRegressionTests(TestRunner runner)
    {
        runner.Run("Pentest p9 Figure 7 bar chart stays inside the original figure", () =>
        {
            var page = Diagnostics("PentestAgent_Agent Pentest.pdf", 9);

            foreach (var text in new[]
            {
                "Success Rate (%)",
                "GPT-4",
                "GPT-3.5",
                "Models"
            })
            {
                AssertParagraph(page, text, p => p.IsBypassed);
            }

            AssertParagraph(page, "Success rate on penetration testing tasks", p =>
                !p.IsDiagram && !p.IsBypassed && !p.IsTable);
        });

        runner.Run("Pentest Figure 7 success-rate axis label is chart protected", () =>
        {
            var method = typeof(PdfTranslateProcessor).Assembly
                .GetType("Clickra.Core.Processors.PdfTranslationPipeline")
                ?.GetMethod(
                "IsLikelyBarChartAxisLabel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.True(method != null, "Expected bar-chart axis label helper.");

            var axis = UninitializedParagraph("Success Rate (%) 60", width: 55, height: 35);
            Assert.True((bool)method!.Invoke(null, new object[] { axis })!,
                "Expected merged Success Rate (%) y-axis label to be treated as a chart label.");
        });

        runner.Run("TOGLL p8 Figure 4 source code stays inside the original figure", () =>
        {
            var page = Diagnostics("TOGLL_Oracle Generation.pdf", 8);
            foreach (var text in new[]
            {
                "public void test3",
                "assertSame(oA1, oA0)",
                "public void test9",
                "assertEquals((-119.4)",
                "public void test14",
                "Ground Truth"
            })
            {
                AssertParagraph(page, text, p =>
                    p.IsDiagram && p.IsBypassed && !p.IsTable && !p.IsGrayPromptContent);
            }
            AssertParagraph(page, "Diverse yet correct test oracles", p =>
                !p.IsDiagram && !p.IsBypassed && !p.IsTable);
        });

        runner.Run("TOGLL p9 Figure 5 source code stays inside the original figure", () =>
        {
            var page = Diagnostics("TOGLL_Oracle Generation.pdf", 9);
            foreach (var text in new[]
            {
                "calculatePrintedLength",
                "public void test327",
                "l0.getVariant",
                "public Rad toRad",
                "Null Return",
                "public void test13",
                "angle_Rad1"
            })
            {
                AssertParagraph(page, text, p =>
                    p.IsDiagram && p.IsBypassed && !p.IsTable && !p.IsGrayPromptContent);
            }
            AssertParagraph(page, "TOGLL generated assertions detecting unique mutants", p =>
                !p.IsDiagram && !p.IsBypassed && !p.IsTable);
        });
    }
}

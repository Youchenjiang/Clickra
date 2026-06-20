using Clickra.Core.Processors;

static partial class TestSuite
{
    public static void RegisterFinalProjectTests(TestRunner runner)
    {
        runner.Run("Final project p14 Work Division cells are tables, not diagrams", () =>
        {
            var page = Diagnostics("114423046_final_project.pdf", 14);

            Assert.True(page.TableCount >= 30,
                $"Expected Work Division tableCount >= 30, got {page.TableCount}.");

            foreach (var text in new[]
            {
                "workflow design",
                "Embedding topic",
                "Attention build up",
                "attention and SHAP"
            })
            {
                AssertParagraph(page, text, p => p.IsTable && p.IsBypassed && !p.IsDiagram);
            }
        });

        runner.Run("Final project appendix feature tables stay bypassed", () =>
        {
            var page15 = Diagnostics("114423046_final_project.pdf", 15);
            foreach (var text in new[] { "TV_SHORT)", "UNKNOWN_SOURCE", "VIDEO_GAME" })
            {
                AssertParagraph(page15, text, p =>
                    p.IsTable && p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent);
            }

            var page16 = Diagnostics("114423046_final_project.pdf", 16);
            foreach (var text in new[]
            {
                "titles from the retrieved title's",
                "retrieved studio score",
                "Jaccard"
            })
            {
                AssertParagraph(page16, text, p =>
                    p.IsTable && p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent);
            }
        });

        runner.Run("Final project p1 translated title clip follows rendered CJK height", () =>
        {
            var method = typeof(PdfTranslateProcessor).Assembly
                .GetType("Clickra.Core.Processors.PageOneLayoutClassifier")
                ?.GetMethod(
                "GetTitleClipBottom",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.True(method != null, "Expected page-one title clip helper.");

            const double clipTop = 56.2;
            const double originalClipBottom = 72.3;
            const double measuredHeight = 24.3;
            double clipBottom = (double)method!.Invoke(
                null, new object[] { clipTop, originalClipBottom, measuredHeight })!;

            Assert.True(clipBottom >= clipTop + measuredHeight,
                $"Expected title clip to contain the measured height, got {clipBottom - clipTop:F1}.");
        });

        runner.Run("Final project p9 table reference labels do not bypass later result pages", () =>
        {
            var page9 = Diagnostics("114423046_final_project.pdf", 9);
            AssertParagraph(page9, "structures from existing studies", p =>
                !p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);

            var page10 = Diagnostics("114423046_final_project.pdf", 10);
            AssertParagraph(page10, "This experiment studies which parts of CARMA matter", p =>
                !p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);

            var page11 = Diagnostics("114423046_final_project.pdf", 11);
            AssertParagraph(page11, "We adopt the Run02 checkpoint", p =>
                !p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);

            var page12 = Diagnostics("114423046_final_project.pdf", 12);
            AssertParagraph(page12, "What changes between titles", p =>
                !p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);
        });
    }
}

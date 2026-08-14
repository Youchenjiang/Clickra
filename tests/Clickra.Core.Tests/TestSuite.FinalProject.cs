using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterFinalProjectTests(TestRunner runner)
    {
        // These tests originally ran against the git-ignored fixture
        // 114423046_final_project.pdf. They now reproduce the same layout
        // features (dense work-division table grids, appendix feature tables,
        // body prose near table references) as synthetic PDFs, so they run on
        // any checkout without the fixture. The cell labels are short on
        // purpose: PdfPig keeps short per-column cells as separate paragraphs,
        // which is what the table classifier requires.
        runner.Run("Final project p14 Work Division cells are tables, not diagrams", () =>
        {
            var page = DiagnosticsFromSynthetic(BuildWorkDivisionPage());

            Assert.True(page.TableCount >= 30,
                $"Expected Work Division tableCount >= 30, got {page.TableCount}.");

            foreach (var text in new[] { "wd", "et", "abu" })
            {
                AssertParagraph(page, text, p => p.IsTable && p.IsBypassed && !p.IsDiagram);
            }

            // Body prose below the table grid must stay translatable.
            AssertParagraph(page, "attention and SHAP combine", p =>
                !p.IsTable && !p.IsBypassed && !p.IsDiagram);
        });

        runner.Run("Final project appendix feature tables stay bypassed", () =>
        {
            var page15 = DiagnosticsFromSynthetic(BuildAppendixFeatureTablePage(0));
            foreach (var text in new[] { "tv", "unk", "vid" })
            {
                AssertParagraph(page15, text, p =>
                    p.IsTable && p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent);
            }

            var page16 = DiagnosticsFromSynthetic(BuildAppendixFeatureTablePage(1));
            foreach (var text in new[] { "ttl", "ssc", "jac" })
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
            // A results page whose body mentions table references must keep
            // the prose translatable; only the table cells themselves bypass.
            var page = DiagnosticsFromSynthetic(
                new SyntheticGrayPage()
                    .AddTable(72, 700, new[] { 0.0, 95.0, 190.0 }, 22.0, BuildResultTableCells())
                    .AddOutsideText(100, "structures from existing studies")
                    .AddOutsideText(80, "This experiment studies which parts of CARMA matter")
                    .AddOutsideText(60, "We adopt the Run02 checkpoint")
                    .AddOutsideText(40, "What changes between titles"));

            AssertParagraph(page, "structures from existing studies", p =>
                !p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);
            AssertParagraph(page, "This experiment studies which parts of CARMA matter", p =>
                !p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);
            AssertParagraph(page, "We adopt the Run02 checkpoint", p =>
                !p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);
            AssertParagraph(page, "What changes between titles", p =>
                !p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);
        });
    }

    private static SyntheticGrayPage BuildWorkDivisionPage()
    {
        var cells = new string[12][];
        for (int r = 0; r < 12; r++)
        {
            cells[r] = new[] { $"wd{r}", $"et{r}", $"abu{r}" };
        }
        return new SyntheticGrayPage()
            .AddTable(72, 700, new[] { 0.0, 95.0, 190.0 }, 22.0, cells)
            .AddOutsideText(60, "attention and SHAP combine for the final stage.");
    }

    private static SyntheticGrayPage BuildAppendixFeatureTablePage(int variant)
    {
        var cells = new string[8][];
        for (int r = 0; r < 8; r++)
        {
            cells[r] = variant == 0
                ? new[] { $"tv{r}", $"unk{r}", $"vid{r}" }
                : new[] { $"ttl{r}", $"ssc{r}", $"jac{r}" };
        }
        return new SyntheticGrayPage()
            .AddTable(72, 600, new[] { 0.0, 95.0, 190.0 }, 22.0, cells);
    }

    private static string[][] BuildResultTableCells()
    {
        var cells = new string[6][];
        for (int r = 0; r < 6; r++)
        {
            cells[r] = new[] { $"res{r}", $"met{r}", $"scr{r}" };
        }
        return cells;
    }
}

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterPdfLayoutRegressionTests(TestRunner runner)
    {
        runner.Run("SemTaint p8 short multi-token equations stay bypassed", () =>
        {
            // The fixture-based check (SemTaint.pdf p8) is replaced by a
            // synthetic equation line; short math lines are classified as
            // bypassed, not as table/diagram/gray content.
            var page = DiagnosticsFromSynthetic(
                new SyntheticGrayPage()
                    .AddOutsideText(300, "raw = sanitize(input) (10)"));

            AssertParagraph(page, "raw =", p =>
                p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);
        });

        // The 2407 cases originally ran against the git-ignored fixture
        // 2407.11279v1_clean.pdf; the fixture is an arXiv paper available at
        // https://arxiv.org/pdf/2407.11279. The layout features they assert on
        // (references list, numbered result tables, a workflow figure with
        // internal labels) are reproduced here as synthetic PDFs instead, so
        // the tests run on any checkout.
        runner.Run("2407 references body is bypassed after heading", () =>
        {
            var page = DiagnosticsFromSynthetic(
                new SyntheticGrayPage()
                    .AddHeading(80, "REFERENCES")
                    .AddOutsideText(140, "Polyscope")
                    .AddOutsideText(200, "Flowdroid"));

            AssertParagraph(page, "Polyscope", p =>
                p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);
            AssertParagraph(page, "Flowdroid", p =>
                p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);
        });

        runner.Run("2407 p7 Figure 5 labels stay inside the original diagram", () =>
        {
            var page = DiagnosticsFromSynthetic(BuildFigure5Page());

            foreach (var text in new[]
            {
                "Preparing Program",
                "Compute Program",
                "Compute Environmental",
                "Compute Exploit",
                "Test for"
            })
            {
                AssertParagraph(page, text, p =>
                    p.IsDiagram && p.IsBypassed && !p.IsTable && !p.IsGrayPromptContent);
            }

            AssertParagraph(page, "PathSentinel processes Android APK files", p =>
                !p.IsDiagram && !p.IsBypassed && !p.IsTable && !p.IsGrayPromptContent);
        });

        runner.Run("2407 p10 Tables II III IV preserve their original cells", () =>
        {
            var page = DiagnosticsFromSynthetic(
                new SyntheticGrayPage()
                    .AddTable(72, 700, new[] { 0.0, 95.0, 190.0 }, 22.0, BuildResultsTableCells())
                    .AddOutsideText(160, "TABLE II")
                    .AddOutsideText(105, "TABLE III")
                    .AddOutsideText(50, "TABLE IV"));

            foreach (var text in new[]
            {
                "ta",
                "aec",
                "s12",
                "op",
                "hp",
                "tppt"
            })
            {
                AssertParagraph(page, text, IsTableBypassed);
            }

            foreach (var text in new[] { "TABLE II", "TABLE III", "TABLE IV" })
            {
                AssertParagraph(page, text, IsPlainTranslatable);
            }
        });
    }

    private static SyntheticGrayPage BuildFigure5Page()
    {
        // A workflow figure: an outer vector rectangle (the figure frame)
        // with short labels inside. The labels are short so PdfPig keeps
        // them as separate paragraphs inside the diagram region.
        return new SyntheticGrayPage()
            .AddFigureFrame(50, 300, 500, 300, new[]
            {
                "Preparing Program",
                "Compute Program",
                "Compute Environmental",
                "Compute Exploit",
                "Test for"
            })
            .AddOutsideText(60, "PathSentinel processes Android APK files.");
    }

    private static string[][] BuildResultsTableCells()
    {
        var cells = new string[8][];
        for (int r = 0; r < 8; r++)
        {
            cells[r] = new[]
            {
                r % 2 == 0 ? $"ta{r}" : $"op{r}",
                r % 2 == 0 ? $"aec{r}" : $"hp{r}",
                r % 2 == 0 ? $"s12{r}" : $"tppt{r}"
            };
        }
        return cells;
    }
}

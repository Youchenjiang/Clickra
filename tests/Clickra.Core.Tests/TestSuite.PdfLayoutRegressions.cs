static partial class TestSuite
{
    public static void RegisterPdfLayoutRegressionTests(TestRunner runner)
    {
        runner.Run("SemTaint p8 short multi-token equations stay bypassed", () =>
        {
            var page = Diagnostics("SemTaint.pdf", 8);

            AssertParagraph(page, "raw =", p =>
                p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);
        });

        runner.Run("2407 references body is bypassed after heading", () =>
        {
            var page = Diagnostics("2407.11279v1_clean.pdf", 14);

            AssertParagraph(page, "Polyscope", p =>
                p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);
            AssertParagraph(page, "Flowdroid", p =>
                p.IsBypassed && !p.IsTable && !p.IsDiagram && !p.IsGrayPromptContent);
        });

        runner.Run("2407 p7 Figure 5 labels stay inside the original diagram", () =>
        {
            var page = Diagnostics("2407.11279v1_clean.pdf", 7);

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
            var page = Diagnostics("2407.11279v1_clean.pdf", 10);

            foreach (var text in new[]
            {
                "Total Apps",
                "Average Entry Count",
                "Samsung 12",
                "OnePlus",
                "Hijacking Positives",
                "True Positive Path Traversal",
                "Source-to-Sink Flow Count",
                "Total Pre",
                "Total Post"
            })
            {
                AssertParagraph(page, text, p =>
                    p.IsTable && p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent);
            }

            foreach (var text in new[] { "TABLE II", "TABLE III", "TABLE IV" })
            {
                AssertParagraph(page, text, p =>
                    !p.IsTable && !p.IsBypassed && !p.IsDiagram && !p.IsGrayPromptContent);
            }
        });
    }
}

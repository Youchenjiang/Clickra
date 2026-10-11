using Clickra.Core.Application;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private static void AssertMarkdownUseCasePlanning(
        IConversionUseCase useCase,
        string command,
        string outputExtension)
    {
        RunWithTempDirectory(tempDir =>
        {
            string sourceDir = Path.Combine(tempDir, "source");
            string outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(outputDir);
            string input = Path.Combine(sourceDir, "notes.md");
            string template = Path.Combine(tempDir, "template.docx");
            File.WriteAllText(input, "# Notes");
            CreateWordTemplateFixture(template);

            var requested = MarkdownPdfOptions.Create(
                MarkdownPdfOptions.ThemeAcademic,
                MarkdownPdfOptions.PaperLetter,
                MarkdownPdfOptions.TextLarge,
                MarkdownPdfOptions.CodeLight,
                template);
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                command,
                new[] { input },
                requested,
                OutputOverride: outputDir,
                TrackTaskLifecycle: false));

            Assert.Equal(
                Path.Combine(Path.GetFullPath(outputDir), "notes" + outputExtension),
                plan.Outputs[0]);
            Assert.Equal(MarkdownPdfOptions.ThemeAcademic, MarkdownPdfOptions.GetTheme(plan.NormalizedOptions));
            Assert.Equal(MarkdownPdfOptions.PaperLetter, MarkdownPdfOptions.GetPaper(plan.NormalizedOptions));
            Assert.Equal(MarkdownPdfOptions.TextLarge, MarkdownPdfOptions.GetTextSize(plan.NormalizedOptions));
            Assert.Equal(MarkdownPdfOptions.CodeLight, MarkdownPdfOptions.GetCodeTheme(plan.NormalizedOptions));
            Assert.Equal(Path.GetFullPath(template), MarkdownPdfOptions.GetTemplatePath(plan.NormalizedOptions) ?? "");
            requested[MarkdownPdfOptions.ThemeKey] = MarkdownPdfOptions.ThemeDefault;
            Assert.Equal(MarkdownPdfOptions.ThemeAcademic, MarkdownPdfOptions.GetTheme(plan.NormalizedOptions));
            Assert.False(plan.TrackTaskLifecycle, "Quiet Markdown execution must be able to disable task tracking.");
        });
    }

    private static void AssertOfficeUseCasePlanning(
        IConversionUseCase useCase,
        string command,
        string inputFileName,
        string contents)
    {
        RunWithTempDirectory(tempDir =>
        {
            string sourceDir = Path.Combine(tempDir, "source");
            string overrideDir = Path.Combine(tempDir, "override");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(overrideDir);
            string input = Path.Combine(sourceDir, inputFileName);
            File.WriteAllText(input, contents);

            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                command,
                new[] { input },
                OutputOverride: overrideDir,
                TrackTaskLifecycle: false));

            Assert.Equal(command, plan.Command);
            Assert.Equal(1, plan.Outputs.Count);
            Assert.Equal(
                Path.Combine(
                    ClickraStorage.GetOutputDir(input),
                    Path.GetFileNameWithoutExtension(input) + ".pdf"),
                plan.Outputs[0]);
            Assert.False(
                Path.GetDirectoryName(plan.Outputs[0])!.Equals(overrideDir, StringComparison.OrdinalIgnoreCase),
                $"{command} must preserve the legacy behavior that ignored Native output overrides.");
            Assert.False(plan.TrackTaskLifecycle, "Headless execution must be able to disable task tracking.");
            Assert.Equal(0, plan.RequiredCapabilities.Count);
        });
    }

    private static (ConversionPlan Plan, ConversionResult Result) ExecuteUntrackedUseCase(
        IConversionUseCase useCase,
        string command,
        IReadOnlyList<string> inputs,
        string outputDir,
        IReadOnlyDictionary<string, object>? options = null)
    {
        ConversionPlan plan = useCase.Plan(new ConversionRequest(
            command,
            inputs,
            options,
            OutputOverride: outputDir,
            TrackTaskLifecycle: false));
        DelegateConversionInteraction interaction = CreateNoOpConversionInteraction();
        ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress: null)
            .GetAwaiter()
            .GetResult();
        return (plan, result);
    }
}

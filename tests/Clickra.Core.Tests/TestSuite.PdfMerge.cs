using Clickra.Core.Application;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterPdfMergeTests(TestRunner runner)
    {
        runner.Run("Merge use case owns single-output planning", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), $"clickra-merge-plan-{Guid.NewGuid():N}");
            string firstDir = Path.Combine(root, "first");
            string secondDir = Path.Combine(root, "second");
            Directory.CreateDirectory(firstDir);
            Directory.CreateDirectory(secondDir);
            try
            {
                string first = Path.Combine(firstDir, "a.pdf");
                string second = Path.Combine(secondDir, "b.pdf");
                ConversionPlan plan = new MergePdfUseCase().Plan(new ConversionRequest(
                    MergePdfUseCase.CommandName,
                    new[] { first, second }));

                Assert.True(plan.Outputs.Count == 1, "merge-pdf must plan exactly one output.");
                Assert.Equal(Path.Combine(firstDir, MergePdfUseCase.OutputFileName), plan.Outputs[0]);
                Assert.True(plan.RequiredCapabilities.Count == 0,
                    "merge-pdf must not require a presentation interaction capability.");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });

        runner.Run("Merge use case honors an explicit output directory", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), $"clickra-merge-out-{Guid.NewGuid():N}");
            string outputDir = Path.Combine(root, "out");
            Directory.CreateDirectory(root);
            try
            {
                ConversionPlan plan = new MergePdfUseCase().Plan(new ConversionRequest(
                    MergePdfUseCase.CommandName,
                    new[] { Path.Combine(root, "a.pdf"), Path.Combine(root, "b.pdf") },
                    OutputOverride: outputDir));

                Assert.Equal(Path.Combine(Path.GetFullPath(outputDir), MergePdfUseCase.OutputFileName), plan.Outputs[0]);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });

        runner.Run("Merge use case can run without task tracking", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), $"clickra-merge-run-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                string first = Path.Combine(root, "a.pdf");
                string second = Path.Combine(root, "b.pdf");
                string outputDir = Path.Combine(root, "out");
                Directory.CreateDirectory(outputDir);
                CreateSamplePdf(first);
                CreateSamplePdf(second);
                var useCase = new MergePdfUseCase();
                ConversionPlan plan = useCase.Plan(new ConversionRequest(
                    MergePdfUseCase.CommandName,
                    new[] { first, second },
                    OutputOverride: outputDir,
                    TrackTaskLifecycle: false));
                var interaction = new DelegateConversionInteraction(
                    (_, _, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<string?>(null),
                    (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));

                ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

                Assert.True(result.Status == ConversionResultStatus.Succeeded, result.Error ?? "Expected merge success.");
                Assert.True(string.IsNullOrEmpty(result.TaskId), "Untracked merge must not create a task identity.");
                using var merged = PdfReader.Open(plan.Outputs[0], PdfDocumentOpenMode.Import);
                Assert.True(merged.PageCount == 2, $"Expected two merged pages, got {merged.PageCount}.");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        });
    }
}

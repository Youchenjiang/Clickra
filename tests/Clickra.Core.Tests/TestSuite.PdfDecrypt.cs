using Clickra.Core;
using Clickra.Core.Application;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterPdfDecryptTests(TestRunner runner)
    {
        runner.Run("PDF decrypt removes protection with the correct password", () =>
        {
            string input = Path.Combine(Path.GetTempPath(), $"clickra-decrypt-{Guid.NewGuid():N}.pdf");
            string output = Path.Combine(Path.GetTempPath(), $"clickra-decrypt-{Guid.NewGuid():N}.pdf");
            const string password = "clickra-test-password";

            try
            {
                using (var document = new PdfDocument())
                {
                    document.AddPage();
                    document.SecuritySettings.UserPassword = password;
                    document.SecuritySettings.OwnerPassword = "clickra-test-owner";
                    document.Save(input);
                }

                FileProcessor.DecryptPdf(input, output, password);

                Assert.True(File.Exists(output), "Expected decrypted PDF to be written.");
                using var decrypted = PdfReader.Open(output, PdfDocumentOpenMode.Import);
                Assert.True(decrypted.PageCount == 1, "Expected decrypted PDF to preserve its page.");
                Assert.True(decrypted.SecurityHandler == null || decrypted.SecurityHandler.Elements.Count == 0,
                    "Expected decrypted PDF to have no security handler.");
            }
            finally
            {
                TryDeleteDecryptFixture(input);
                TryDeleteDecryptFixture(output);
            }
        });
        runner.Run("Decrypt use case owns planning and password retry", TestDecryptUseCaseOwnsWorkflow);
        runner.Run("Decrypt use case honors an explicit output directory", TestDecryptUseCaseHonorsOutputDirectory);
        runner.Run("Decrypt use case can run without task tracking", TestDecryptUseCaseCanRunWithoutTaskTracking);
    }

    private static void TestDecryptUseCaseCanRunWithoutTaskTracking()
    {
        string input = Path.Combine(Path.GetTempPath(), $"clickra-decrypt-untracked-{Guid.NewGuid():N}.pdf");
        string outputDir = Path.Combine(Path.GetTempPath(), $"clickra-decrypt-untracked-out-{Guid.NewGuid():N}");
        const string password = "clickra-untracked-password";
        int historyBefore = ClickraStorage.GetHistory(100).Count;
        int activeBefore = ClickraStorage.GetActiveTasks().Count;
        try
        {
            Directory.CreateDirectory(outputDir);
            using (var document = new PdfDocument())
            {
                document.AddPage();
                document.SecuritySettings.UserPassword = password;
                document.SecuritySettings.OwnerPassword = "clickra-untracked-owner";
                document.Save(input);
            }

            var useCase = new DecryptPdfUseCase();
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                DecryptPdfUseCase.CommandName,
                new[] { input },
                OutputOverride: outputDir,
                TrackTaskLifecycle: false));
            ConversionResult result = useCase.ExecuteAsync(
                    plan,
                    new RetryPasswordInteraction(password),
                    progress: null)
                .GetAwaiter()
                .GetResult();

            Assert.True(result.Status == ConversionResultStatus.Succeeded,
                $"Expected untracked decrypt success, got {result.Status}: {result.Error}");
            Assert.Equal("", result.TaskId);
            Assert.True(ClickraStorage.GetHistory(100).Count == historyBefore,
                "An untracked application execution must not write conversion history.");
            Assert.True(ClickraStorage.GetActiveTasks().Count == activeBefore,
                "An untracked application execution must not create a task record.");
        }
        finally
        {
            TryDeleteDecryptFixture(input);
            try { Directory.Delete(outputDir, recursive: true); } catch { /* Best-effort fixture cleanup. */ }
        }
    }

    private static void TestDecryptUseCaseHonorsOutputDirectory()
    {
        string input = Path.Combine(Path.GetTempPath(), "decrypt-plan-source", "secret.pdf");
        string outputDir = Path.Combine(Path.GetTempPath(), $"clickra-decrypt-output-{Guid.NewGuid():N}");
        var useCase = new DecryptPdfUseCase();

        ConversionPlan plan = useCase.Plan(new ConversionRequest(
            DecryptPdfUseCase.CommandName,
            new[] { input },
            OutputOverride: outputDir));

        Assert.Equal(
            Path.Combine(Path.GetFullPath(outputDir), "secret_decrypted.pdf"),
            plan.Outputs.Single());
    }

    private static void TestDecryptUseCaseOwnsWorkflow()
    {
        string input = Path.Combine(Path.GetTempPath(), $"clickra-decrypt-usecase-{Guid.NewGuid():N}.pdf");
        string dataDir = Environment.GetEnvironmentVariable("CLICKRA_DATA_DIR")
            ?? throw new InvalidOperationException("CLICKRA_DATA_DIR must be set for tests.");
        string historyPath = Path.Combine(dataDir, ClickraStorage.HistoryFileName);
        byte[]? historySnapshot = File.Exists(historyPath) ? File.ReadAllBytes(historyPath) : null;
        const string password = "clickra-usecase-password";
        try
        {
            using (var document = new PdfDocument())
            {
                document.AddPage();
                document.SecuritySettings.UserPassword = password;
                document.SecuritySettings.OwnerPassword = "clickra-usecase-owner";
                document.Save(input);
            }

            var useCase = new DecryptPdfUseCase();
            ConversionPlan plan = useCase.Plan(new ConversionRequest(DecryptPdfUseCase.CommandName, new[] { input }));
            Assert.True(plan.RequiredCapabilities.SetEquals(new[] { ConversionCapability.Password }),
                "Decrypt planning must declare password interaction as its only capability.");
            Assert.True(plan.Outputs.Count == 1 && plan.Outputs[0].EndsWith("_decrypted.pdf", StringComparison.OrdinalIgnoreCase),
                "Decrypt planning must own the decrypted output path.");

            var interaction = new RetryPasswordInteraction("wrong-password", password);
            ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();
            try
            {
                Assert.True(result.Status == ConversionResultStatus.Succeeded, $"Expected decrypt success, got {result.Status}: {result.Error}");
                Assert.True(interaction.Requests == 2, $"Expected one initial password prompt and one retry, got {interaction.Requests}.");
                Assert.False(interaction.RetryFlags[0], "The first password prompt must not be marked as a retry.");
                Assert.True(interaction.RetryFlags[1], "A wrong password must cause the next prompt to be marked as a retry.");
                using var decrypted = PdfReader.Open(plan.Outputs[0], PdfDocumentOpenMode.Import);
                Assert.True(decrypted.PageCount == 1, "Use-case decrypt output must remain a readable PDF.");
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(result.TaskId)) ClickraStorage.DeleteTask(result.TaskId);
                TryDeleteDecryptFixture(plan.Outputs[0]);
            }
        }
        finally
        {
            TryDeleteDecryptFixture(input);
            if (historySnapshot is null)
            {
                try { if (File.Exists(historyPath)) File.Delete(historyPath); } catch { /* Best-effort test isolation. */ }
            }
            else
            {
                File.WriteAllBytes(historyPath, historySnapshot);
            }
        }
    }

    private sealed class RetryPasswordInteraction(params string[] passwords) : IConversionInteraction
    {
        private readonly Queue<string> _passwords = new(passwords);

        public int Requests { get; private set; }
        public List<bool> RetryFlags { get; } = new();

        public Task<string?> RequestPasswordAsync(
            int fileIndex,
            string inputPath,
            bool isRetry,
            CancellationToken cancellationToken)
        {
            Requests++;
            RetryFlags.Add(isRetry);
            return Task.FromResult<string?>(_passwords.Dequeue());
        }

        public Task<string?> RequestSplitPagesAsync(
            int fileIndex,
            string inputPath,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, object>?> RequestMarkdownOptionsAsync(
            string command,
            IReadOnlyList<string> inputFiles,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static void TryDeleteDecryptFixture(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* Ignored: best-effort test fixture cleanup. */ }
    }
}

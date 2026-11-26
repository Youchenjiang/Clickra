using Clickra.Core;
using Clickra.Core.Application;
using Clickra.Core.Modules.Pdf.Decrypt.Application;
using Clickra.Core.Processors;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string DecryptTestLanguage = "zh-TW";

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

                new Clickra.Core.Modules.Pdf.Decrypt.Processing.PdfDecryptProcessor().Process(
                    new List<string> { input },
                    output,
                    new Dictionary<string, object>
                    {
                        ["password"] = password,
                        [ExecutionLocalizationOptions.LanguageKey] = DecryptTestLanguage
                    });

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
        runner.Run("IsEncrypted detects encrypted and unencrypted PDFs", TestIsEncryptedDetection);
        runner.Run("Encrypted PDF processing throws localized error across pipelines", TestEncryptedPdfThrowsLocalizedErrors);
        runner.Run("Decrypt tells an open password from print or copy restrictions", TestDecryptTellsOpenPasswordFromRestrictions);
        runner.Run("Decrypt uses the card password and needs none for restricted PDFs", TestDecryptUsesCardPassword);
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

    private static string CreateProtectedPdf(string name, string? userPassword)
    {
        string path = Path.Combine(Path.GetTempPath(), $"clickra-{name}-{Guid.NewGuid():N}.pdf");
        using var document = new PdfDocument();
        document.AddPage();
        if (userPassword is not null) document.SecuritySettings.UserPassword = userPassword;
        document.SecuritySettings.OwnerPassword = "clickra-restrict-owner";
        document.SecuritySettings.PermitPrint = false;
        document.SecuritySettings.PermitExtractContent = false;
        document.Save(path);
        return path;
    }

    private static void TestDecryptTellsOpenPasswordFromRestrictions()
    {
        string locked = CreateProtectedPdf("decrypt-open", "clickra-open-password");
        string restricted = CreateProtectedPdf("decrypt-restricted", null);
        try
        {
            Assert.True(PdfEncryptionQuery.IsEncrypted(locked), "A PDF with an open password is encrypted.");
            Assert.True(PdfEncryptionQuery.IsEncrypted(restricted), "A PDF that only restricts printing is encrypted.");
            Assert.True(PdfEncryptionQuery.NeedsOpenPassword(locked), "A PDF with an open password needs it to open.");
            Assert.True(!PdfEncryptionQuery.NeedsOpenPassword(restricted), "A PDF that only restricts printing opens without a password.");
        }
        finally
        {
            TryDeleteDecryptFixture(locked);
            TryDeleteDecryptFixture(restricted);
        }
    }

    private static void TestDecryptUsesCardPassword()
    {
        const string password = "clickra-card-password";
        string locked = CreateProtectedPdf("decrypt-card", password);
        string restricted = CreateProtectedPdf("decrypt-card-restricted", null);
        string outputDir = Path.Combine(Path.GetTempPath(), $"clickra-decrypt-card-out-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outputDir);
            var useCase = new DecryptPdfUseCase();
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                DecryptPdfUseCase.CommandName,
                new[] { restricted, locked },
                new Dictionary<string, object> { [DecryptPdfUseCase.PasswordOptionKey] = password },
                OutputOverride: outputDir,
                TrackTaskLifecycle: false));
            var interaction = new RetryPasswordInteraction();
            ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

            Assert.True(result.Status == ConversionResultStatus.Succeeded,
                $"Expected decrypt success, got {result.Status}: {result.Error}");
            Assert.True(interaction.Requests == 0,
                "The card password opens the locked PDF and the restricted one needs none, so nothing is asked again.");
            Assert.True(plan.Outputs.All(File.Exists), "Both decrypted PDFs must be written.");
        }
        finally
        {
            TryDeleteDecryptFixture(locked);
            TryDeleteDecryptFixture(restricted);
            try { Directory.Delete(outputDir, recursive: true); } catch { /* Best-effort fixture cleanup. */ }
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

    private static void TestIsEncryptedDetection()
    {
        string encryptedPath = Path.Combine(Path.GetTempPath(), $"clickra-enc-test-{Guid.NewGuid():N}.pdf");
        string unencryptedPath = Path.Combine(Path.GetTempPath(), $"clickra-unenc-test-{Guid.NewGuid():N}.pdf");
        try
        {
            using (var doc = new PdfDocument())
            {
                doc.AddPage();
                doc.SecuritySettings.UserPassword = "test";
                doc.Save(encryptedPath);
            }
            using (var doc = new PdfDocument())
            {
                doc.AddPage();
                doc.Save(unencryptedPath);
            }

            Assert.True(Clickra.Core.Modules.Pdf.Decrypt.Processing.PdfDecryptProcessor.IsEncrypted(encryptedPath),
                "Expected encrypted PDF to be detected as encrypted.");
            Assert.False(Clickra.Core.Modules.Pdf.Decrypt.Processing.PdfDecryptProcessor.IsEncrypted(unencryptedPath),
                "Expected unencrypted PDF to be detected as not encrypted.");
            Assert.False(Clickra.Core.Modules.Pdf.Decrypt.Processing.PdfDecryptProcessor.IsEncrypted("non-existent-file-path.pdf"),
                "Expected non-existent file to be detected as not encrypted.");
        }
        finally
        {
            TryDeleteDecryptFixture(encryptedPath);
            TryDeleteDecryptFixture(unencryptedPath);
        }
    }

    private static void TestEncryptedPdfThrowsLocalizedErrors()
    {
        string encryptedPath = Path.Combine(Path.GetTempPath(), $"clickra-enc-err-{Guid.NewGuid():N}.pdf");
        string outCompress = Path.Combine(Path.GetTempPath(), $"clickra-enc-comp-{Guid.NewGuid():N}.pdf");
        string outMerge = Path.Combine(Path.GetTempPath(), $"clickra-enc-merge-{Guid.NewGuid():N}.pdf");
        string outTranslate = Path.Combine(Path.GetTempPath(), $"clickra-enc-trans-{Guid.NewGuid():N}.pdf");
        try
        {
            using (var doc = new PdfDocument())
            {
                doc.AddPage();
                doc.SecuritySettings.UserPassword = "test";
                doc.Save(encryptedPath);
            }

            var compOptions = new Dictionary<string, object>
            {
                [ExecutionLocalizationOptions.LanguageKey] = DecryptTestLanguage
            };
            var compProcessor = new Clickra.Core.Modules.Pdf.Compression.Processing.PdfCompressionProcessor();
            var exComp = Assert.Throws<PdfEncryptedException>(() =>
                compProcessor.Process(new List<string> { encryptedPath }, outCompress, compOptions));
            Assert.True(exComp.ResourceKey == "PdfEncrypted", "Expected resource key match.");

            var mergeProcessor = new Clickra.Core.Modules.Pdf.Merge.Processing.PdfMergeProcessor();
            var exMerge = Assert.Throws<PdfEncryptedException>(() =>
                mergeProcessor.Process(new List<string> { encryptedPath }, outMerge, compOptions));
            Assert.True(exMerge.ResourceKey == "PdfEncrypted", "Expected resource key match.");

            var transOptions = new Dictionary<string, object>
            {
                ["targetLang"] = "en",
                [ExecutionLocalizationOptions.LanguageKey] = DecryptTestLanguage
            };
            var transProcessor = new Clickra.Core.Modules.Pdf.Translation.Processing.PdfTranslateProcessor();
            var exTrans = Assert.Throws<PdfEncryptedException>(() =>
                transProcessor.Process(new List<string> { encryptedPath }, outTranslate, transOptions));
            Assert.True(exTrans.ResourceKey == "PdfEncrypted", "Expected resource key match.");

            // Test Preflight Validate on UseCases - planning phase no longer blocks encrypted files
            var compUseCase = new Clickra.Core.Modules.Pdf.Compression.Application.CompressPdfUseCase();
            var compVal = compUseCase.Validate(new ConversionRequest("compress-pdf", new[] { encryptedPath }, compOptions));
            Assert.True(compVal.IsValid, "Expected compress preflight to succeed (unlock deferred to execution).");

            var mergeUseCase = new Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase();
            var mergeVal = mergeUseCase.Validate(new ConversionRequest("merge-pdf", new[] { encryptedPath, encryptedPath }, compOptions));
            Assert.True(mergeVal.IsValid, "Expected merge preflight to succeed (unlock deferred to execution).");

            var transUseCase = new Clickra.Core.Modules.Pdf.Translation.Application.TranslatePdfUseCase();
            var transVal = transUseCase.Validate(new ConversionRequest("translate-pdf", new[] { encryptedPath }, transOptions));
            Assert.True(transVal.IsValid, "Expected translate preflight to succeed (unlock deferred to execution).");
        }
        finally
        {
            TryDeleteDecryptFixture(encryptedPath);
            TryDeleteDecryptFixture(outCompress);
            TryDeleteDecryptFixture(outMerge);
            TryDeleteDecryptFixture(outTranslate);
        }
    }
}

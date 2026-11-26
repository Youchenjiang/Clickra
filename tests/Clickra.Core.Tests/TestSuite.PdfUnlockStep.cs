using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core.Application;
using Clickra.Core.Modules.Pdf.Decrypt.Application;
using Clickra.Core.Storage;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterPdfUnlockStepTests(TestRunner runner)
    {
        runner.Run("PdfUnlockStep: unencrypted PDF returns original path", TestPdfUnlockStepUnencrypted);
        runner.Run("PdfUnlockStep: restricted PDF unlocks without asking password", TestPdfUnlockStepRestricted);
        runner.Run("PdfUnlockStep: known password unlocks without prompt", TestPdfUnlockStepKnownPassword);
        runner.Run("PdfUnlockStep: wrong password prompts with retry flag and remembers password", TestPdfUnlockStepPromptAndRetry);
        runner.Run("PdfUnlockStep: cancel throws OperationCanceledException and leaves no temp file", TestPdfUnlockStepCancel);
        runner.Run("PdfUnlockStep: cleans residual unlock files older than one day", TestPdfUnlockStepResidualPruning);
        runner.Run("ConversionPasswordCache: deduplicates and preserves insertion order", TestConversionPasswordCacheOrder);
        runner.Run("PasswordPromptResult: validates status and factory methods", TestPasswordPromptResultProperties);
        runner.Run("DelegateConversionInteraction: defaults to Skip when prompt is absent", TestDelegateConversionInteractionDefaultsToSkip);
        runner.Run("PerFileConversionUseCaseBase: supports skip and reports SkippedFiles", TestPerFileConversionUseCaseBaseSkipSupport);
        runner.Run("PerFileConversionUseCaseBase: fails when all files are skipped", TestPerFileConversionUseCaseBaseAllSkipped);
        runner.Run("MergePdf: unlocks password-protected file and merges all", TestMergePdfUnlockSuccess);
        runner.Run("MergePdf: skips password-protected file and merges remaining files", TestMergePdfUnlockSkip);
        runner.Run("MergePdf: cancel stops merge and produces no output", TestMergePdfUnlockCancel);
        runner.Run("MergePdf: produces no output when less than two files remain", TestMergePdfUnlockLessThanTwoFilesRemaining);
        runner.Run("SplitPdf: unlocks password-protected file and splits", TestSplitPdfUnlockSuccessAndSkip);
        runner.Run("PdfToImage: unlocks password-protected file and converts", TestPdfToImageUnlockSuccessAndSkip);
        runner.Run("DecryptPdf: skips unencrypted files and decrypts protected files", TestDecryptPdfMixedWithUnencrypted);
        runner.Run("DecryptPdf: fails when all files are unencrypted", TestDecryptPdfAllUnencrypted);
        runner.Run("PdfUnlockStep: two files with same password ask only once", TestSamePasswordAcrossTwoFilesAskedOnce);
    }

    private static void TestPdfUnlockStepUnencrypted()
    {
        string path = CreateTestPdf("unencrypted", null);
        try
        {
            var interaction = new MockPasswordInteraction();
            using var result = PdfUnlockStep.UnlockAsync(0, path, interaction).GetAwaiter().GetResult();

            Assert.True(result.Status == PdfUnlockStatus.NotEncrypted, "Expected NotEncrypted status.");
            Assert.Equal(path, result.EffectivePath);
            Assert.False(result.WasEncrypted, "Expected not encrypted.");
            Assert.False(result.HadOpenPassword, "Expected no open password.");
            Assert.False(result.IsTempFile, "Expected not a temp file.");
            Assert.Equal(0L, (long)interaction.CallCount);
        }
        finally
        {
            TryDeleteDecryptFixture(path);
        }
    }

    private static void TestPdfUnlockStepRestricted()
    {
        string path = CreateTestRestrictedPdf("restricted");
        string? tempPath = null;
        try
        {
            var interaction = new MockPasswordInteraction();
            using (var result = PdfUnlockStep.UnlockAsync(0, path, interaction).GetAwaiter().GetResult())
            {
                tempPath = result.EffectivePath;
                Assert.True(result.Status == PdfUnlockStatus.Success, "Expected Success status.");
                Assert.True(result.WasEncrypted, "Expected was encrypted.");
                Assert.False(result.HadOpenPassword, "Expected no open password.");
                Assert.True(result.IsTempFile, "Expected is temp file.");
                Assert.True(!string.Equals(path, tempPath, StringComparison.Ordinal), "Temp path must differ from original.");
                Assert.True(File.Exists(tempPath), "Temp file must exist while active.");
                Assert.Equal(0L, (long)interaction.CallCount);

                using var doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Import);
                Assert.Equal(1L, (long)doc.PageCount);
                Assert.True(doc.SecurityHandler == null || doc.SecurityHandler.Elements.Count == 0, "Decrypted doc must have no security handler.");
            }

            Assert.False(File.Exists(tempPath), "Temp file must be deleted upon disposal.");
        }
        finally
        {
            TryDeleteDecryptFixture(path);
            if (tempPath != null) TryDeleteDecryptFixture(tempPath);
        }
    }

    private static void TestPdfUnlockStepKnownPassword()
    {
        const string password = "known-password-123";
        string path = CreateTestPdf("locked", password);
        string? tempPath = null;
        try
        {
            var interaction = new MockPasswordInteraction();
            var known = new List<string> { "wrong-pass", password };

            using (var result = PdfUnlockStep.UnlockAsync(0, path, interaction, known).GetAwaiter().GetResult())
            {
                tempPath = result.EffectivePath;
                Assert.True(result.Status == PdfUnlockStatus.Success, "Expected Success status.");
                Assert.True(result.WasEncrypted, "Expected was encrypted.");
                Assert.True(result.HadOpenPassword, "Expected had open password.");
                Assert.True(result.IsTempFile, "Expected is temp file.");
                Assert.True(File.Exists(tempPath), "Temp file must exist.");
                Assert.Equal(0L, (long)interaction.CallCount);
            }

            Assert.False(File.Exists(tempPath), "Temp file must be deleted upon disposal.");
        }
        finally
        {
            TryDeleteDecryptFixture(path);
            if (tempPath != null) TryDeleteDecryptFixture(tempPath);
        }
    }

    private static void TestPdfUnlockStepPromptAndRetry()
    {
        const string password = "user-entered-password";
        string path = CreateTestPdf("prompt", password);
        string? tempPath = null;
        try
        {
            var interaction = new MockPasswordInteraction("wrong-1", password);
            var known = new List<string>();

            using (var result = PdfUnlockStep.UnlockAsync(0, path, interaction, known).GetAwaiter().GetResult())
            {
                tempPath = result.EffectivePath;
                Assert.True(result.Status == PdfUnlockStatus.Success, "Expected Success status.");
                Assert.True(result.WasEncrypted, "Expected was encrypted.");
                Assert.True(result.HadOpenPassword, "Expected had open password.");
                Assert.True(result.IsTempFile, "Expected is temp file.");
                Assert.True(File.Exists(tempPath), "Temp file must exist.");

                Assert.Equal(2L, (long)interaction.CallCount);
                Assert.False(interaction.RetryFlags[0], "First prompt must have isRetry=false.");
                Assert.True(interaction.RetryFlags[1], "Second prompt must have isRetry=true.");
                Assert.True(known.Contains(password), "Successfully used password must be added to known list.");
            }

            Assert.False(File.Exists(tempPath), "Temp file must be deleted upon disposal.");
        }
        finally
        {
            TryDeleteDecryptFixture(path);
            if (tempPath != null) TryDeleteDecryptFixture(tempPath);
        }
    }

    private static void TestPdfUnlockStepCancel()
    {
        string path = CreateTestPdf("cancel", "secret");
        try
        {
            var interaction = new MockPasswordInteraction((string?)null);
            Assert.Throws<OperationCanceledException>(() =>
            {
                PdfUnlockStep.UnlockAsync(0, path, interaction).GetAwaiter().GetResult();
            });
        }
        finally
        {
            TryDeleteDecryptFixture(path);
        }
    }

    private static void TestPdfUnlockStepResidualPruning()
    {
        Directory.CreateDirectory(StorageRuntime.UnlockTempDirectory);
        string oldFile = Path.Combine(StorageRuntime.UnlockTempDirectory, $"{Guid.NewGuid():N}.pdf");
        string newFile = Path.Combine(StorageRuntime.UnlockTempDirectory, $"{Guid.NewGuid():N}.pdf");

        try
        {
            File.WriteAllText(oldFile, "%PDF-1.4 dummy");
            File.WriteAllText(newFile, "%PDF-1.4 dummy");

            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-2));
            File.SetLastWriteTimeUtc(newFile, DateTime.UtcNow);

            TaskPruningCoordinator.Prune();

            Assert.False(File.Exists(oldFile), "Files older than 1 day in unlock/ must be pruned.");
            Assert.True(File.Exists(newFile), "Recent files in unlock/ must not be pruned.");
        }
        finally
        {
            TryDeleteDecryptFixture(oldFile);
            TryDeleteDecryptFixture(newFile);
        }
    }

    private static string CreateTestPdf(string name, string? password)
    {
        string path = Path.Combine(Path.GetTempPath(), $"clickra-unlock-{name}-{Guid.NewGuid():N}.pdf");
        using var doc = new PdfDocument();
        doc.AddPage();
        if (password != null)
        {
            doc.SecuritySettings.UserPassword = password;
            doc.SecuritySettings.OwnerPassword = $"{password}-owner";
        }
        doc.Save(path);
        return path;
    }

    private static string CreateTestRestrictedPdf(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), $"clickra-unlock-{name}-{Guid.NewGuid():N}.pdf");
        using var doc = new PdfDocument();
        doc.AddPage();
        doc.SecuritySettings.OwnerPassword = "owner-only-restrict";
        doc.SecuritySettings.PermitPrint = false;
        doc.SecuritySettings.PermitExtractContent = false;
        doc.Save(path);
        return path;
    }

    private sealed class MockPasswordInteraction : IConversionInteraction
    {
        private readonly Queue<PasswordPromptResult> _responses;

        public MockPasswordInteraction()
        {
            _responses = new Queue<PasswordPromptResult>();
        }

        public MockPasswordInteraction(params string?[] responses)
        {
            _responses = new Queue<PasswordPromptResult>(responses.Select(r =>
                r == null ? PasswordPromptResult.Cancel : PasswordPromptResult.FromPassword(r)));
        }

        public MockPasswordInteraction(params PasswordPromptResult[] responses)
        {
            _responses = new Queue<PasswordPromptResult>(responses);
        }

        public int CallCount { get; private set; }
        public List<bool> RetryFlags { get; } = new();

        public Task<string?> RequestPasswordAsync(int fileIndex, string inputPath, bool isRetry, CancellationToken cancellationToken)
        {
            CallCount++;
            RetryFlags.Add(isRetry);
            if (_responses.Count == 0) return Task.FromResult<string?>(null);
            var res = _responses.Dequeue();
            return Task.FromResult(res.Password);
        }

        public Task<PasswordPromptResult> RequestPasswordResultAsync(int fileIndex, string inputPath, bool isRetry, CancellationToken cancellationToken)
        {
            CallCount++;
            RetryFlags.Add(isRetry);
            if (_responses.Count == 0) return Task.FromResult(PasswordPromptResult.Cancel);
            return Task.FromResult(_responses.Dequeue());
        }

        public Task<string?> RequestSplitPagesAsync(int fileIndex, string inputPath, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyDictionary<string, object>?> RequestMarkdownOptionsAsync(string command, IReadOnlyList<string> inputFiles, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, object>?>(null);
    }

    private static void TestConversionPasswordCacheOrder()
    {
        var cache = new ConversionPasswordCache();
        cache.Add("pass1");
        cache.Add("pass2");
        cache.Add("pass1");
        cache.Add("");

        Assert.Equal(2L, (long)cache.Passwords.Count);
        Assert.Equal("pass1", cache.Passwords[0]);
        Assert.Equal("pass2", cache.Passwords[1]);
    }

    private static void TestPasswordPromptResultProperties()
    {
        var pwd = PasswordPromptResult.FromPassword("secret");
        Assert.True(pwd.IsPassword, "Expected IsPassword");
        Assert.Equal("secret", pwd.Password ?? "");

        var skip = PasswordPromptResult.Skip;
        Assert.True(skip.IsSkip, "Expected IsSkip");

        var cancel = PasswordPromptResult.Cancel;
        Assert.True(cancel.IsCancel, "Expected IsCancel");
    }

    private static void TestDelegateConversionInteractionDefaultsToSkip()
    {
        var interaction = new DelegateConversionInteraction(
            password: null,
            splitPages: (_, _, _) => Task.FromResult<string?>(null),
            markdownOptions: (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));

        var skip = interaction.RequestPasswordResultAsync(0, "a.pdf", false, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(skip.IsSkip, "When no password prompt is configured, RequestPasswordResultAsync must default to Skip.");

        var withPwd = new DelegateConversionInteraction(
            password: (_, _, _, _) => Task.FromResult<string?>("entered-pwd"),
            splitPages: (_, _, _) => Task.FromResult<string?>(null),
            markdownOptions: (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));

        var pwdRes = withPwd.RequestPasswordResultAsync(0, "a.pdf", false, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(pwdRes.IsPassword, "Legacy string password must map to Password result.");
        Assert.Equal("entered-pwd", pwdRes.Password ?? "");

        var withCancel = new DelegateConversionInteraction(
            password: (_, _, _, _) => Task.FromResult<string?>(null),
            splitPages: (_, _, _) => Task.FromResult<string?>(null),
            markdownOptions: (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));

        var cancelRes = withCancel.RequestPasswordResultAsync(0, "a.pdf", false, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(cancelRes.IsCancel, "Legacy null password must map to Cancel result.");
    }

    private static void TestPerFileConversionUseCaseBaseSkipSupport()
    {
        var useCase = new MockPerFileUseCase(index =>
        {
            if (index == 0)
                throw new ConversionFileSkippedException(ConversionSkipReason.NeedsPassword);
        });

        var plan = useCase.Plan(new ConversionRequest(
            "mock_per_file",
            new[] { "file0.pdf", "file1.pdf" }));

        var interaction = new MockPasswordInteraction();
        var result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

        Assert.True(result.Status == ConversionResultStatus.Succeeded, "Partial skips must result in Succeeded overall.");
        Assert.Equal(1L, (long)result.SkippedFiles.Count);
        Assert.Equal("file0.pdf", result.SkippedFiles[0].Path);
        Assert.True(result.SkippedFiles[0].Reason == ConversionSkipReason.NeedsPassword, "Expected NeedsPassword reason.");
        Assert.Equal(1L, (long)result.Outputs.Count);
        Assert.Equal("file1.pdf.out", result.Outputs[0]);
    }

    private static void TestPerFileConversionUseCaseBaseAllSkipped()
    {
        var useCase = new MockPerFileUseCase(_ =>
            throw new ConversionFileSkippedException(ConversionSkipReason.NeedsPassword));

        var plan = useCase.Plan(new ConversionRequest(
            "mock_per_file",
            new[] { "file0.pdf", "file1.pdf" }));

        var interaction = new MockPasswordInteraction();
        var result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

        Assert.True(result.Status == ConversionResultStatus.Failed, "All files skipped must result in Failed.");
        Assert.True(result.Error?.Contains("每個檔案都要密碼", StringComparison.Ordinal) == true,
            $"Expected all files skipped error message, got: {result.Error}");
        Assert.Equal(2L, (long)result.SkippedFiles.Count);
    }

    private sealed class MockPerFileUseCase(Action<int>? onExecuteFile = null) : PerFileConversionUseCaseBase
    {
        public override string Command => "mock_per_file";
        protected override string UseCaseName => "Mock Per File";
        protected override string InputRequirementError => "Input required";
        protected override string OutputCountError => "Output count mismatch";
        protected override string UnsupportedInputError(string path) => "Unsupported input";
        public override bool AcceptsAllFileExtensions => true;

        protected override string GetOutputFileName(string input) => Path.GetFileName(input) + ".out";

        public override ConversionPlan Plan(ConversionRequest request)
        {
            var outputs = request.InputFiles.Select(f => f + ".out").ToArray();
            return new ConversionPlan(
                request.Command,
                request.InputFiles,
                outputs,
                Capabilities,
                request.Options ?? new Dictionary<string, object>());
        }

        protected override Task ExecuteFileAsync(
            int index,
            ConversionPlan plan,
            IConversionInteraction interaction,
            IProgress<ConversionProgress>? progress,
            object? executionState,
            CancellationToken cancellationToken)
        {
            onExecuteFile?.Invoke(index);
            return Task.CompletedTask;
        }
    }

    private static void TestMergePdfUnlockSuccess()
    {
        string f1 = CreateTestPdf("merge1", null);
        string f2 = CreateTestPdf("merge2", "secret");
        string f3 = CreateTestPdf("merge3", null);
        string outDir = Path.Combine(Path.GetTempPath(), $"clickra-merge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);

        try
        {
            var useCase = new Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase();
            var plan = useCase.Plan(new ConversionRequest(
                Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase.CommandName,
                new[] { f1, f2, f3 },
                null,
                null,
                outDir,
                false,
                false));

            var interaction = new MockPasswordInteraction("secret");
            var result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

            Assert.True(result.Status == ConversionResultStatus.Succeeded, "Merge must succeed when password is provided.");
            Assert.True(File.Exists(plan.Outputs[0]), "Merged output must exist.");
            using var doc = PdfReader.Open(plan.Outputs[0], PdfDocumentOpenMode.Import);
            Assert.Equal(3L, (long)doc.PageCount);
            Assert.Equal(0L, (long)result.SkippedFiles.Count);
        }
        finally
        {
            TryDeleteDecryptFixture(f1);
            TryDeleteDecryptFixture(f2);
            TryDeleteDecryptFixture(f3);
            if (Directory.Exists(outDir))
            {
                try { Directory.Delete(outDir, true); } catch { }
            }
        }
    }

    private static void TestMergePdfUnlockSkip()
    {
        string f1 = CreateTestPdf("merge1", null);
        string f2 = CreateTestPdf("merge2", "secret");
        string f3 = CreateTestPdf("merge3", null);
        string outDir = Path.Combine(Path.GetTempPath(), $"clickra-merge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);

        try
        {
            var useCase = new Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase();
            var plan = useCase.Plan(new ConversionRequest(
                Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase.CommandName,
                new[] { f1, f2, f3 },
                null,
                null,
                outDir,
                false,
                false));

            var interaction = new MockPasswordInteraction(PasswordPromptResult.Skip);
            var result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

            Assert.True(result.Status == ConversionResultStatus.Succeeded, "Merge must succeed when skipping a file if at least 2 remain.");
            Assert.True(File.Exists(plan.Outputs[0]), "Merged output must exist.");
            using var doc = PdfReader.Open(plan.Outputs[0], PdfDocumentOpenMode.Import);
            Assert.Equal(2L, (long)doc.PageCount);
            Assert.Equal(1L, (long)result.SkippedFiles.Count);
            Assert.Equal(f2, result.SkippedFiles[0].Path);
            Assert.True(result.SkippedFiles[0].Reason == ConversionSkipReason.NeedsPassword, "Expected NeedsPassword");
        }
        finally
        {
            TryDeleteDecryptFixture(f1);
            TryDeleteDecryptFixture(f2);
            TryDeleteDecryptFixture(f3);
            if (Directory.Exists(outDir))
            {
                try { Directory.Delete(outDir, true); } catch { }
            }
        }
    }

    private static void TestMergePdfUnlockCancel()
    {
        string f1 = CreateTestPdf("merge1", null);
        string f2 = CreateTestPdf("merge2", "secret");
        string f3 = CreateTestPdf("merge3", null);
        string outDir = Path.Combine(Path.GetTempPath(), $"clickra-merge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);

        try
        {
            var useCase = new Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase();
            var plan = useCase.Plan(new ConversionRequest(
                Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase.CommandName,
                new[] { f1, f2, f3 },
                null,
                null,
                outDir,
                false,
                false));

            var interaction = new MockPasswordInteraction(PasswordPromptResult.Cancel);
            var result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

            Assert.True(result.Status == ConversionResultStatus.Canceled, "Cancel must return Canceled status.");
            Assert.False(File.Exists(plan.Outputs[0]), "Cancelled merge must produce no output file.");
        }
        finally
        {
            TryDeleteDecryptFixture(f1);
            TryDeleteDecryptFixture(f2);
            TryDeleteDecryptFixture(f3);
            if (Directory.Exists(outDir))
            {
                try { Directory.Delete(outDir, true); } catch { }
            }
        }
    }

    private static void TestMergePdfUnlockLessThanTwoFilesRemaining()
    {
        string f1 = CreateTestPdf("merge1", null);
        string f2 = CreateTestPdf("merge2", "secret");
        string outDir = Path.Combine(Path.GetTempPath(), $"clickra-merge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);

        try
        {
            var useCase = new Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase();
            var plan = useCase.Plan(new ConversionRequest(
                Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase.CommandName,
                new[] { f1, f2 },
                null,
                null,
                outDir,
                false,
                false));

            var interaction = new MockPasswordInteraction(PasswordPromptResult.Skip);
            var result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

            Assert.True(result.Status == ConversionResultStatus.Failed, "Merge with fewer than 2 files must fail.");
            Assert.True(result.Error?.Contains("只剩 1 個檔案，沒有合併", StringComparison.Ordinal) == true,
                $"Expected error mentioning only 1 file remaining, got: {result.Error}");
            Assert.False(File.Exists(plan.Outputs[0]), "Failed merge must produce no output file.");
        }
        finally
        {
            TryDeleteDecryptFixture(f1);
            TryDeleteDecryptFixture(f2);
            if (Directory.Exists(outDir))
            {
                try { Directory.Delete(outDir, true); } catch { }
            }
        }
    }

    private static void TestSplitPdfUnlockSuccessAndSkip()
    {
        string f1 = CreateTestPdf("split1", null);
        string f2 = CreateTestPdf("split2", "secret");
        string f3 = CreateTestPdf("split3", null);
        string outDir = Path.Combine(Path.GetTempPath(), $"clickra-split-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);

        try
        {
            var useCase = new Clickra.Core.Modules.Pdf.Split.Application.SplitPdfUseCase();
            var options = new Dictionary<string, object> { ["pages"] = "1" };
            var plan = useCase.Plan(new ConversionRequest(
                Clickra.Core.Modules.Pdf.Split.Application.SplitPdfUseCase.CommandName,
                new[] { f1, f2, f3 },
                options,
                null,
                outDir,
                false,
                false));

            var interaction = new MockPasswordInteraction("secret");
            var result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

            Assert.True(result.Status == ConversionResultStatus.Succeeded, "Split must succeed when password provided.");
            Assert.Equal(0L, (long)result.SkippedFiles.Count);
            Assert.Equal(3L, (long)result.Outputs.Count);

            // Now test with skip using a fresh use case instance (empty password cache)
            var useCaseSkip = new Clickra.Core.Modules.Pdf.Split.Application.SplitPdfUseCase();
            var interactionSkip = new MockPasswordInteraction(PasswordPromptResult.Skip);
            var resultSkip = useCaseSkip.ExecuteAsync(plan, interactionSkip, progress: null).GetAwaiter().GetResult();
            Assert.True(resultSkip.Status == ConversionResultStatus.Succeeded, "Split must succeed with partial skips.");
            Assert.Equal(1L, (long)resultSkip.SkippedFiles.Count);
            Assert.Equal(f2, resultSkip.SkippedFiles[0].Path);
        }
        finally
        {
            TryDeleteDecryptFixture(f1);
            TryDeleteDecryptFixture(f2);
            TryDeleteDecryptFixture(f3);
            if (Directory.Exists(outDir))
            {
                try { Directory.Delete(outDir, true); } catch { }
            }
        }
    }

    private static void TestPdfToImageUnlockSuccessAndSkip()
    {
        string f1 = CreateTestPdf("p2i1", null);
        string f2 = CreateTestPdf("p2i2", "secret");
        string f3 = CreateTestPdf("p2i3", null);
        string outDir = Path.Combine(Path.GetTempPath(), $"clickra-p2i-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);

        try
        {
            var useCase = new Clickra.Core.Modules.Pdf.ToImage.Application.PdfToImageUseCase();
            var plan = useCase.Plan(new ConversionRequest(
                Clickra.Core.Modules.Pdf.ToImage.Application.PdfToImageUseCase.CommandName,
                new[] { f1, f2, f3 },
                null,
                null,
                outDir,
                false,
                false));

            var interaction = new MockPasswordInteraction("secret");
            var result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

            Assert.True(result.Status == ConversionResultStatus.Succeeded, "PDF to Image must succeed when password provided.");
            Assert.Equal(0L, (long)result.SkippedFiles.Count);

            // Test with skip using a fresh use case instance (empty password cache)
            var useCaseSkip = new Clickra.Core.Modules.Pdf.ToImage.Application.PdfToImageUseCase();
            var interactionSkip = new MockPasswordInteraction(PasswordPromptResult.Skip);
            var resultSkip = useCaseSkip.ExecuteAsync(plan, interactionSkip, progress: null).GetAwaiter().GetResult();
            Assert.True(resultSkip.Status == ConversionResultStatus.Succeeded, "PDF to Image must succeed with partial skips.");
            Assert.Equal(1L, (long)resultSkip.SkippedFiles.Count);
            Assert.Equal(f2, resultSkip.SkippedFiles[0].Path);
        }
        finally
        {
            TryDeleteDecryptFixture(f1);
            TryDeleteDecryptFixture(f2);
            TryDeleteDecryptFixture(f3);
            if (Directory.Exists(outDir))
            {
                try { Directory.Delete(outDir, true); } catch { }
            }
        }
    }

    private static void TestDecryptPdfMixedWithUnencrypted()
    {
        string f1 = CreateTestPdf("dec1", null);
        string f2 = CreateTestPdf("dec2", "secret");
        string f3 = CreateTestRestrictedPdf("dec3");
        string outDir = Path.Combine(Path.GetTempPath(), $"clickra-dec-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);

        try
        {
            var useCase = new Clickra.Core.Modules.Pdf.Decrypt.Application.DecryptPdfUseCase();
            var plan = useCase.Plan(new ConversionRequest(
                Clickra.Core.Modules.Pdf.Decrypt.Application.DecryptPdfUseCase.CommandName,
                new[] { f1, f2, f3 },
                null,
                null,
                outDir,
                false,
                false));

            var interaction = new MockPasswordInteraction("secret");
            var result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

            Assert.True(result.Status == ConversionResultStatus.Succeeded, "Mixed decrypt must succeed.");
            Assert.Equal(1L, (long)result.SkippedFiles.Count);
            Assert.Equal(f1, result.SkippedFiles[0].Path);
            Assert.True(result.SkippedFiles[0].Reason == ConversionSkipReason.NotEncrypted, "f1 must be skipped as NotEncrypted");
            Assert.True(File.Exists(plan.Outputs[1]), "f2 decrypted output must exist.");
            Assert.True(File.Exists(plan.Outputs[2]), "f3 decrypted output must exist.");
        }
        finally
        {
            TryDeleteDecryptFixture(f1);
            TryDeleteDecryptFixture(f2);
            TryDeleteDecryptFixture(f3);
            if (Directory.Exists(outDir))
            {
                try { Directory.Delete(outDir, true); } catch { }
            }
        }
    }

    private static void TestDecryptPdfAllUnencrypted()
    {
        string f1 = CreateTestPdf("dec_none1", null);
        string f2 = CreateTestPdf("dec_none2", null);
        string outDir = Path.Combine(Path.GetTempPath(), $"clickra-dec-none-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);

        try
        {
            var useCase = new Clickra.Core.Modules.Pdf.Decrypt.Application.DecryptPdfUseCase();
            var plan = useCase.Plan(new ConversionRequest(
                Clickra.Core.Modules.Pdf.Decrypt.Application.DecryptPdfUseCase.CommandName,
                new[] { f1, f2 },
                null,
                null,
                outDir,
                false,
                false));

            var interaction = new MockPasswordInteraction();
            var result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

            Assert.True(result.Status == ConversionResultStatus.Failed, "All unencrypted files in decrypt must fail.");
            Assert.True(result.Error?.Contains(Localization.T("pdf_not_encrypted", "zh-TW"), StringComparison.Ordinal) == true ||
                        result.Error?.Contains(Localization.T("pdf_not_encrypted", "en-US"), StringComparison.Ordinal) == true,
                $"Expected pdf_not_encrypted error, got: {result.Error}");
            Assert.Equal(2L, (long)result.SkippedFiles.Count);
            Assert.True(result.SkippedFiles.All(s => s.Reason == ConversionSkipReason.NotEncrypted), "All reasons must be NotEncrypted");
        }
        finally
        {
            TryDeleteDecryptFixture(f1);
            TryDeleteDecryptFixture(f2);
            if (Directory.Exists(outDir))
            {
                try { Directory.Delete(outDir, true); } catch { }
            }
        }
    }

    private static void TestSamePasswordAcrossTwoFilesAskedOnce()
    {
        string f1 = CreateTestPdf("same1", "common-secret");
        string f2 = CreateTestPdf("same2", "common-secret");
        string outDir = Path.Combine(Path.GetTempPath(), $"clickra-same-pwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);

        try
        {
            var useCase = new Clickra.Core.Modules.Pdf.Compression.Application.CompressPdfUseCase();
            var plan = useCase.Plan(new ConversionRequest(
                Clickra.Core.Modules.Pdf.Compression.Application.CompressPdfUseCase.CommandName,
                new[] { f1, f2 },
                null,
                null,
                outDir,
                false,
                false));

            var interaction = new MockPasswordInteraction("common-secret");
            var result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

            Assert.True(result.Status == ConversionResultStatus.Succeeded, "Compression must succeed.");
            Assert.Equal(1L, (long)interaction.CallCount);
        }
        finally
        {
            TryDeleteDecryptFixture(f1);
            TryDeleteDecryptFixture(f2);
            if (Directory.Exists(outDir))
            {
                try { Directory.Delete(outDir, true); } catch { }
            }
        }
    }
}

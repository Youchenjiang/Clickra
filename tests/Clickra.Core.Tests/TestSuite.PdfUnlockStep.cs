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
    private const string SampleUnlockValue = "sample-unlock";
    private const string MergePrefix1 = "merge1";
    private const string MergePrefix2 = "merge2";
    private const string MergeTestDirPrefix = "merge-test";

    public static void RegisterPdfUnlockStepTests(TestRunner runner)
    {
        runner.Run("PdfUnlockStep: unencrypted PDF returns original path", TestPdfUnlockStepUnencrypted);
        runner.Run("PdfUnlockStep: restricted PDF unlocks without asking password", TestPdfUnlockStepRestricted);
        runner.Run("PdfUnlockStep: known password unlocks without prompt", TestPdfUnlockStepKnownPassword);
        runner.Run("PdfUnlockStep: wrong password prompts with retry flag and remembers password", TestPdfUnlockStepPromptAndRetry);
        runner.Run("PdfUnlockStep: cancel throws OperationCanceledException and leaves no temp file", TestPdfUnlockStepCancel);
        runner.Run("PdfUnlockStep: preserves PDF outlines when unlocking", TestPdfUnlockStepPreservesOutlines);
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
        runner.Run("UseCase: same instance does not leak password cache across batches", TestSameUseCaseInstanceDoesNotShareCacheAcrossExecutions);
        runner.Run("CompressPdf: unlocks, skips, and cancels in 3-file batch", TestCompressPdfUnlockBatch);
        runner.Run("TranslatePdf: unlocks, skips, and cancels in 3-file batch", TestTranslatePdfUnlockBatch);
        runner.Run("PrintPdf: unlocks, skips, and cancels in 3-file batch", TestPrintPdfUnlockBatch);
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
        string path = CreateTestPdf("cancel", SampleUnlockValue);
        try
        {
            var interaction = new MockPasswordInteraction((string?)null);
            Assert.Throws<OperationCanceledException>(
                () => PdfUnlockStep.UnlockAsync(0, path, interaction).GetAwaiter().GetResult());
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

        var snapshot = cache.GetSnapshot();
        Assert.Equal(2L, (long)snapshot.Count);
        Assert.Equal("pass1", snapshot[0]);
        Assert.Equal("pass2", snapshot[1]);
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

    private static void RunWithTempFilesAndOutDir(
        string outDirPrefix,
        Func<string[]> createInputs,
        Action<string[], string> testAction)
    {
        string[] inputs = createInputs();
        string outDir = Path.Combine(Path.GetTempPath(), $"clickra-{outDirPrefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            testAction(inputs, outDir);
        }
        finally
        {
            foreach (string input in inputs)
            {
                TryDeleteDecryptFixture(input);
            }
            if (Directory.Exists(outDir))
            {
                try { Directory.Delete(outDir, true); }
                catch { /* Best-effort test output directory cleanup */ }
            }
        }
    }

    private static ConversionPlan CreateUnlockTestPlan(
        IConversionUseCase useCase,
        string command,
        IReadOnlyList<string> inputs,
        string outDir,
        Dictionary<string, object>? options = null) =>
        useCase.Plan(new ConversionRequest(command, inputs, options, null, outDir, false, false));

    private static void TestMergePdfUnlockSuccess() =>
        RunWithTempFilesAndOutDir(
            MergeTestDirPrefix,
            () => [CreateTestPdf(MergePrefix1, null), CreateTestPdf(MergePrefix2, SampleUnlockValue), CreateTestPdf("merge3", null)],
            (files, outDir) =>
            {
                var useCase = new Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase();
                var plan = CreateUnlockTestPlan(useCase, Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase.CommandName, files, outDir);
                var result = useCase.ExecuteAsync(plan, new MockPasswordInteraction(SampleUnlockValue), progress: null).GetAwaiter().GetResult();

                Assert.True(result.Status == ConversionResultStatus.Succeeded, "Merge must succeed when password is provided.");
                Assert.True(File.Exists(plan.Outputs[0]), "Merged output must exist.");
                using var doc = PdfReader.Open(plan.Outputs[0], PdfDocumentOpenMode.Import);
                Assert.Equal(3L, (long)doc.PageCount);
                Assert.Equal(0L, (long)result.SkippedFiles.Count);
            });

    private static void TestMergePdfUnlockSkip() =>
        RunWithTempFilesAndOutDir(
            MergeTestDirPrefix,
            () => [CreateTestPdf(MergePrefix1, null), CreateTestPdf(MergePrefix2, SampleUnlockValue), CreateTestPdf("merge3", null)],
            (files, outDir) =>
            {
                var useCase = new Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase();
                var plan = CreateUnlockTestPlan(useCase, Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase.CommandName, files, outDir);
                var result = useCase.ExecuteAsync(plan, new MockPasswordInteraction(PasswordPromptResult.Skip), progress: null).GetAwaiter().GetResult();

                Assert.True(result.Status == ConversionResultStatus.Succeeded, "Merge must succeed when skipping a file if at least 2 remain.");
                Assert.True(File.Exists(plan.Outputs[0]), "Merged output must exist.");
                using var doc = PdfReader.Open(plan.Outputs[0], PdfDocumentOpenMode.Import);
                Assert.Equal(2L, (long)doc.PageCount);
                Assert.Equal(1L, (long)result.SkippedFiles.Count);
                Assert.Equal(files[1], result.SkippedFiles[0].Path);
                Assert.True(result.SkippedFiles[0].Reason == ConversionSkipReason.NeedsPassword, "Expected NeedsPassword");
            });

    private static void TestMergePdfUnlockCancel() =>
        RunWithTempFilesAndOutDir(
            MergeTestDirPrefix,
            () => [CreateTestPdf(MergePrefix1, null), CreateTestPdf(MergePrefix2, SampleUnlockValue), CreateTestPdf("merge3", null)],
            (files, outDir) =>
            {
                var useCase = new Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase();
                var plan = CreateUnlockTestPlan(useCase, Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase.CommandName, files, outDir);
                var result = useCase.ExecuteAsync(plan, new MockPasswordInteraction(PasswordPromptResult.Cancel), progress: null).GetAwaiter().GetResult();

                Assert.True(result.Status == ConversionResultStatus.Canceled, "Cancel must return Canceled status.");
                Assert.False(File.Exists(plan.Outputs[0]), "Cancelled merge must produce no output file.");
            });

    private static void TestMergePdfUnlockLessThanTwoFilesRemaining() =>
        RunWithTempFilesAndOutDir(
            MergeTestDirPrefix,
            () => [CreateTestPdf(MergePrefix1, null), CreateTestPdf(MergePrefix2, SampleUnlockValue)],
            (files, outDir) =>
            {
                var useCase = new Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase();
                var plan = CreateUnlockTestPlan(useCase, Clickra.Core.Modules.Pdf.Merge.Application.MergePdfUseCase.CommandName, files, outDir);
                var result = useCase.ExecuteAsync(plan, new MockPasswordInteraction(PasswordPromptResult.Skip), progress: null).GetAwaiter().GetResult();

                Assert.True(result.Status == ConversionResultStatus.Failed, "Merge with fewer than 2 files must fail.");
                Assert.True(result.Error?.Contains("只剩 1 個檔案，沒有合併", StringComparison.Ordinal) == true,
                    $"Expected error mentioning only 1 file remaining, got: {result.Error}");
                Assert.False(File.Exists(plan.Outputs[0]), "Failed merge must produce no output file.");
            });

    private static void TestSplitPdfUnlockSuccessAndSkip() =>
        RunWithTempFilesAndOutDir(
            "split-test",
            () => [CreateTestPdf("split1", null), CreateTestPdf("split2", SampleUnlockValue), CreateTestPdf("split3", null)],
            (files, outDir) =>
            {
                var useCase = new Clickra.Core.Modules.Pdf.Split.Application.SplitPdfUseCase();
                var plan = CreateUnlockTestPlan(
                    useCase,
                    Clickra.Core.Modules.Pdf.Split.Application.SplitPdfUseCase.CommandName,
                    files,
                    outDir,
                    new Dictionary<string, object> { ["pages"] = "1" });
                var result = useCase.ExecuteAsync(plan, new MockPasswordInteraction(SampleUnlockValue), progress: null).GetAwaiter().GetResult();

                Assert.True(result.Status == ConversionResultStatus.Succeeded, "Split must succeed when password provided.");
                Assert.Equal(0L, (long)result.SkippedFiles.Count);
                Assert.Equal(3L, (long)result.Outputs.Count);

                var useCaseSkip = new Clickra.Core.Modules.Pdf.Split.Application.SplitPdfUseCase();
                var resultSkip = useCaseSkip.ExecuteAsync(plan, new MockPasswordInteraction(PasswordPromptResult.Skip), progress: null).GetAwaiter().GetResult();
                Assert.True(resultSkip.Status == ConversionResultStatus.Succeeded, "Split must succeed with partial skips.");
                Assert.Equal(1L, (long)resultSkip.SkippedFiles.Count);
                Assert.Equal(files[1], resultSkip.SkippedFiles[0].Path);
            });

    private static void TestPdfToImageUnlockSuccessAndSkip() =>
        RunWithTempFilesAndOutDir(
            "p2i-test",
            () => [CreateTestPdf("p2i1", null), CreateTestPdf("p2i2", SampleUnlockValue), CreateTestPdf("p2i3", null)],
            (files, outDir) =>
            {
                var useCase = new Clickra.Core.Modules.Pdf.ToImage.Application.PdfToImageUseCase();
                var plan = CreateUnlockTestPlan(useCase, Clickra.Core.Modules.Pdf.ToImage.Application.PdfToImageUseCase.CommandName, files, outDir);
                var result = useCase.ExecuteAsync(plan, new MockPasswordInteraction(SampleUnlockValue), progress: null).GetAwaiter().GetResult();

                Assert.True(result.Status == ConversionResultStatus.Succeeded, "PDF to Image must succeed when password provided.");
                Assert.Equal(0L, (long)result.SkippedFiles.Count);

                var useCaseSkip = new Clickra.Core.Modules.Pdf.ToImage.Application.PdfToImageUseCase();
                var resultSkip = useCaseSkip.ExecuteAsync(plan, new MockPasswordInteraction(PasswordPromptResult.Skip), progress: null).GetAwaiter().GetResult();
                Assert.True(resultSkip.Status == ConversionResultStatus.Succeeded, "PDF to Image must succeed with partial skips.");
                Assert.Equal(1L, (long)resultSkip.SkippedFiles.Count);
                Assert.Equal(files[1], resultSkip.SkippedFiles[0].Path);
            });

    private static void TestDecryptPdfMixedWithUnencrypted() =>
        RunWithTempFilesAndOutDir(
            "dec-test",
            () => [CreateTestPdf("dec1", null), CreateTestPdf("dec2", SampleUnlockValue), CreateTestRestrictedPdf("dec3")],
            (files, outDir) =>
            {
                var useCase = new Clickra.Core.Modules.Pdf.Decrypt.Application.DecryptPdfUseCase();
                var plan = CreateUnlockTestPlan(useCase, Clickra.Core.Modules.Pdf.Decrypt.Application.DecryptPdfUseCase.CommandName, files, outDir);
                var result = useCase.ExecuteAsync(plan, new MockPasswordInteraction(SampleUnlockValue), progress: null).GetAwaiter().GetResult();

                Assert.True(result.Status == ConversionResultStatus.Succeeded, "Mixed decrypt must succeed.");
                Assert.Equal(1L, (long)result.SkippedFiles.Count);
                Assert.Equal(files[0], result.SkippedFiles[0].Path);
                Assert.True(result.SkippedFiles[0].Reason == ConversionSkipReason.NotEncrypted, "f1 must be skipped as NotEncrypted");
                Assert.True(File.Exists(plan.Outputs[1]), "f2 decrypted output must exist.");
                Assert.True(File.Exists(plan.Outputs[2]), "f3 decrypted output must exist.");
            });

    private static void TestDecryptPdfAllUnencrypted() =>
        RunWithTempFilesAndOutDir(
            "dec-none",
            () => [CreateTestPdf("dec_none1", null), CreateTestPdf("dec_none2", null)],
            (files, outDir) =>
            {
                var useCase = new Clickra.Core.Modules.Pdf.Decrypt.Application.DecryptPdfUseCase();
                var plan = CreateUnlockTestPlan(useCase, Clickra.Core.Modules.Pdf.Decrypt.Application.DecryptPdfUseCase.CommandName, files, outDir);
                var result = useCase.ExecuteAsync(plan, new MockPasswordInteraction(), progress: null).GetAwaiter().GetResult();

                Assert.True(result.Status == ConversionResultStatus.Failed, "All unencrypted files in decrypt must fail.");
                Assert.True(result.Error?.Contains(Localization.T("pdf_not_encrypted", "zh-TW"), StringComparison.Ordinal) == true ||
                            result.Error?.Contains(Localization.T("pdf_not_encrypted", "en-US"), StringComparison.Ordinal) == true,
                    $"Expected pdf_not_encrypted error, got: {result.Error}");
                Assert.Equal(2L, (long)result.SkippedFiles.Count);
                Assert.True(result.SkippedFiles.All(s => s.Reason == ConversionSkipReason.NotEncrypted), "All reasons must be NotEncrypted");
            });

    private static void TestSamePasswordAcrossTwoFilesAskedOnce() =>
        RunWithTempFilesAndOutDir(
            "same-pwd",
            () => [CreateTestPdf("same1", SampleUnlockValue), CreateTestPdf("same2", SampleUnlockValue)],
            (files, outDir) =>
            {
                var useCase = new Clickra.Core.Modules.Pdf.Compression.Application.CompressPdfUseCase();
                var plan = CreateUnlockTestPlan(useCase, Clickra.Core.Modules.Pdf.Compression.Application.CompressPdfUseCase.CommandName, files, outDir);
                var interaction = new MockPasswordInteraction(SampleUnlockValue);
                var result = useCase.ExecuteAsync(plan, interaction, progress: null).GetAwaiter().GetResult();

                Assert.True(result.Status == ConversionResultStatus.Succeeded, "Compression must succeed.");
                Assert.Equal(1L, (long)interaction.CallCount);
            });

    private static void TestSameUseCaseInstanceDoesNotShareCacheAcrossExecutions() =>
        RunWithTempFilesAndOutDir(
            "reuse-cache",
            () => [CreateTestPdf("reuse1", SampleUnlockValue)],
            (files, outDir) =>
            {
                var useCase = new Clickra.Core.Modules.Pdf.Compression.Application.CompressPdfUseCase();
                var plan = CreateUnlockTestPlan(useCase, Clickra.Core.Modules.Pdf.Compression.Application.CompressPdfUseCase.CommandName, files, outDir);

                var batch1 = useCase.ExecuteAsync(plan, new MockPasswordInteraction(SampleUnlockValue), progress: null).GetAwaiter().GetResult();
                Assert.True(batch1.Status == ConversionResultStatus.Succeeded, "First batch must succeed.");

                var skipInteraction = new MockPasswordInteraction(PasswordPromptResult.Skip);
                var batch2 = useCase.ExecuteAsync(plan, skipInteraction, progress: null).GetAwaiter().GetResult();
                Assert.True(batch2.Status == ConversionResultStatus.Failed, "Second batch on same instance must not reuse first batch password.");
                Assert.Equal(1L, (long)skipInteraction.CallCount);
            });

    private static void TestPdfUnlockStepPreservesOutlines()
    {
        string path = Path.Combine(Path.GetTempPath(), $"clickra-outline-{Guid.NewGuid():N}.pdf");
        try
        {
            using (var doc = new PdfDocument())
            {
                var page = doc.AddPage();
                doc.Outlines.Add("Chapter 1", page);
                doc.SecuritySettings.UserPassword = SampleUnlockValue;
                doc.Save(path);
            }

            var interaction = new MockPasswordInteraction(SampleUnlockValue);
            using var unlocked = PdfUnlockStep.UnlockAsync(0, path, interaction).GetAwaiter().GetResult();
            Assert.True(unlocked.Status == PdfUnlockStatus.Success, "Expected unlock success.");

            using var outDoc = PdfReader.Open(unlocked.EffectivePath, PdfDocumentOpenMode.Import);
            Assert.Equal(1L, (long)outDoc.Outlines.Count);
            Assert.Equal("Chapter 1", outDoc.Outlines[0].Title);
        }
        finally
        {
            TryDeleteDecryptFixture(path);
        }
    }

    private static void AssertPerFileUnlockBatchBehavior(
        string prefix,
        Func<IConversionUseCase> createUseCase,
        string command,
        bool checkOutputFilesExist,
        Action<int>? assertProcessedCount = null) =>
        RunWithTempFilesAndOutDir(
            prefix,
            () => [CreateTestPdf($"{prefix}1", null), CreateTestPdf($"{prefix}2", SampleUnlockValue), CreateTestPdf($"{prefix}3", null)],
            (files, outDir) =>
            {
                var okUseCase = createUseCase();
                var plan = CreateUnlockTestPlan(okUseCase, command, files, outDir);
                var okResult = okUseCase.ExecuteAsync(plan, new MockPasswordInteraction(SampleUnlockValue), progress: null).GetAwaiter().GetResult();
                Assert.True(okResult.Status == ConversionResultStatus.Succeeded, $"{command}: password batch must succeed.");
                Assert.Equal(0L, (long)okResult.SkippedFiles.Count);
                if (checkOutputFilesExist)
                {
                    Assert.Equal(3L, (long)okResult.Outputs.Count);
                    Assert.True(okResult.Outputs.All(File.Exists), $"{command}: all 3 outputs must exist.");
                }
                assertProcessedCount?.Invoke(3);

                var skipUseCase = createUseCase();
                var skipResult = skipUseCase.ExecuteAsync(plan, new MockPasswordInteraction(PasswordPromptResult.Skip), progress: null).GetAwaiter().GetResult();
                Assert.True(skipResult.Status == ConversionResultStatus.Succeeded, $"{command}: skip batch must succeed.");
                Assert.Equal(1L, (long)skipResult.SkippedFiles.Count);
                Assert.Equal(files[1], skipResult.SkippedFiles[0].Path);
                Assert.True(skipResult.SkippedFiles[0].Reason == ConversionSkipReason.NeedsPassword, $"{command}: expected NeedsPassword.");
                if (checkOutputFilesExist)
                {
                    Assert.Equal(2L, (long)skipResult.Outputs.Count);
                    Assert.True(skipResult.Outputs.All(File.Exists), $"{command}: remaining 2 outputs must exist.");
                }
                assertProcessedCount?.Invoke(2);

                var cancelUseCase = createUseCase();
                var cancelResult = cancelUseCase.ExecuteAsync(plan, new MockPasswordInteraction(PasswordPromptResult.Cancel), progress: null).GetAwaiter().GetResult();
                Assert.True(cancelResult.Status == ConversionResultStatus.Canceled, $"{command}: cancel batch must return Canceled.");
            });

    private static void TestCompressPdfUnlockBatch() =>
        AssertPerFileUnlockBatchBehavior(
            "cmp-batch",
            () => new Clickra.Core.Modules.Pdf.Compression.Application.CompressPdfUseCase(),
            Clickra.Core.Modules.Pdf.Compression.Application.CompressPdfUseCase.CommandName,
            checkOutputFilesExist: true);

    private static void TestTranslatePdfUnlockBatch()
    {
        string? previousEngine = Environment.GetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE");
        Environment.SetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE", "identity");
        try
        {
            AssertPerFileUnlockBatchBehavior(
                "trans-batch",
                () => new Clickra.Core.Modules.Pdf.Translation.Application.TranslatePdfUseCase(),
                Clickra.Core.Modules.Pdf.Translation.Application.TranslatePdfUseCase.CommandName,
                checkOutputFilesExist: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE", previousEngine);
        }
    }

    private static void TestPrintPdfUnlockBatch()
    {
        int printedCount = 0;
        var discovery = new MockPrinterDiscoveryForUnlockTest();
        AssertPerFileUnlockBatchBehavior(
            "prt-batch",
            () =>
            {
                printedCount = 0;
                return new Clickra.Core.Modules.Pdf.Print.Application.PrintPdfUseCase(
                    discovery,
                    (path, _, _, _) =>
                    {
                        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                        Assert.True(doc.PageCount > 0, "Printed PDF must be unlocked and readable.");
                        printedCount++;
                    });
            },
            Clickra.Core.Modules.Pdf.Print.Application.PrintPdfUseCase.CommandName,
            checkOutputFilesExist: false,
            expected => Assert.Equal((long)expected, (long)printedCount));
    }

    private sealed class MockPrinterDiscoveryForUnlockTest : Clickra.Core.Modules.Pdf.Print.Ports.IPrinterDiscoveryService
    {
        public string? GetDefaultPrinter() => "MockPrinter";
        public bool IsPrinterInstalled(string printerName) => true;
        public IReadOnlyList<string> GetInstalledPrinters() => ["MockPrinter"];
        public IReadOnlyList<string> GetSupportedPaperSizes(string? printerName = null) => ["A4"];
        public bool CanDuplex(string? printerName = null) => false;
    }
}

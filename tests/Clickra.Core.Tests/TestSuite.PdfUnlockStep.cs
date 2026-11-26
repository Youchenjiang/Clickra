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
        private readonly Queue<string?> _responses;

        public MockPasswordInteraction(params string?[] responses)
        {
            _responses = new Queue<string?>(responses);
        }

        public int CallCount { get; private set; }
        public List<bool> RetryFlags { get; } = new();

        public Task<string?> RequestPasswordAsync(int fileIndex, string inputPath, bool isRetry, CancellationToken cancellationToken)
        {
            CallCount++;
            RetryFlags.Add(isRetry);
            return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : null);
        }

        public Task<string?> RequestSplitPagesAsync(int fileIndex, string inputPath, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyDictionary<string, object>?> RequestMarkdownOptionsAsync(string command, IReadOnlyList<string> inputFiles, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, object>?>(null);
    }
}

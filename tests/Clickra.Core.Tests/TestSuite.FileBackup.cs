using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterFileBackupTests(TestRunner runner)
    {
        runner.Run("Processor output rollback restores an overwritten file after failure", () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"clickra-output-rollback-{Guid.NewGuid():N}");
            string input = Path.Combine(tempDir, "input.txt");
            string output = Path.Combine(tempDir, "output.txt");

            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(input, "input");
                File.WriteAllText(output, "Original Precious Data");

                var processor = new TestOverwriteProcessor(throwAfterWrite: true);
                Assert.Throws<InvalidOperationException>(() =>
                    processor.Process(new List<string> { input }, output));

                Assert.Equal("Original Precious Data", File.ReadAllText(output));
                Assert.True(
                    Directory.GetFiles(tempDir, "output.txt.clickra_bak_*").Length == 0,
                    "Rollback must not leave a temporary backup behind.");
            }
            finally
            {
                DeleteTestDirectoryBestEffort(tempDir);
            }
        });

        runner.Run("Processor output rollback commits a successful replacement", () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"clickra-output-commit-{Guid.NewGuid():N}");
            string input = Path.Combine(tempDir, "input.txt");
            string output = Path.Combine(tempDir, "output.txt");

            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(input, "input");
                File.WriteAllText(output, "Original Precious Data");

                var processor = new TestOverwriteProcessor(throwAfterWrite: false);
                processor.Process(new List<string> { input }, output);

                Assert.Equal("New Successful Output", File.ReadAllText(output));
                Assert.True(
                    Directory.GetFiles(tempDir, "output.txt.clickra_bak_*").Length == 0,
                    "Commit must remove the temporary backup.");
            }
            finally
            {
                DeleteTestDirectoryBestEffort(tempDir);
            }
        });

        runner.Run("Processor output rollback refuses an unprotected overwrite", () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"clickra-output-backup-failure-{Guid.NewGuid():N}");
            string output = Path.Combine(tempDir, "output.txt");

            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(output, "Original Precious Data");

                using var lockedOutput = new FileStream(
                    output,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None);

                Assert.Throws<IOException>(() => FileBackupScope.Create(output));
                Assert.True(
                    Directory.GetFiles(tempDir, "output.txt.clickra_bak_*").Length == 0,
                    "A failed backup attempt must not leave a misleading recovery file behind.");
            }
            finally
            {
                DeleteTestDirectoryBestEffort(tempDir);
            }
        });

        runner.Run("Processor output rollback preserves recovery copy when restore fails", () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"clickra-output-restore-failure-{Guid.NewGuid():N}");
            string output = Path.Combine(tempDir, "output.txt");

            Directory.CreateDirectory(tempDir);
            FileBackupScope? scope = null;
            try
            {
                File.WriteAllText(output, "Original Precious Data");
                scope = FileBackupScope.Create(output);
                File.WriteAllText(output, "Partial Replacement");

                using (var lockedOutput = new FileStream(
                           output,
                           FileMode.Open,
                           FileAccess.ReadWrite,
                           FileShare.None))
                {
                    IOException restoreFailure = Assert.Throws<IOException>(() => scope.Rollback());
                    Assert.True(
                        restoreFailure.Message.Contains("Recovery backup retained", StringComparison.Ordinal),
                        $"Rollback failure must identify the retained recovery artifact, got: {restoreFailure.Message}");
                    Assert.True(
                        Directory.GetFiles(tempDir, "output.txt.clickra_bak_*").Length == 1,
                        "A failed restore must retain exactly one recovery backup.");
                }

                scope.Rollback();
                Assert.Equal("Original Precious Data", File.ReadAllText(output));
                Assert.True(
                    Directory.GetFiles(tempDir, "output.txt.clickra_bak_*").Length == 0,
                    "A successful retry must remove the recovery backup.");
            }
            finally
            {
                scope?.Dispose();
                DeleteTestDirectoryBestEffort(tempDir);
            }
        });

        runner.Run("Processor output commit reports retained backup cleanup failure", () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"clickra-output-cleanup-failure-{Guid.NewGuid():N}");
            string output = Path.Combine(tempDir, "output.txt");

            Directory.CreateDirectory(tempDir);
            FileBackupScope? scope = null;
            try
            {
                File.WriteAllText(output, "Original Precious Data");
                scope = FileBackupScope.Create(output);
                File.WriteAllText(output, "New Successful Output");

                string backup = Directory.GetFiles(tempDir, "output.txt.clickra_bak_*").Single();
                using (var lockedBackup = new FileStream(
                           backup,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.None))
                {
                    IOException cleanupFailure = Assert.Throws<IOException>(() => scope.Commit());
                    Assert.True(
                        cleanupFailure.Message.Contains("recovery backup could not be removed", StringComparison.Ordinal),
                        $"Commit cleanup failure must identify the retained backup, got: {cleanupFailure.Message}");
                }

                scope.Dispose();
                scope = null;

                Assert.Equal("New Successful Output", File.ReadAllText(output));
                Assert.True(File.Exists(backup), "Commit cleanup failure must leave the recovery backup intact.");
                File.Delete(backup);
            }
            finally
            {
                scope?.Dispose();
                DeleteTestDirectoryBestEffort(tempDir);
            }
        });
    }

    private static void DeleteTestDirectoryBestEffort(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Test cleanup could not remove '{tempDir}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Test cleanup could not access '{tempDir}': {ex.Message}");
        }
    }

    private sealed class TestOverwriteProcessor(bool throwAfterWrite) : SingleFileProcessorBase
    {
        protected override string GetOutputSuffix() => ".out";

        protected override void ProcessSingleFile(
            string fullPath,
            string targetOutputPath,
            int fileIndex,
            int totalFiles,
            Dictionary<string, object>? options,
            Action<int, int, string>? onProgress,
            CancellationToken cancellationToken)
        {
            File.WriteAllText(targetOutputPath, "New Successful Output");
            if (throwAfterWrite)
                throw new InvalidOperationException("Simulated processor failure.");
        }
    }
}

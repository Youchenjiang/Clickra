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
                try { Directory.Delete(tempDir, recursive: true); } catch { }
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
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        });
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core.Application;
using Clickra.Core.Diagnostics;
using Clickra.Core.Modules.Pdf.Compression.Application;
using Clickra.Core.Storage;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string SampleRedactionToken = "tok-xyz-123";

    public static void RegisterClickraLogTests(TestRunner runner)
    {
        runner.Run("ClickraLog: redacts paths, secrets, query strings, and sensitive options", TestClickraLogRedaction);
        runner.Run("ClickraLog: unwraps nested, aggregate, and external process exceptions", TestClickraLogExceptionUnwrapping);
        runner.Run("ClickraLog: rotates at 5 MB and switches file on date change", TestClickraLogRotation);
        runner.Run("ClickraLog: cleans logs older than 14 days while keeping recent and non-log files", TestClickraLogRetentionCleanup);
        runner.Run("ClickraLog: concurrent writes produce 1000 complete non-interleaved entries", TestClickraLogConcurrency);
        runner.Run("ClickraLog: write failure does not throw", TestClickraLogWriteFailureSafe);
        runner.Run("ClickraLog: per-file failure logs redacted nested exception", TestClickraLogPerFileFailureLogsRedactedNestedException);
        runner.Run("ClickraLog: password-protected PDF does not leak secret to logs or storage", TestClickraLogPasswordProtectedPdfDoesNotLeakSecret);
    }

    private static void TestClickraLogRedaction()
    {
        var ctx = new RedactionContext(
            KnownPaths: [@"C:\Users\Alice\Documents\SecretReport.pdf", @"D:\Out\SecretReport_compressed.pdf"],
            Secrets: [SampleRedactionToken]);

        string raw = @"Failed C:\Users\Alice\Documents\SecretReport.pdf -> D:\Out\SecretReport_compressed.pdf; other E:\Corp\Private.docx and \\fileserver\share\Team.xlsx; key=tok-xyz-123; url=https://api.example.com/v1/chat?apiKey=abc";
        string redacted = ClickraLog.Redact(raw, ctx);

        Assert.False(redacted.Contains(@"C:\Users\Alice", StringComparison.OrdinalIgnoreCase), "Known input dir must be redacted.");
        Assert.False(redacted.Contains(@"D:\Out", StringComparison.OrdinalIgnoreCase), "Known output dir must be redacted.");
        Assert.True(redacted.Contains("SecretReport.pdf", StringComparison.Ordinal), "Known input filename must remain.");
        Assert.True(redacted.Contains("SecretReport_compressed.pdf", StringComparison.Ordinal), "Known output filename must remain.");
        Assert.True(redacted.Contains(@"<path>\Private.docx", StringComparison.Ordinal), "Other drive path must become <path>\\filename.");
        Assert.True(redacted.Contains(@"<path>\Team.xlsx", StringComparison.Ordinal), "UNC path must become <path>\\filename.");
        Assert.False(redacted.Contains(SampleRedactionToken, StringComparison.Ordinal), "Secret token must be redacted.");
        Assert.True(redacted.Contains("***", StringComparison.Ordinal), "Secret token must be replaced with ***.");
        Assert.False(redacted.Contains("apiKey=abc", StringComparison.Ordinal), "URL query string must be stripped.");

        var opts = new Dictionary<string, object>
        {
            ["targetLang"] = "zh-TW",
            ["aiApiKey"] = SampleRedactionToken,
            ["userToken"] = "abc",
            ["pdfPassword"] = "1234"
        };
        string summary = ClickraLog.FormatOptionsSummary(opts);
        Assert.True(summary.Contains("targetLang=zh-TW", StringComparison.Ordinal), "Non-sensitive option must remain.");
        Assert.False(summary.Contains(SampleRedactionToken, StringComparison.Ordinal), "Sensitive option value must be redacted.");
        Assert.True(summary.Contains("aiApiKey=***", StringComparison.Ordinal), "aiApiKey must be ***.");
        Assert.True(summary.Contains("userToken=***", StringComparison.Ordinal), "userToken must be ***.");
        Assert.True(summary.Contains("pdfPassword=***", StringComparison.Ordinal), "pdfPassword must be ***.");
    }

    private static void TestClickraLogExceptionUnwrapping()
    {
        string logDir = CreateTempLogDir();
        try
        {
            var inner2 = new InvalidDataException("level-2 failure");
            inner2.Data["ExitCode"] = 42;
            inner2.Data["StdErr"] = new string('X', 3000);
            var inner1 = new IOException("level-1 failure", inner2);
            var agg = new AggregateException("top aggregate", inner1, new TimeoutException("branch-2 timeout"));

            ClickraLog.EnqueueForTests("ERROR", "test-ex", "operation failed", agg, logDir, new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.FromHours(8)));
            ClickraLog.Flush();

            string logFile = Path.Combine(logDir, "clickra-2026-11-28.log");
            Assert.True(File.Exists(logFile), "Expected log file to be created.");
            string content = File.ReadAllText(logFile);
            Assert.True(content.Contains("level-1 failure", StringComparison.Ordinal), "Must contain inner1.");
            Assert.True(content.Contains("level-2 failure", StringComparison.Ordinal), "Must contain inner2.");
            Assert.True(content.Contains("branch-2 timeout", StringComparison.Ordinal), "Must contain AggregateException branch 2.");
            Assert.True(content.Contains("exit code: 42", StringComparison.Ordinal), "Must contain exit code.");
            Assert.True(content.Contains("...(truncated)", StringComparison.Ordinal), "External output > 2 KB must be truncated.");
        }
        finally
        {
            DeleteTempLogDir(logDir);
        }
    }

    private static void TestClickraLogRotation()
    {
        string logDir = CreateTempLogDir();
        try
        {
            var day1 = new DateTime(2026, 11, 28);
            string primary = Path.Combine(logDir, "clickra-2026-11-28.log");
            File.WriteAllBytes(primary, new byte[ClickraLog.MaxFileBytes]);

            string rotated = ClickraLog.ResolveLogFilePathForWrite(logDir, day1, 100, ClickraLog.MaxFileBytes);
            Assert.Equal(Path.Combine(logDir, "clickra-2026-11-28.1.log"), rotated);

            var day2 = new DateTime(2026, 11, 29);
            string nextDay = ClickraLog.ResolveLogFilePathForWrite(logDir, day2, 100, ClickraLog.MaxFileBytes);
            Assert.Equal(Path.Combine(logDir, "clickra-2026-11-29.log"), nextDay);
        }
        finally
        {
            DeleteTempLogDir(logDir);
        }
    }

    private static void TestClickraLogRetentionCleanup()
    {
        string logDir = CreateTempLogDir();
        try
        {
            string oldLog = Path.Combine(logDir, "clickra-2026-11-01.log");
            string recentLog = Path.Combine(logDir, "clickra-2026-11-27.log");
            string nonLog = Path.Combine(logDir, "notes.txt");

            File.WriteAllText(oldLog, "old");
            File.WriteAllText(recentLog, "recent");
            File.WriteAllText(nonLog, "keep");

            DateTime now = new(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(oldLog, now.AddDays(-15));
            File.SetLastWriteTimeUtc(recentLog, now.AddDays(-5));
            File.SetLastWriteTimeUtc(nonLog, now.AddDays(-30));

            ClickraLog.CleanExpiredLogs(logDir, TimeSpan.FromDays(14), now);

            Assert.False(File.Exists(oldLog), "Logs older than 14 days must be deleted.");
            Assert.True(File.Exists(recentLog), "Recent logs must be preserved.");
            Assert.True(File.Exists(nonLog), "Non-log files must not be deleted.");
        }
        finally
        {
            DeleteTempLogDir(logDir);
        }
    }

    private static void TestClickraLogConcurrency()
    {
        string logDir = CreateTempLogDir();
        try
        {
            var stamp = new DateTimeOffset(2026, 11, 28, 11, 0, 0, TimeSpan.FromHours(8));
            Task t1 = Task.Run(() =>
            {
                for (int i = 0; i < 500; i++)
                {
                    ClickraLog.EnqueueForTests("INFO", "t1", $"msg-a-{i}", null, logDir, stamp);
                }
            });
            Task t2 = Task.Run(() =>
            {
                for (int i = 0; i < 500; i++)
                {
                    ClickraLog.EnqueueForTests("INFO", "t2", $"msg-b-{i}", null, logDir, stamp);
                }
            });

            Task.WaitAll(t1, t2);
            ClickraLog.Flush(TimeSpan.FromSeconds(10));

            string logFile = Path.Combine(logDir, "clickra-2026-11-28.log");
            string[] lines = File.ReadAllLines(logFile);
            int headerCount = lines.Count(l => l.StartsWith("2026-11-28 11:00:00.000", StringComparison.Ordinal));
            Assert.Equal(1000L, (long)headerCount);
            Assert.Equal(2000L, (long)lines.Length);
        }
        finally
        {
            DeleteTempLogDir(logDir);
        }
    }

    private static void TestClickraLogWriteFailureSafe()
    {
        string badPath = Path.Combine(Path.GetTempPath(), $"clickra-bad-log-{Guid.NewGuid():N}");
        File.WriteAllText(badPath, "file-not-directory");
        try
        {
            ClickraLog.EnqueueForTests("ERROR", "fail-safe", "must not throw", null, badPath);
            ClickraLog.Flush();
        }
        finally
        {
            try { File.Delete(badPath); }
            catch { /* Best-effort cleanup */ }
        }
    }

    private static string CreateTempLogDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"clickra-log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteTempLogDir(string dir)
    {
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, true); }
            catch { /* Best-effort test log directory cleanup */ }
        }
    }

    private static void TestClickraLogPerFileFailureLogsRedactedNestedException()
    {
        string tempRoot = CreateTempLogDir();
        string inputDir = Path.Combine(tempRoot, "secret-input-dir");
        string outputDir = Path.Combine(tempRoot, "secret-output-dir");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);
        string inputFile = Path.Combine(inputDir, "SecretFailureInput.pdf");
        File.WriteAllText(inputFile, "stub");

        try
        {
            string logFile = Path.Combine(ClickraLog.LogsDirectory, $"clickra-{DateTimeOffset.Now:yyyy-MM-dd}.log");
            ClickraLog.Flush();
            long offset = File.Exists(logFile) ? new FileInfo(logFile).Length : 0;

            var useCase = new FailingNestedPerFileUseCase();
            var plan = new ConversionPlan(
                useCase.Command,
                [inputFile],
                [Path.Combine(outputDir, "SecretFailureInput_out.pdf")],
                new HashSet<ConversionCapability>(),
                new Dictionary<string, object>(StringComparer.Ordinal),
                TrackTaskLifecycle: false);

            ConversionResult result = useCase
                .ExecuteAsync(plan, new MockPasswordInteraction(string.Empty), progress: null)
                .GetAwaiter()
                .GetResult();
            ClickraLog.Flush();

            Assert.True(result.Status == ConversionResultStatus.Failed, "UseCase must report Failed.");
            Assert.True(File.Exists(logFile), "Expected today's ClickraLog file to exist.");
            string appended = ReadFileTail(logFile, offset);

            Assert.True(appended.Contains(" ERROR ", StringComparison.Ordinal), "Log must contain ERROR entry.");
            Assert.True(appended.Contains(useCase.Command, StringComparison.Ordinal), "Log must contain command.");
            Assert.True(appended.Contains("1/1", StringComparison.Ordinal), "Log must contain file index 1/1.");
            Assert.True(appended.Contains("SecretFailureInput.pdf", StringComparison.Ordinal), "Log must retain filename.");
            Assert.True(appended.Contains("inner cause", StringComparison.Ordinal), "Log must contain inner exception.");
            Assert.True(appended.Contains("root cause", StringComparison.Ordinal), "Log must contain nested root exception.");
            Assert.False(appended.Contains(inputDir, StringComparison.OrdinalIgnoreCase), "Input directory path must be redacted.");
            Assert.False(appended.Contains(outputDir, StringComparison.OrdinalIgnoreCase), "Output directory path must be redacted.");
        }
        finally
        {
            DeleteTempLogDir(tempRoot);
        }
    }

    private static void TestClickraLogPasswordProtectedPdfDoesNotLeakSecret() =>
        RunWithTempFilesAndOutDir(
            "log-pwd-redact",
            () => [CreateTestPdf("pwd-log", SampleUnlockValue)],
            (files, outDir) =>
            {
                ClickraLog.Flush();
                var useCase = new CompressPdfUseCase();
                ConversionPlan plan = CreateUnlockTestPlan(useCase, CompressPdfUseCase.CommandName, files, outDir);
                ConversionResult result = useCase
                    .ExecuteAsync(plan, new MockPasswordInteraction(SampleUnlockValue), progress: null)
                    .GetAwaiter()
                    .GetResult();
                ClickraLog.Flush();

                Assert.True(result.Status == ConversionResultStatus.Succeeded, "Password-protected compression must succeed.");

                if (Directory.Exists(ClickraLog.LogsDirectory))
                {
                    foreach (string logFile in Directory.EnumerateFiles(ClickraLog.LogsDirectory, "*.log"))
                    {
                        string content = ReadFileTail(logFile, 0);
                        Assert.False(
                            content.Contains(SampleUnlockValue, StringComparison.Ordinal),
                            $"Secret leaked into log file: {Path.GetFileName(logFile)}");
                    }
                }

                if (File.Exists(StorageRuntime.HistoryFilePath))
                {
                    string history = ReadFileTail(StorageRuntime.HistoryFilePath, 0);
                    Assert.False(
                        history.Contains(SampleUnlockValue, StringComparison.Ordinal),
                        "Secret leaked into history store.");
                }

                if (Directory.Exists(StorageRuntime.TasksDirectory))
                {
                    foreach (string taskFile in Directory.EnumerateFiles(StorageRuntime.TasksDirectory, "*", SearchOption.AllDirectories))
                    {
                        string taskContent = ReadFileTail(taskFile, 0);
                        Assert.False(
                            taskContent.Contains(SampleUnlockValue, StringComparison.Ordinal),
                            $"Secret leaked into task store: {Path.GetFileName(taskFile)}");
                    }
                }
            });

    private static string ReadFileTail(string path, long offset)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(Math.Min(offset, fs.Length), SeekOrigin.Begin);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    private sealed class FailingNestedPerFileUseCase : PerFileConversionUseCaseBase
    {
        public override string Command => CompressPdfUseCase.CommandName;
        protected override string UseCaseName => nameof(FailingNestedPerFileUseCase);
        protected override string InputRequirementError => "input required";
        protected override string UnsupportedInputError(string path) => path;
        protected override string OutputCountError => "output mismatch";
        protected override string GetOutputFileName(string input) =>
            Path.GetFileNameWithoutExtension(input) + "_out.pdf";

        protected override Task ExecuteFileAsync(
            int index,
            ConversionPlan plan,
            IConversionInteraction interaction,
            IProgress<ConversionProgress>? progress,
            object? executionState,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "outer failure",
                new IOException("inner cause", new UnauthorizedAccessException("root cause")));
    }
}

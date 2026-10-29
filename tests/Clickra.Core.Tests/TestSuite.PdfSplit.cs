using Clickra.Core;
using Clickra.Core.Application;
using Clickra.Core.Modules.PdfSplit.Application;
using Clickra.Core.Modules.PdfSplit.Processing;
using Clickra.Core.Processors;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterPdfSplitTests(TestRunner runner)
    {
        runner.Run("PDF page-count query delegates unreadable and valid document semantics", () =>
        {
            string inputPath = CreateTempPdf($"clickra-page-count-{Guid.NewGuid():N}", 3);
            try
            {
                Assert.True(PdfPageCountQuery.Get(inputPath) == 3,
                    "Application page-count query must preserve the processor's exact valid-document count.");
                Assert.True(PdfPageCountQuery.Get(inputPath + ".missing") == 0,
                    "Application page-count query must preserve the processor's unreadable-document zero result.");
            }
            finally
            {
                DeleteTempFiles(inputPath);
            }
        });

        runner.Run("PdfSplitProcessor parses page ranges accurately", () =>
        {
            var r1 = PdfSplitProcessor.ParsePageRange("1-3", 10);
            Assert.True(r1.SequenceEqual(new[] { 1, 2, 3 }), "Expected 1-3 to yield pages 1, 2, 3.");

            var r2 = PdfSplitProcessor.ParsePageRange("1, 5, 8-10", 10);
            Assert.True(r2.SequenceEqual(new[] { 1, 5, 8, 9, 10 }), "Expected 1, 5, 8-10 to yield pages 1, 5, 8, 9, 10.");

            var r3 = PdfSplitProcessor.ParsePageRange("12-15", 5);
            Assert.True(r3.Count == 0, "Expected out of bound range to yield empty list.");
        });

        runner.Run("PdfSplitProcessor extracts specified page ranges to new document", () =>
        {
            string inputPath = CreateTempPdf($"clickra-split-src-{Guid.NewGuid():N}", 3);
            string outputPath = Path.Combine(Path.GetTempPath(), $"clickra-split-out-{Guid.NewGuid():N}.pdf");

            try
            {
                new PdfSplitProcessor().Process(new List<string> { inputPath }, outputPath, new Dictionary<string, object> { ["pages"] = "1-2" });

                Assert.True(File.Exists(outputPath), "Expected output split PDF file to exist.");
                using var outDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
                Assert.True(outDoc.PageCount == 2, $"Expected output PDF to have 2 pages, got {outDoc.PageCount}.");
            }
            finally
            {
                DeleteTempFiles(inputPath, outputPath);
            }
        });

        runner.Run("PdfSplitProcessor.BuildSegmentSpec builds custom segment spec (mode 0)", () =>
        {
            var segs = new List<(int Start, int End)> { (1, 2), (3, 3), (7, 9) };
            var spec = PdfSplitProcessor.BuildSegmentSpec(0, 5, 10, segs);
            Assert.Equal("1-2; 3; 7-9", spec);

            var empty = PdfSplitProcessor.BuildSegmentSpec(0, 5, 10, new List<(int, int)>());
            Assert.Equal("all", empty);
        });

        runner.Run("PdfSplitProcessor.BuildSegmentSpec returns all in split-each mode (mode 1)", () =>
        {
            var spec = PdfSplitProcessor.BuildSegmentSpec(1, 5, 10, new List<(int, int)> { (1, 10) });
            Assert.Equal("all", spec);
        });

        runner.Run("PdfSplitProcessor.BuildSegmentSpec builds fixed-page spec (mode 2)", () =>
        {
            var spec = PdfSplitProcessor.BuildSegmentSpec(2, 3, 7, new List<(int, int)>());
            Assert.Equal("1-3; 4-6; 7", spec);

            var oversized = PdfSplitProcessor.BuildSegmentSpec(2, 9, 7, new List<(int, int)>());
            Assert.Equal("1-7", oversized);

            var clamped = PdfSplitProcessor.BuildSegmentSpec(2, 0, 7, new List<(int, int)>());
            Assert.Equal("1; 2; 3; 4; 5; 6; 7", clamped);
        });

        runner.Run("PdfSplitSpecBuilder.BuildSegmentSpec builds specifications in application layer", () =>
        {
            var segs = new List<(int Start, int End)> { (1, 2), (3, 3), (7, 9) };
            Assert.Equal("1-2; 3; 7-9", PdfSplitSpecBuilder.BuildSegmentSpec(0, 5, 10, segs));
            Assert.Equal("all", PdfSplitSpecBuilder.BuildSegmentSpec(1, 5, 10, segs));
            Assert.Equal("1-3; 4-6; 7", PdfSplitSpecBuilder.BuildSegmentSpec(2, 3, 7, new List<(int, int)>()));
        });

        runner.Run("PdfSplitProcessor emits one output file per multi-segment spec", () =>
        {
            string baseName = $"clickra-multisplit-{Guid.NewGuid():N}";
            string inputPath = CreateTempPdf(baseName, 5);
            string outputPath = Path.Combine(Path.GetTempPath(), $"{baseName}_target.pdf");
            string seg1Path = Path.Combine(Path.GetTempPath(), $"{baseName}_1-2.pdf");
            string seg2Path = Path.Combine(Path.GetTempPath(), $"{baseName}_4-4.pdf");

            try
            {
                new PdfSplitProcessor().Process(new List<string> { inputPath }, outputPath, new Dictionary<string, object> { ["pages"] = "1-2; 4" });

                Assert.True(File.Exists(seg1Path), "Expected segment 1-2 output file to exist.");
                Assert.True(File.Exists(seg2Path), "Expected segment 4 output file to exist.");
                Assert.True(!File.Exists(outputPath), "Multi-segment split should not write the single target output path.");

                using var s1 = PdfReader.Open(seg1Path, PdfDocumentOpenMode.Import);
                Assert.True(s1.PageCount == 2, $"Expected segment 1-2 to have 2 pages, got {s1.PageCount}.");
                using var s2 = PdfReader.Open(seg2Path, PdfDocumentOpenMode.Import);
                Assert.True(s2.PageCount == 1, $"Expected segment 4 to have 1 page, got {s2.PageCount}.");
            }
            finally
            {
                DeleteTempFiles(inputPath, outputPath, seg1Path, seg2Path);
            }
        });

        runner.Run("Split use case owns planning and page interaction", TestSplitUseCaseOwnsWorkflow);
        runner.Run("Split use case accepts headless page ranges without interaction", TestSplitUseCaseAcceptsFixedPages);
        runner.Run("PDF split detects password protection and extracts pages with password", () =>
        {
            string input = Path.Combine(Path.GetTempPath(), $"clickra-split-enc-{Guid.NewGuid():N}.pdf");
            string output = Path.Combine(Path.GetTempPath(), $"clickra-split-enc-out-{Guid.NewGuid():N}.pdf");
            const string password = "clickra-split-pass";

            try
            {
                using (var doc = new PdfDocument())
                {
                    doc.AddPage();
                    doc.AddPage();
                    doc.SecuritySettings.UserPassword = password;
                    doc.SecuritySettings.OwnerPassword = "clickra-owner-pass";
                    doc.Save(input);
                }

                Assert.True(PdfSplitProcessor.IsPasswordProtected(input), "Encrypted PDF must report IsPasswordProtected = true.");
                Assert.True(PdfPageCountQuery.Get(input) == 0, "Querying page count without password must return 0.");
                Assert.True(PdfPageCountQuery.Get(input, password) == 2, "Querying page count with password must return 2.");

                var model = VisualSplitModel.FromFile(input, password);
                Assert.True(model.TotalPages == 2, "VisualSplitModel must reflect the unlocked page count.");

                new PdfSplitProcessor().Process(
                    new List<string> { input },
                    output,
                    new Dictionary<string, object>
                    {
                        ["pages"] = "1",
                        ["password"] = password
                    });

                Assert.True(File.Exists(output), "Output split PDF must exist.");
                using var outDoc = PdfReader.Open(output, PdfDocumentOpenMode.Import);
                Assert.True(outDoc.PageCount == 1, "Output PDF must contain the split page.");
            }
            finally
            {
                DeleteTempFiles(input, output);
            }
        });

        runner.Run("Split use case prompts for password on encrypted PDF", () =>
        {
            string input = Path.Combine(Path.GetTempPath(), $"clickra-split-enc-uc-{Guid.NewGuid():N}.pdf");
            string outputDir = Path.Combine(Path.GetTempPath(), $"clickra-split-enc-uc-out-{Guid.NewGuid():N}");
            const string password = "clickra-split-uc-pass";

            try
            {
                Directory.CreateDirectory(outputDir);
                using (var doc = new PdfDocument())
                {
                    doc.AddPage();
                    doc.AddPage();
                    doc.SecuritySettings.UserPassword = password;
                    doc.SecuritySettings.OwnerPassword = "clickra-owner-pass";
                    doc.Save(input);
                }

                var useCase = new SplitPdfUseCase();
                ConversionPlan plan = useCase.Plan(new ConversionRequest(
                    SplitPdfUseCase.CommandName,
                    new[] { input },
                    OutputOverride: outputDir,
                    TrackTaskLifecycle: false));

                var interaction = new SplitPagesInteraction("1", password);
                ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress: null)
                    .GetAwaiter()
                    .GetResult();

                Assert.True(result.Status == ConversionResultStatus.Succeeded,
                    $"Expected split success, got {result.Status}: {result.Error}");
                Assert.True(interaction.Requests == 1, "Must request page spec.");
                Assert.True(interaction.PasswordRequests == 1, "Must request password when encrypted.");
                Assert.True(File.Exists(plan.Outputs[0]), "Split output file must exist.");
            }
            finally
            {
                DeleteTempFiles(input);
                try { Directory.Delete(outputDir, recursive: true); } catch { }
            }
        });
        RegisterSplitFailureTests(runner);
    }

    private static void TestSplitUseCaseOwnsWorkflow()
    {
        string input = CreateTempPdf($"clickra-split-usecase-{Guid.NewGuid():N}", 2);
        string outputDir = Path.Combine(Path.GetTempPath(), $"clickra-split-usecase-out-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outputDir);
            var useCase = new SplitPdfUseCase();
            ConversionPlan plan = useCase.Plan(new ConversionRequest(
                SplitPdfUseCase.CommandName,
                new[] { input },
                OutputOverride: outputDir,
                TrackTaskLifecycle: false));

            Assert.True(plan.RequiredCapabilities.SetEquals(new[] { ConversionCapability.SplitPages }),
                "Interactive split planning must declare the split-pages capability.");

            var interaction = new SplitPagesInteraction("1");
            ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress: null)
                .GetAwaiter()
                .GetResult();

            Assert.True(result.Status == ConversionResultStatus.Succeeded,
                $"Expected split success, got {result.Status}: {result.Error}");
            Assert.True(interaction.Requests == 1,
                "Interactive split must request one page spec per input file.");
            Assert.True(File.Exists(plan.Outputs[0]),
                "Split use case must produce the planned output file.");
        }
        finally
        {
            DeleteTempFiles(input);
            try { Directory.Delete(outputDir, recursive: true); } catch { /* Best-effort fixture cleanup. */ }
        }
    }

    private static void TestSplitUseCaseAcceptsFixedPages()
    {
        string input = Path.Combine(Path.GetTempPath(), "split-plan-source", "source.pdf");
        string outputDir = Path.Combine(Path.GetTempPath(), $"clickra-split-plan-{Guid.NewGuid():N}");
        var useCase = new SplitPdfUseCase();
        ConversionPlan plan = useCase.Plan(new ConversionRequest(
            SplitPdfUseCase.CommandName,
            new[] { input },
            new Dictionary<string, object> { [SplitPdfUseCase.PagesOptionKey] = "1-3" },
            OutputOverride: outputDir,
            TrackTaskLifecycle: false));

        Assert.True(plan.RequiredCapabilities.Count == 0,
            "A supplied page range must not require visual split interaction.");
        Assert.Equal("1-3", (string)plan.NormalizedOptions[SplitPdfUseCase.PagesOptionKey]);
        Assert.Equal(
            Path.Combine(Path.GetFullPath(outputDir), "source_split.pdf"),
            plan.Outputs.Single());
    }

    private sealed class SplitPagesInteraction(string pages, string? password = null) : IConversionInteraction
    {
        public int Requests { get; private set; }
        public int PasswordRequests { get; private set; }

        public Task<string?> RequestPasswordAsync(
            int fileIndex,
            string inputPath,
            bool isRetry,
            CancellationToken cancellationToken)
        {
            PasswordRequests++;
            return Task.FromResult<string?>(password);
        }

        public Task<string?> RequestSplitPagesAsync(
            int fileIndex,
            string inputPath,
            CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult<string?>(pages);
        }

        public Task<IReadOnlyDictionary<string, object>?> RequestMarkdownOptionsAsync(
            string command,
            IReadOnlyList<string> inputFiles,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>Registers the split tests that assert loud failures on invalid input.</summary>
    private static void RegisterSplitFailureTests(TestRunner runner)
    {
        runner.Run("PdfSplitProcessor multi-segment split fails loudly on out-of-range segment",
            () => AssertSplitFails("1-2; 99", "Expected an out-of-range multi-segment spec to throw instead of silently succeeding."));

        runner.Run("PdfSplitProcessor multi-segment split rejects colliding output names",
            () => AssertSplitFails("1-2; 1,2", "Expected colliding segment output names to throw instead of overwriting."));
    }

    /// <summary>Asserts that splitting a 3-page temp PDF with the given spec throws.</summary>
    /// <remarks>Everything lives in one unique temp directory so the segment
    /// outputs SplitPdf derives from the spec cannot leak into the repo cwd
    /// (a relative "unused.pdf" output path used to leave
    /// clickra-split-fail-*_1-2.pdf files behind); the whole directory is
    /// deleted in finally.</remarks>
    private static void AssertSplitFails(string spec, string failureMessage)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"clickra-split-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string inputPath = CreateTempPdf(tempDir, "input.pdf", 3);
            bool threw = false;
            try
            {
                new PdfSplitProcessor().Process(new List<string> { inputPath }, Path.Combine(tempDir, "unused.pdf"), new Dictionary<string, object> { ["pages"] = spec });
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            Assert.True(threw, failureMessage);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; temp files are git-ignored anyway.
            }
        }
    }

    /// <summary>Creates a temporary blank PDF in the given directory with the given page count.</summary>
    private static string CreateTempPdf(string directory, string fileName, int pageCount)
    {
        string inputPath = Path.Combine(directory, fileName);
        using (var doc = new PdfDocument())
        {
            for (int i = 0; i < pageCount; i++)
            {
                doc.AddPage();
            }
            doc.Save(inputPath);
        }
        return inputPath;
    }

    /// <summary>Creates a temporary blank PDF in the system temp directory with the given page count.</summary>
    private static string CreateTempPdf(string baseName, int pageCount) =>
        CreateTempPdf(Path.GetTempPath(), $"{baseName}.pdf", pageCount);

    /// <summary>Deletes the given temporary files when they exist.</summary>
    private static void DeleteTempFiles(params string[] paths)
    {
        foreach (var path in paths.Where(File.Exists)) File.Delete(path);
    }
}

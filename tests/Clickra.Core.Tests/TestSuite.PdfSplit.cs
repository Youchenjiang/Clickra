using Clickra.Core;
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
        runner.Run("PdfSplitProcessor parses page ranges accurately", () =>
        {
            var r1 = PdfSplitProcessor.ParsePageRange("1-3", 10);
            Assert.True(r1.SequenceEqual(new[] { 1, 2, 3 }), "Expected 1-3 to yield pages 1, 2, 3.");

            var r2 = PdfSplitProcessor.ParsePageRange("1, 5, 8-10", 10);
            Assert.True(r2.SequenceEqual(new[] { 1, 5, 8, 9, 10 }), "Expected 1, 5, 8-10 to yield pages 1, 5, 8, 9, 10.");

            var r3 = PdfSplitProcessor.ParsePageRange("12-15", 5);
            Assert.True(r3.Count == 0, "Expected out of bound range to yield empty list.");
        });

        runner.Run("FileProcessor.SplitPdf extracts specified page ranges to new document", () =>
        {
            string inputPath = CreateTempPdf($"clickra-split-src-{Guid.NewGuid():N}", 3);
            string outputPath = Path.Combine(Path.GetTempPath(), $"clickra-split-out-{Guid.NewGuid():N}.pdf");

            try
            {
                FileProcessor.SplitPdf(inputPath, outputPath, "1-2");

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

        runner.Run("PdfSplitProcessor emits one output file per multi-segment spec", () =>
        {
            string baseName = $"clickra-multisplit-{Guid.NewGuid():N}";
            string inputPath = CreateTempPdf(baseName, 5);
            string outputPath = Path.Combine(Path.GetTempPath(), $"{baseName}_target.pdf");
            string seg1Path = Path.Combine(Path.GetTempPath(), $"{baseName}_1-2.pdf");
            string seg2Path = Path.Combine(Path.GetTempPath(), $"{baseName}_4-4.pdf");

            try
            {
                FileProcessor.SplitPdf(inputPath, outputPath, "1-2; 4");

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

        RegisterSplitFailureTests(runner);
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
    private static void AssertSplitFails(string spec, string failureMessage)
    {
        string inputPath = CreateTempPdf($"clickra-split-fail-{Guid.NewGuid():N}", 3);
        try
        {
            bool threw = false;
            try
            {
                FileProcessor.SplitPdf(inputPath, "unused.pdf", spec);
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            Assert.True(threw, failureMessage);
        }
        finally
        {
            DeleteTempFiles(inputPath);
        }
    }

    /// <summary>Creates a temporary blank PDF with the given page count and returns its path.</summary>
    private static string CreateTempPdf(string baseName, int pageCount)
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"{baseName}.pdf");
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

    /// <summary>Deletes the given temporary files when they exist.</summary>
    private static void DeleteTempFiles(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

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
            string inputPath = Path.Combine(Path.GetTempPath(), $"clickra-split-src-{Guid.NewGuid():N}.pdf");
            string outputPath = Path.Combine(Path.GetTempPath(), $"clickra-split-out-{Guid.NewGuid():N}.pdf");

            try
            {
                using (var doc = new PdfDocument())
                {
                    doc.AddPage();
                    doc.AddPage();
                    doc.AddPage();
                    doc.Save(inputPath);
                }

                FileProcessor.SplitPdf(inputPath, outputPath, "1-2");

                Assert.True(File.Exists(outputPath), "Expected output split PDF file to exist.");
                using var outDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
                Assert.True(outDoc.PageCount == 2, $"Expected output PDF to have 2 pages, got {outDoc.PageCount}.");
            }
            finally
            {
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
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
            string inputPath = Path.Combine(Path.GetTempPath(), $"{baseName}.pdf");
            string outputPath = Path.Combine(Path.GetTempPath(), $"{baseName}_target.pdf");
            string seg1Path = Path.Combine(Path.GetTempPath(), $"{baseName}_1-2.pdf");
            string seg2Path = Path.Combine(Path.GetTempPath(), $"{baseName}_4-4.pdf");

            try
            {
                using (var doc = new PdfDocument())
                {
                    doc.AddPage();
                    doc.AddPage();
                    doc.AddPage();
                    doc.AddPage();
                    doc.AddPage();
                    doc.Save(inputPath);
                }

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
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
                if (File.Exists(seg1Path)) File.Delete(seg1Path);
                if (File.Exists(seg2Path)) File.Delete(seg2Path);
            }
        });
    }
}

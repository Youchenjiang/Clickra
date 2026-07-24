#pragma warning disable S3903 // TestSuite is intentionally in the global namespace

using Clickra.Core;
using Clickra.Core.Processors;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
                using (var outDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import))
                {
                    Assert.True(outDoc.PageCount == 2, $"Expected output PDF to have 2 pages, got {outDoc.PageCount}.");
                }
            }
            finally
            {
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        });
    }
}

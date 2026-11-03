using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core.Application;
using Clickra.Core.Modules.PdfToImage.Application;
using Clickra.Core.Modules.PdfToImage.Processing;
using PdfSharp.Pdf;

#pragma warning disable CA1416 // Validate platform compatibility

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterPdfToImageTests(TestRunner runner)
    {
        runner.Run("PdfToImageOptions normalizes format and DPI parameters correctly", () =>
        {
            Assert.Equal("png", PdfToImageOptions.NormalizeFormat(""));
            Assert.Equal("png", PdfToImageOptions.NormalizeFormat("PNG"));
            Assert.Equal("jpg", PdfToImageOptions.NormalizeFormat("jpg"));
            Assert.Equal("jpg", PdfToImageOptions.NormalizeFormat(".jpeg"));
            Assert.Equal("webp", PdfToImageOptions.NormalizeFormat("webp"));
            Assert.Equal("png", PdfToImageOptions.NormalizeFormat("invalid"));

            Assert.Equal(72, PdfToImageOptions.NormalizeDpi(10));
            Assert.Equal(600, PdfToImageOptions.NormalizeDpi(1200));
            Assert.Equal(300, PdfToImageOptions.NormalizeDpi(300));
        });

        runner.Run("PdfToImageUseCase plans output paths and normalizes options", () =>
        {
            var useCase = ConversionUseCases.GetRequired(PdfToImageUseCase.CommandName);
            Assert.True(useCase is PdfToImageUseCase, "Expected use case to be PdfToImageUseCase instance.");

            string inputPath = CreatePdfForImageTest("test-plan", 2);
            try
            {
                var request = new ConversionRequest(
                    PdfToImageUseCase.CommandName,
                    new[] { inputPath },
                    new Dictionary<string, object>
                    {
                        ["format"] = "jpg",
                        ["dpi"] = 300,
                        ["pages"] = "1-2",
                        ["stitch"] = true
                    });

                var plan = useCase.Plan(request);
                Assert.Equal(1, plan.Inputs.Count);
                Assert.Equal(1, plan.Outputs.Count);
                Assert.True(plan.Outputs[0].EndsWith(".png", StringComparison.OrdinalIgnoreCase), "Default plan output ends with .png");
                Assert.Equal("jpg", (string)plan.NormalizedOptions["format"]);
                Assert.Equal(300, (int)plan.NormalizedOptions["dpi"]);
                Assert.Equal("1-2", (string)plan.NormalizedOptions["pages"]);
                Assert.True((bool)plan.NormalizedOptions["stitch"], "Stitch option must be true.");
            }
            finally
            {
                DeleteTempFileSafe(inputPath);
            }
        });

        runner.Run("PdfToImageProcessor converts single-page PDF to PNG", () =>
        {
            string inputPath = CreatePdfForImageTest("test-single", 1);
            string outDir = Path.Combine(Path.GetTempPath(), $"clickra-pdf2img-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outDir);
            string outputPath = Path.Combine(outDir, "test-single.png");

            try
            {
                var processor = new PdfToImageProcessor();
                processor.Process(
                    new List<string> { inputPath },
                    outputPath,
                    new Dictionary<string, object> { ["format"] = "png", ["dpi"] = 150 });

                Assert.True(File.Exists(outputPath), $"Expected output file {outputPath} to exist.");
                using var bmp = new Bitmap(outputPath);
                Assert.True(bmp.Width > 0 && bmp.Height > 0, "Bitmap must have positive dimensions.");
            }
            finally
            {
                DeleteTempFileSafe(inputPath);
                DeleteTempDirectorySafe(outDir);
            }
        });

        runner.Run("PdfToImageProcessor converts multi-page PDF to per-page PNGs", () =>
        {
            string inputPath = CreatePdfForImageTest("test-multi", 3);
            string baseName = Path.GetFileNameWithoutExtension(inputPath);
            string outDir = Path.Combine(Path.GetTempPath(), $"clickra-pdf2img-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outDir);
            string outputPath = Path.Combine(outDir, "test-multi.png");

            try
            {
                var processor = new PdfToImageProcessor();
                processor.Process(
                    new List<string> { inputPath },
                    outputPath,
                    new Dictionary<string, object> { ["format"] = "png", ["dpi"] = 150, ["pages"] = "all", ["stitch"] = false });

                string p1 = Path.Combine(outDir, $"{baseName}_page_001.png");
                string p2 = Path.Combine(outDir, $"{baseName}_page_002.png");
                string p3 = Path.Combine(outDir, $"{baseName}_page_003.png");

                Assert.True(File.Exists(p1), $"Expected page 1 file {p1} to exist.");
                Assert.True(File.Exists(p2), $"Expected page 2 file {p2} to exist.");
                Assert.True(File.Exists(p3), $"Expected page 3 file {p3} to exist.");
            }
            finally
            {
                DeleteTempFileSafe(inputPath);
                DeleteTempDirectorySafe(outDir);
            }
        });

        runner.Run("PdfToImageProcessor stitches multiple pages into a vertical long image", () =>
        {
            string inputPath = CreatePdfForImageTest("test-stitch", 3);
            string outDir = Path.Combine(Path.GetTempPath(), $"clickra-pdf2img-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outDir);
            string outputPath = Path.Combine(outDir, "test-stitch_long.png");

            try
            {
                var processor = new PdfToImageProcessor();
                processor.Process(
                    new List<string> { inputPath },
                    outputPath,
                    new Dictionary<string, object> { ["format"] = "png", ["dpi"] = 150, ["stitch"] = true });

                Assert.True(File.Exists(outputPath), $"Expected stitched file {outputPath} to exist.");
                using var bmp = new Bitmap(outputPath);
                Assert.True(bmp.Height >= bmp.Width, "Stitched vertical image height should be greater than or equal to width.");
            }
            finally
            {
                DeleteTempFileSafe(inputPath);
                DeleteTempDirectorySafe(outDir);
            }
        });

        runner.Run("PdfToImageProcessor respects custom page range specification", () =>
        {
            string inputPath = CreatePdfForImageTest("test-range", 5);
            string baseName = Path.GetFileNameWithoutExtension(inputPath);
            string outDir = Path.Combine(Path.GetTempPath(), $"clickra-pdf2img-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outDir);
            string outputPath = Path.Combine(outDir, "test-range.png");

            try
            {
                var processor = new PdfToImageProcessor();
                processor.Process(
                    new List<string> { inputPath },
                    outputPath,
                    new Dictionary<string, object> { ["format"] = "jpg", ["dpi"] = 150, ["pages"] = "2-3" });

                string p1 = Path.Combine(outDir, $"{baseName}_page_001.jpg");
                string p2 = Path.Combine(outDir, $"{baseName}_page_002.jpg");
                string p3 = Path.Combine(outDir, $"{baseName}_page_003.jpg");

                Assert.False(File.Exists(p1), "Page 1 was not selected and should not exist.");
                Assert.True(File.Exists(p2), "Page 2 was selected and must exist.");
                Assert.True(File.Exists(p3), "Page 3 was selected and must exist.");
            }
            finally
            {
                DeleteTempFileSafe(inputPath);
                DeleteTempDirectorySafe(outDir);
            }
        });

        runner.Run("PdfToImageProcessor fails gracefully on invalid page ranges", () =>
        {
            string inputPath = CreatePdfForImageTest("test-invalid", 2);
            string outDir = Path.Combine(Path.GetTempPath(), $"clickra-pdf2img-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outDir);
            string outputPath = Path.Combine(outDir, "test-invalid.png");

            try
            {
                var processor = new PdfToImageProcessor();
                bool threw = false;
                try
                {
                    processor.Process(
                        new List<string> { inputPath },
                        outputPath,
                        new Dictionary<string, object> { ["pages"] = "99-100" });
                }
                catch (ArgumentException)
                {
                    threw = true;
                }
                Assert.True(threw, "Processor must throw ArgumentException when page range resolves to empty.");
            }
            finally
            {
                DeleteTempFileSafe(inputPath);
                DeleteTempDirectorySafe(outDir);
            }
        });
    }

    private static string CreatePdfForImageTest(string name, int pageCount)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}.pdf");
        using var doc = new PdfDocument();
        for (int i = 0; i < pageCount; i++)
        {
            doc.AddPage();
        }
        doc.Save(path);
        return path;
    }

    private static void DeleteTempFileSafe(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static void DeleteTempDirectorySafe(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch { }
    }
}

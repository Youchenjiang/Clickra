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
    private const string FormatOptionKey = "format";
    private const string PagesOptionKey = "pages";
    private const string StitchOptionKey = "stitch";
    private const string DpiOptionKey = "dpi";

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
                        [FormatOptionKey] = "jpg",
                        [DpiOptionKey] = 300,
                        [PagesOptionKey] = "1-2",
                        [StitchOptionKey] = true
                    });

                var plan = useCase.Plan(request);
                Assert.Equal(1, plan.Inputs.Count);
                Assert.Equal(1, plan.Outputs.Count);
                Assert.True(plan.Outputs[0].EndsWith(".png", StringComparison.OrdinalIgnoreCase), "Default plan output ends with .png");
                Assert.Equal("jpg", (string)plan.NormalizedOptions[FormatOptionKey]);
                Assert.Equal(300, (int)plan.NormalizedOptions[DpiOptionKey]);
                Assert.Equal("1-2", (string)plan.NormalizedOptions[PagesOptionKey]);
                Assert.True((bool)plan.NormalizedOptions[StitchOptionKey], "Stitch option must be true.");
            }
            finally
            {
                DeleteTempFileSafe(inputPath);
            }
        });

        runner.Run("PdfToImageProcessor converts single-page PDF to PNG", () =>
            WithTempPdfContext("test-single", 1, (inputPath, outDir, _) =>
            {
                string outputPath = Path.Combine(outDir, "test-single.png");
                var processor = new PdfToImageProcessor();
                processor.Process(
                    new List<string> { inputPath },
                    outputPath,
                    new Dictionary<string, object> { [FormatOptionKey] = "png", [DpiOptionKey] = 150 });

                Assert.True(File.Exists(outputPath), $"Expected output file {outputPath} to exist.");
                using var bmp = new Bitmap(outputPath);
                Assert.True(bmp.Width > 0 && bmp.Height > 0, "Bitmap must have positive dimensions.");
            }));

        runner.Run("PdfToImageProcessor converts multi-page PDF to per-page PNGs", () =>
            WithTempPdfContext("test-multi", 3, (inputPath, outDir, baseName) =>
            {
                string outputPath = Path.Combine(outDir, "test-multi.png");
                var processor = new PdfToImageProcessor();
                processor.Process(
                    new List<string> { inputPath },
                    outputPath,
                    new Dictionary<string, object> { [FormatOptionKey] = "png", [DpiOptionKey] = 150, [PagesOptionKey] = "all", [StitchOptionKey] = false });

                string p1 = Path.Combine(outDir, $"{baseName}_page_001.png");
                string p2 = Path.Combine(outDir, $"{baseName}_page_002.png");
                string p3 = Path.Combine(outDir, $"{baseName}_page_003.png");

                Assert.True(File.Exists(p1), $"Expected page 1 file {p1} to exist.");
                Assert.True(File.Exists(p2), $"Expected page 2 file {p2} to exist.");
                Assert.True(File.Exists(p3), $"Expected page 3 file {p3} to exist.");
            }));

        runner.Run("PdfToImageProcessor stitches multiple pages into a vertical long image", () =>
            WithTempPdfContext("test-stitch", 3, (inputPath, outDir, _) =>
            {
                string outputPath = Path.Combine(outDir, "test-stitch_long.png");
                var processor = new PdfToImageProcessor();
                processor.Process(
                    new List<string> { inputPath },
                    outputPath,
                    new Dictionary<string, object> { [FormatOptionKey] = "png", [DpiOptionKey] = 150, [StitchOptionKey] = true });

                Assert.True(File.Exists(outputPath), $"Expected stitched file {outputPath} to exist.");
                using var bmp = new Bitmap(outputPath);
                Assert.True(bmp.Height >= bmp.Width, "Stitched vertical image height should be greater than or equal to width.");
            }));

        runner.Run("PdfToImageProcessor respects custom page range specification", () =>
            WithTempPdfContext("test-range", 5, (inputPath, outDir, baseName) =>
            {
                string outputPath = Path.Combine(outDir, "test-range.png");
                var processor = new PdfToImageProcessor();
                processor.Process(
                    new List<string> { inputPath },
                    outputPath,
                    new Dictionary<string, object> { [FormatOptionKey] = "jpg", [DpiOptionKey] = 150, [PagesOptionKey] = "2-3" });

                string p1 = Path.Combine(outDir, $"{baseName}_page_001.jpg");
                string p2 = Path.Combine(outDir, $"{baseName}_page_002.jpg");
                string p3 = Path.Combine(outDir, $"{baseName}_page_003.jpg");

                Assert.False(File.Exists(p1), "Page 1 was not selected and should not exist.");
                Assert.True(File.Exists(p2), "Page 2 was selected and must exist.");
                Assert.True(File.Exists(p3), "Page 3 was selected and must exist.");
            }));

        runner.Run("PdfToImageProcessor fails gracefully on invalid page ranges", () =>
            WithTempPdfContext("test-invalid", 2, (inputPath, outDir, _) =>
            {
                string outputPath = Path.Combine(outDir, "test-invalid.png");
                var processor = new PdfToImageProcessor();
                bool threw = false;
                try
                {
                    processor.Process(
                        new List<string> { inputPath },
                        outputPath,
                        new Dictionary<string, object> { [PagesOptionKey] = "99-100" });
                }
                catch (ArgumentException)
                {
                    threw = true;
                }
                Assert.True(threw, "Processor must throw ArgumentException when page range resolves to empty.");
            }));
    }

    private static void WithTempPdfContext(string name, int pageCount, Action<string, string, string> action)
    {
        string inputPath = CreatePdfForImageTest(name, pageCount);
        string baseName = Path.GetFileNameWithoutExtension(inputPath);
        string outDir = Path.Combine(Path.GetTempPath(), $"clickra-pdf2img-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        try
        {
            action(inputPath, outDir, baseName);
        }
        finally
        {
            DeleteTempFileSafe(inputPath);
            DeleteTempDirectorySafe(outDir);
        }
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
        catch (Exception) // skipcq: CS-R1008
        {
            // Best-effort cleanup of temporary test files
        }
    }

    private static void DeleteTempDirectorySafe(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception) // skipcq: CS-R1008
        {
            // Best-effort cleanup of temporary test directories
        }
    }
}

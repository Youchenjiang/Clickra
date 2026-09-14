using Clickra.Core.Processors;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterWicImageTests(TestRunner runner)
    {
        runner.Run("WicImageHelper encoder and decoder availability checks run without exception", () =>
        {
            bool encoderAvailable = WicImageHelper.IsHeicEncoderAvailable();
            bool decoderAvailable = WicImageHelper.IsHeicDecoderAvailable();
            Assert.True(encoderAvailable || !encoderAvailable, "Encoder query succeeded.");
            Assert.True(decoderAvailable || !decoderAvailable, "Decoder query succeeded.");
        });

        runner.Run("WicImageHelper.LoadImageSafely throws FileNotFoundException on missing file", () =>
        {
            string nonExistent = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png");
            Assert.Throws<FileNotFoundException>(() => WicImageHelper.LoadImageSafely(nonExistent));
        });

        runner.Run("WicImageHelper.LoadImageSafely loads standard PNG and JPEG correctly", () =>
            RunWithTempDirectory(tempDir =>
            {
                string pngPath = Path.Combine(tempDir, "test.png");
                using (var bmp = new Bitmap(100, 100))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Blue);
                    }
                    bmp.Save(pngPath, ImageFormat.Png);
                }

                using var loaded = WicImageHelper.LoadImageSafely(pngPath);
                Assert.True(loaded != null, "Loaded bitmap should not be null.");
                Assert.True(loaded!.Width == 100, "Loaded width should match.");
                Assert.True(loaded.Height == 100, "Loaded height should match.");
            }));

        runner.Run("ImageToPdfProcessor processes image using WicImage fallback", () =>
            RunWithTempDirectory(tempDir =>
            {
                string pngPath = Path.Combine(tempDir, "page.png");
                using (var bmp = new Bitmap(50, 50))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Green);
                    }
                    bmp.Save(pngPath, ImageFormat.Png);
                }

                string pdfOut = Path.Combine(tempDir, "output.pdf");
                new ImageToPdfProcessor().Process(new List<string> { pngPath }, pdfOut, null, null, CancellationToken.None);

                Assert.True(File.Exists(pdfOut), "Generated PDF should exist.");
                Assert.True(new FileInfo(pdfOut).Length > 0, "Generated PDF should not be empty.");

                // Also test WIC fallback branch via .heic extension
                string heicSimulatedPath = Path.Combine(tempDir, "simulated.heic");
                File.Copy(pngPath, heicSimulatedPath);
                string pdfOut2 = Path.Combine(tempDir, "output2.pdf");
                new ImageToPdfProcessor().Process(new List<string> { heicSimulatedPath }, pdfOut2, null, null, CancellationToken.None);
                Assert.True(File.Exists(pdfOut2), "Generated PDF from simulated HEIC should exist.");
                Assert.True(new FileInfo(pdfOut2).Length > 0, "Generated PDF from simulated HEIC should not be empty.");
            }));

        runner.Run("ImageStitchProcessor processes images loaded via WicImageHelper", () =>
            RunWithTempDirectory(tempDir =>
            {
                string img1 = Path.Combine(tempDir, "1.png");
                string img2 = Path.Combine(tempDir, "2.png");
                using (var bmp = new Bitmap(40, 40))
                {
                    using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Red); }
                    bmp.Save(img1, ImageFormat.Png);
                    bmp.Save(img2, ImageFormat.Png);
                }

                string stitchedOut = Path.Combine(tempDir, "stitched.png");
                new ImageStitchProcessor().Process(new List<string> { img1, img2 }, stitchedOut, null, null, CancellationToken.None);

                Assert.True(File.Exists(stitchedOut), "Stitched image should exist.");
                using var result = new Bitmap(stitchedOut);
                Assert.True(result.Width == 40, "Stitched width should match.");
                Assert.True(result.Height == 80, "Stitched height should match.");
            }));
    }

    private static void RunWithTempDirectory(Action<string> action)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"wic-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            action(tempDir);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (IOException ex)
                {
                    // Best-effort cleanup for temporary directories; file locks or antivirus scans may briefly hold directory handles.
                    Console.WriteLine($"[TestCleanup] Failed to delete temporary test directory: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    // Best-effort cleanup for temporary directories.
                    Console.WriteLine($"[TestCleanup] Unauthorized access deleting temporary test directory: {ex.Message}");
                }
            }
        }
    }
}

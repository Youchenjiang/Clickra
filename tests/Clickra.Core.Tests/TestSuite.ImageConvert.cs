using Clickra.Core;
using Clickra.Core.Processors;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterImageConvertTests(TestRunner runner)
    {
        RegisterBasicFormatConversionTests(runner);
        RegisterCodecAndLocalizationTests(runner);
        RegisterCommandRegistryAndRunnerTests(runner);
    }

    private static void RegisterBasicFormatConversionTests(TestRunner runner)
    {
        runner.Run("ImageFormatConvertProcessor converts PNG to JPG with JPEG magic bytes", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, "input.png", ImageFormat.Png);
                string output = Path.Combine(tempDir, "output.jpg");
                FileProcessor.ConvertImageFormat(input, output, "jpg");
                Assert.True(File.Exists(output), "Expected JPG output to exist.");
                Assert.True(HasJpegMagicBytes(output), "Expected JPG magic bytes (FF D8 FF).");
            }));

        runner.Run("ImageFormatConvertProcessor converts JPG to PNG with PNG magic bytes", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, "input.jpg", ImageFormat.Jpeg);
                string output = Path.Combine(tempDir, "output.png");
                FileProcessor.ConvertImageFormat(input, output, "png");
                Assert.True(File.Exists(output), "Expected PNG output to exist.");
                Assert.True(HasPngMagicBytes(output), "Expected PNG magic bytes (89 50 4E 47).");
            }));

        runner.Run("ImageFormatConvertProcessor converts to WEBP and GIF", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, "input.png", ImageFormat.Png);
                string gif = Path.Combine(tempDir, "out.gif");
                FileProcessor.ConvertImageFormat(input, gif, "gif");
                Assert.True(File.Exists(gif), "Expected GIF output to exist.");

                string webp = Path.Combine(tempDir, "out.webp");
                if (ImageFormatConvertProcessor.IsWebpEncodingSupported())
                {
                    FileProcessor.ConvertImageFormat(input, webp, "webp");
                    Assert.True(File.Exists(webp), "Expected WEBP output to exist.");
                }
                else
                {
                    Assert.Throws<NotSupportedException>(() => FileProcessor.ConvertImageFormat(input, webp, "webp"));
                }
            }));

        runner.Run("ImageFormatConvertProcessor skips same-format input without overwriting the source", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, "input.png", ImageFormat.Png);
                long originalSize = new FileInfo(input).Length;
                string same = Path.Combine(tempDir, "input.png");
                FileProcessor.ConvertImageFormat(input, same, "png");
                Assert.True(File.Exists(same), "Expected the source file to still exist.");
                Assert.True(new FileInfo(same).Length == originalSize, "Source must not be overwritten by a same-format conversion.");
            }));

        runner.Run("ImageFormatConvertProcessor rejects unsupported target formats", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, "input.png", ImageFormat.Png);
                string output = Path.Combine(tempDir, "out.xyz");
                Assert.Throws<NotSupportedException>(() => FileProcessor.ConvertImageFormat(input, output, "xyz"));
            }));

        runner.Run("ImageFormatConvertProcessor supports HEIC output when a codec is available", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, "input.png", ImageFormat.Png);
                string output = Path.Combine(tempDir, "out.heic");
                if (ImageFormatConvertProcessor.IsHeicEncodingSupported())
                {
                    FileProcessor.ConvertImageFormat(input, output, "heic");
                    Assert.True(File.Exists(output), "Expected HEIC output to exist when a HEIC encoder is present.");
                    Assert.True(HasHeicMagicBytes(output), "Expected HEIC ftyp magic bytes.");

                    string backPng = Path.Combine(tempDir, "from_heic.png");
                    FileProcessor.ConvertImageFormat(output, backPng, "png");
                    Assert.True(File.Exists(backPng), "Expected PNG output decoded from HEIC to exist.");
                    Assert.True(HasPngMagicBytes(backPng), "Expected PNG magic bytes from HEIC decode.");

                    string backJpg = Path.Combine(tempDir, "from_heic.jpg");
                    FileProcessor.ConvertImageFormat(output, backJpg, "jpg");
                    Assert.True(File.Exists(backJpg), "Expected JPG output decoded from HEIC to exist.");
                    Assert.True(HasJpegMagicBytes(backJpg), "Expected JPG magic bytes from HEIC decode.");

                    string heicPdf = Path.Combine(tempDir, "heic_to_pdf.pdf");
                    FileProcessor.ConvertImagesToPdf(new List<string> { output }, heicPdf);
                    Assert.True(File.Exists(heicPdf), "Expected PDF output from HEIC input to exist.");
                    Assert.True(new FileInfo(heicPdf).Length > 0, "Expected non-empty PDF from HEIC.");

                    string stitchedOut = Path.Combine(tempDir, "stitched.png");
                    FileProcessor.StitchImages(new List<string> { output, output }, stitchedOut);
                    Assert.True(File.Exists(stitchedOut), "Expected stitched output from HEIC inputs to exist.");
                    Assert.True(HasPngMagicBytes(stitchedOut), "Expected valid PNG header from stitched HEIC.");
                }
                else
                {
                    Assert.Throws<NotSupportedException>(() => FileProcessor.ConvertImageFormat(input, output, "heic"));
                }
            }));
    }

    private static void RegisterCodecAndLocalizationTests(TestRunner runner)
    {
        runner.Run("ImageFormatConvertProcessor maps codecs and Store URIs for target formats", () =>
        {
            Assert.Equal("heic", ImageFormatConvertProcessor.GetRequiredCodec("heic") ?? "");
            Assert.Equal("heic", ImageFormatConvertProcessor.GetRequiredCodec(".HEIC") ?? "");
            Assert.Equal("webp", ImageFormatConvertProcessor.GetRequiredCodec("webp") ?? "");
            Assert.True(ImageFormatConvertProcessor.GetRequiredCodec("png") is null, "png must not require an extra codec.");
            Assert.True(ImageFormatConvertProcessor.GetRequiredCodec("jpg") is null, "jpg must not require an extra codec.");
            Assert.True(ImageFormatConvertProcessor.GetRequiredCodec("gif") is null, "gif must not require an extra codec.");
            Assert.True(ImageFormatConvertProcessor.GetRequiredCodec("xyz") is null, "unknown formats report no codec.");

            Assert.Equal("ms-windows-store://pdp/?productid=9PMMSR1CGPWG", ImageFormatConvertProcessor.GetCodecStoreUri("heic")?.AbsoluteUri);
            Assert.Equal("ms-windows-store://pdp/?productid=9PG2DK419DRG", ImageFormatConvertProcessor.GetCodecStoreUri("webp")?.AbsoluteUri);
            Assert.True(ImageFormatConvertProcessor.GetCodecStoreUri("png") is null, "png does not require a Store codec.");
        });

        runner.Run("ImageFormatConvertProcessor preflight reports the codec a command is missing", () =>
        {
            bool heicSupported = ImageFormatConvertProcessor.IsHeicEncodingSupported();
            bool webpSupported = ImageFormatConvertProcessor.IsWebpEncodingSupported();

            Assert.True(heicSupported == (ImageFormatConvertProcessor.GetMissingCodecForCommand("img-to-heic", new List<string> { "C:\\x\\a.png" }) is null), "img-to-heic preflight must agree with IsHeicEncodingSupported.");
            Assert.True(webpSupported == (ImageFormatConvertProcessor.GetMissingCodecForCommand("img-to-webp", new List<string> { "C:\\x\\a.png" }) is null), "img-to-webp preflight must agree with IsWebpEncodingSupported.");
            Assert.True(ImageFormatConvertProcessor.GetMissingCodecForCommand("img2pdf", new List<string> { "C:\\x\\a.png" }) is null, "img2pdf needs no extra codec.");
            Assert.True(ImageFormatConvertProcessor.GetMissingCodecForCommand("merge-pdf", new List<string> { "C:\\x\\a.pdf" }) is null, "merge-pdf needs no extra codec.");
        });

        runner.Run("Missing-codec messages are localized in every supported language", () =>
        {
            foreach (var lang in new[] { "zh-TW", "zh-CN", "en-US", "ja-JP", "ko-KR" })
            {
                string heic = Localization.T("error_heic_codec_missing", lang);
                string webp = Localization.T("error_webp_codec_missing", lang);
                Assert.False(heic.Equals("error_heic_codec_missing", StringComparison.Ordinal), $"heic error message missing for {lang}.");
                Assert.False(webp.Equals("error_webp_codec_missing", StringComparison.Ordinal), $"webp error message missing for {lang}.");
                string prompt = Localization.T("codec_missing_store_prompt", lang);
                Assert.True(prompt.Contains("{0}") && prompt.Contains("{1}"), $"store prompt for {lang} lost its format placeholders.");
            }
            Assert.Equal("HEIF Image Extensions", Localization.T("codec_heif_extension_name", "en-US"));
            Assert.Equal("WebP Image Extensions", Localization.T("codec_webp_extension_name", "en-US"));
            Assert.Equal("安裝編碼器", Localization.T("codec_missing_install_action", "zh-TW"));
        });

        runner.Run("ImageFormatConvertProcessor.ToOutputExtension maps aliases to canonical extensions", () =>
        {
            Assert.Equal(".jpg", ImageFormatConvertProcessor.ToOutputExtension("jpg"));
            Assert.Equal(".jpg", ImageFormatConvertProcessor.ToOutputExtension("jpeg"));
            Assert.Equal(".png", ImageFormatConvertProcessor.ToOutputExtension("PNG"));
            Assert.Equal(".webp", ImageFormatConvertProcessor.ToOutputExtension("webp"));
            Assert.Equal(".heic", ImageFormatConvertProcessor.ToOutputExtension("heic"));
        });
    }

    private static void RegisterCommandRegistryAndRunnerTests(TestRunner runner)
    {
        runner.Run("ConvertCommandRegistry registers img-to-* commands with image inputs", () =>
        {
            foreach (var command in new[] { "img-to-png", "img-to-jpg", "img-to-webp", "img-to-gif", "img-to-heic" })
            {
                Assert.True(ConvertCommandRegistry.IsKnownCommand(command), $"{command} should be a known command.");
                Assert.True(ConvertCommandRegistry.GetMinFiles(command) == 1, $"{command} should accept a single file.");
                Assert.True(ConvertCommandRegistry.GetAllowedExtensions(command).Contains(".png", StringComparer.OrdinalIgnoreCase) || command == "img-to-png", $"{command} should accept PNG inputs unless PNG is the target.");
                Assert.True(ConvertCommandRegistry.GetAllowedExtensions(command).Contains(".heic", StringComparer.OrdinalIgnoreCase) || command == "img-to-heic", $"{command} should accept HEIC inputs unless HEIC is the target.");
                Assert.Equal("image", ConvertCommandRegistry.GetFileTypeForCommand(command));
            }
        });

        runner.Run("ConvertCommandRegistry excludes same-format sources from img-to-* commands", () =>
        {
            Assert.False(ConvertCommandRegistry.GetAllowedExtensions("img-to-png").Contains(".png", StringComparer.OrdinalIgnoreCase), "img-to-png must not accept .png inputs.");
            Assert.False(ConvertCommandRegistry.GetAllowedExtensions("img-to-jpg").Contains(".jpg", StringComparer.OrdinalIgnoreCase), "img-to-jpg must not accept .jpg inputs.");
            Assert.False(ConvertCommandRegistry.GetAllowedExtensions("img-to-jpg").Contains(".jpeg", StringComparer.OrdinalIgnoreCase), "img-to-jpg must not accept .jpeg inputs.");
            Assert.False(ConvertCommandRegistry.GetAllowedExtensions("img-to-webp").Contains(".webp", StringComparer.OrdinalIgnoreCase), "img-to-webp must not accept .webp inputs.");
            Assert.False(ConvertCommandRegistry.GetAllowedExtensions("img-to-gif").Contains(".gif", StringComparer.OrdinalIgnoreCase), "img-to-gif must not accept .gif inputs.");
            Assert.False(ConvertCommandRegistry.GetAllowedExtensions("img-to-heic").Contains(".heic", StringComparer.OrdinalIgnoreCase), "img-to-heic must not accept .heic inputs.");

            Assert.True(ConvertCommandRegistry.GetAllowedExtensions("img-to-heic").Contains(".png", StringComparer.OrdinalIgnoreCase), "img-to-heic should still accept .png inputs.");
            Assert.True(ConvertCommandRegistry.GetExcludedExtensions("img-to-heic").Contains(".heic", StringComparer.OrdinalIgnoreCase), "img-to-heic should declare .heic as excluded.");
            Assert.True(ConvertCommandRegistry.GetExcludedExtensions("img2pdf").Length == 0, "img2pdf accepts every image input.");
            Assert.True(ConvertCommandRegistry.GetAllowedExtensions("img2pdf").Contains(".bmp", StringComparer.OrdinalIgnoreCase), "img2pdf must accept .bmp inputs.");
            Assert.True(ConvertCommandRegistry.GetAllowedExtensions("img2pdf").Contains(".tiff", StringComparer.OrdinalIgnoreCase), "img2pdf must accept .tiff inputs.");
            Assert.True(ConvertCommandRegistry.GetAllowedExtensions("img-to-png").Contains(".bmp", StringComparer.OrdinalIgnoreCase), "img-to-png must accept .bmp inputs.");
        });

        runner.Run("ConvertCommandRegistry.EstimateOutputs predicts target extension per input", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, "photo.png", ImageFormat.Png);
                var outputs = ConvertCommandRegistry.EstimateOutputs("img-to-jpg", new List<string> { input });
                Assert.True(outputs.Count == 1, "Expected exactly one predicted output.");
                Assert.True(outputs[0].EndsWith(".jpg", StringComparison.OrdinalIgnoreCase), $"Expected .jpg output, got {outputs[0]}.");

                outputs = ConvertCommandRegistry.EstimateOutputs("img-to-heic", new List<string> { input });
                Assert.True(outputs.Count == 1, "Expected exactly one predicted output.");
                Assert.True(outputs[0].EndsWith(".heic", StringComparison.OrdinalIgnoreCase), $"Expected .heic output, got {outputs[0]}.");
            }));

        runner.Run("ConvertCommandRunner runs img-to-heic end to end when a HEIC encoder is available", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, "source.png", ImageFormat.Png);
                string output = Path.Combine(tempDir, "source.heic");
                var messages = new List<string>();
                if (ImageFormatConvertProcessor.IsHeicEncodingSupported())
                {
                    ConvertCommandRunner.Run("img-to-heic", new List<string> { input }, new List<string> { output },
                        (c, t, m) => messages.Add(m),
                        new ConvertCommandRunner.ConversionOptions(
                            _ => throw new InvalidOperationException("Password prompt must not run for image conversion."),
                            (_, _) => throw new InvalidOperationException("Split prompt must not run for image conversion.")));
                    Assert.True(File.Exists(output), "Expected HEIC output to exist after ConvertCommandRunner.Run when a HEIC encoder is present.");
                    Assert.True(HasHeicMagicBytes(output), "Expected HEIC magic bytes on runner output.");
                }
                else
                {
                    Assert.Throws<NotSupportedException>(() =>
                        ConvertCommandRunner.Run("img-to-heic", new List<string> { input }, new List<string> { output },
                            (c, t, m) => messages.Add(m),
                            new ConvertCommandRunner.ConversionOptions(
                                _ => throw new InvalidOperationException("Password prompt must not run for image conversion."),
                                (_, _) => throw new InvalidOperationException("Split prompt must not run for image conversion."))));
                }
                Assert.True(messages.Count > 0, "Expected the runner to attempt the conversion.");
            }));

        runner.Run("ConvertCommandRunner runs img-to-webp end to end", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = CreateTestImage(tempDir, "source.png", ImageFormat.Png);
                string output = Path.Combine(tempDir, "source.webp");
                var messages = new List<string>();
                if (ImageFormatConvertProcessor.IsWebpEncodingSupported())
                {
                    ConvertCommandRunner.Run("img-to-webp", new List<string> { input }, new List<string> { output },
                        (c, t, m) => messages.Add(m),
                        new ConvertCommandRunner.ConversionOptions(
                            _ => throw new InvalidOperationException("Password prompt must not run for image conversion."),
                            (_, _) => throw new InvalidOperationException("Split prompt must not run for image conversion.")));
                    Assert.True(File.Exists(output), "Expected WEBP output to exist after ConvertCommandRunner.Run.");
                }
                else
                {
                    Assert.Throws<NotSupportedException>(() =>
                        ConvertCommandRunner.Run("img-to-webp", new List<string> { input }, new List<string> { output },
                            (c, t, m) => messages.Add(m),
                            new ConvertCommandRunner.ConversionOptions(
                                _ => throw new InvalidOperationException("Password prompt must not run for image conversion."),
                                (_, _) => throw new InvalidOperationException("Split prompt must not run for image conversion."))));
                }
                Assert.True(messages.Count > 0, "Expected the runner to attempt the conversion.");
            }));
    }

    private static string CreateTestImage(string directory, string fileName, ImageFormat format)
    {
        string path = Path.Combine(directory, fileName);
        using (var bitmap = new Bitmap(32, 24))
        {
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                using var brush = new SolidBrush(Color.FromArgb(40, 90, 180));
                g.FillRectangle(brush, 4, 4, 24, 16);
            }
            bitmap.Save(path, format);
        }
        return path;
    }

    private static bool HasJpegMagicBytes(string path)
    {
        byte[] header = File.ReadAllBytes(path).Take(3).ToArray();
        return header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
    }

    private static bool HasPngMagicBytes(string path)
    {
        byte[] header = File.ReadAllBytes(path).Take(8).ToArray();
        return header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
    }

    private static bool HasHeicMagicBytes(string path)
    {
        byte[] header = File.ReadAllBytes(path).Take(12).ToArray();
        if (header.Length < 12) return false;
        return header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70;
    }
}

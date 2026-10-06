using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string ImageCompressionLevelKey = "level";
    private const string ImageCompressionQualityKey = "quality";

    public static void RegisterImageCompressionTests(TestRunner runner)
    {
        runner.Run("Image compression presets live in one table shared by the processor and the UIs", () =>
        {
            // 1. Slider positions 0-3 must map cleanly onto the four discrete quality levels.
            Assert.True(ImageCompressionOptions.FromSliderLevel(0) == ImageCompressionLevel.Minimum, "Slider 0 must mean Minimum.");
            Assert.True(ImageCompressionOptions.FromSliderLevel(1) == ImageCompressionLevel.Small, "Slider 1 must mean Small.");
            Assert.True(ImageCompressionOptions.FromSliderLevel(2) == ImageCompressionLevel.Standard, "Slider 2 must mean Standard.");
            Assert.True(ImageCompressionOptions.FromSliderLevel(3) == ImageCompressionLevel.High, "Slider 3 must mean High.");

            // 2. Option names must round-trip with TryParseLevel.
            var levels = new[]
            {
                ImageCompressionLevel.Minimum,
                ImageCompressionLevel.Small,
                ImageCompressionLevel.Standard,
                ImageCompressionLevel.High
            };

            foreach (var level in levels)
            {
                string optionName = ImageCompressionOptions.ToOptionName(level);
                Assert.True(!string.IsNullOrWhiteSpace(optionName), $"{level} must have a non-empty option name.");
                Assert.True(ImageCompressionOptions.TryParseLevel(optionName, out var parsed), $"TryParseLevel must accept {optionName}.");
                Assert.True(parsed == level, $"Round-trip parsed {optionName} must equal {level}.");

                int quality = ImageCompressionOptions.GetQuality(level);
                Assert.True(quality >= 1 && quality <= 100, $"Quality for {level} must be between 1 and 100, got {quality}.");

                string labelKey = ImageCompressionOptions.GetLabelKey(level);
                Assert.True(!string.IsNullOrWhiteSpace(labelKey), $"{level} must have a non-empty label key.");
                foreach (string lang in new[] { "zh-TW", "zh-CN", "en-US", "ja-JP", "ko-KR" })
                {
                    string translated = Localization.T(labelKey, lang);
                    Assert.True(translated != labelKey, $"{labelKey} must be translated in {lang}.");
                }
            }

            // Quality percentages must be strictly increasing with the level.
            Assert.True(ImageCompressionOptions.GetQuality(ImageCompressionLevel.Minimum) <
                        ImageCompressionOptions.GetQuality(ImageCompressionLevel.Small), "Minimum quality < Small quality.");
            Assert.True(ImageCompressionOptions.GetQuality(ImageCompressionLevel.Small) <
                        ImageCompressionOptions.GetQuality(ImageCompressionLevel.Standard), "Small quality < Standard quality.");
            Assert.True(ImageCompressionOptions.GetQuality(ImageCompressionLevel.Standard) <
                        ImageCompressionOptions.GetQuality(ImageCompressionLevel.High), "Standard quality < High quality.");
        });

        runner.Run("Image compression: command registry delegates to single source of truth", () =>
        {
            string origLevel = ClickraStorage.GetSetting(ClickraSettings.ImageCompressLevel);
            try
            {
                ClickraStorage.SaveSetting(ClickraSettings.ImageCompressLevel, "0");
                var opt0 = ConvertCommandRegistry.ImageCompressionOptions();
                Assert.Equal(ImageCompressionOptions.OptionMin, (string)opt0[ImageCompressionLevelKey]);
                Assert.Equal(ImageCompressionOptions.GetQuality(ImageCompressionLevel.Minimum), (int)opt0[ImageCompressionQualityKey]);

                ClickraStorage.SaveSetting(ClickraSettings.ImageCompressLevel, "1");
                var opt1 = ConvertCommandRegistry.ImageCompressionOptions();
                Assert.Equal(ImageCompressionOptions.OptionSmall, (string)opt1[ImageCompressionLevelKey]);
                Assert.Equal(ImageCompressionOptions.GetQuality(ImageCompressionLevel.Small), (int)opt1[ImageCompressionQualityKey]);

                ClickraStorage.SaveSetting(ClickraSettings.ImageCompressLevel, "2");
                var opt2 = ConvertCommandRegistry.ImageCompressionOptions();
                Assert.Equal(ImageCompressionOptions.OptionStandard, (string)opt2[ImageCompressionLevelKey]);
                Assert.Equal(ImageCompressionOptions.GetQuality(ImageCompressionLevel.Standard), (int)opt2[ImageCompressionQualityKey]);

                ClickraStorage.SaveSetting(ClickraSettings.ImageCompressLevel, "3");
                var opt3 = ConvertCommandRegistry.ImageCompressionOptions();
                Assert.Equal(ImageCompressionOptions.OptionHigh, (string)opt3[ImageCompressionLevelKey]);
                Assert.Equal(ImageCompressionOptions.GetQuality(ImageCompressionLevel.High), (int)opt3[ImageCompressionQualityKey]);
            }
            finally
            {
                ClickraStorage.SaveSetting(ClickraSettings.ImageCompressLevel, origLevel);
            }
        });

        runner.Run("Image compression: UI and call sites share unified label keys from core", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException("Could not locate repository root.");

            string fluent = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));

            // Fluent UI must use GetLabelKey instead of an ad-hoc switch with string literals
            Assert.True(fluent.Contains("PdfCompressionOptions.GetLabelKey", StringComparison.Ordinal),
                "Fluent UI must delegate PDF compression label keys to core.");
            Assert.False(fluent.Contains("switch\n        {\n            \"small\"", StringComparison.Ordinal),
                "Fluent UI must not hardcode private switch on compression level names.");

            // Verify that both PDF and Image options share the same label key naming convention
            Assert.Equal("setting_pdf_compress_level_small", ImageCompressionOptions.GetLabelKey(ImageCompressionLevel.Small));
            Assert.Equal("setting_pdf_compress_level_std", ImageCompressionOptions.GetLabelKey(ImageCompressionLevel.Standard));
            Assert.Equal("setting_pdf_compress_level_high", ImageCompressionOptions.GetLabelKey(ImageCompressionLevel.High));
            Assert.Equal("setting_pdf_compress_level_min", ImageCompressionOptions.GetLabelKey(ImageCompressionLevel.Minimum));

            Assert.Equal("setting_pdf_compress_level_small", PdfCompressionOptions.GetLabelKey(PdfCompressionLevel.Small));
            Assert.Equal("setting_pdf_compress_level_std", PdfCompressionOptions.GetLabelKey(PdfCompressionLevel.Balanced));
            Assert.Equal("setting_pdf_compress_level_high", PdfCompressionOptions.GetLabelKey(PdfCompressionLevel.HighQuality));
        });

        runner.Run("Image compression: img-compress is a production registry command", () =>
        {
            Assert.True(ConvertCommandRegistry.IsKnownCommand("img-compress"),
                "img-compress must be registered before the Explorer menu can invoke it.");
            Assert.Equal("cmd_img_compress", ConvertCommandRegistry.GetLabelKey("img-compress"));
            Assert.True(ConvertCommandRegistry.GetCommandsForType("image").Contains("img-compress", StringComparer.Ordinal),
                "Image command discovery must include img-compress.");

            var outputs = ConvertCommandRegistry.EstimateImageCompressionOutputs(
                new List<string> { Path.Combine("C:\\input", "photo.jpg") },
                "C:\\output");
            Assert.True(outputs.Count == 1 && outputs[0].EndsWith("photo_compressed.jpg", StringComparison.OrdinalIgnoreCase),
                "img-compress must preserve the source extension and use the _compressed suffix.");
        });

        runner.Run("Image compression: runner produces a smaller JPEG without overwriting input", () =>
        {
            RunWithTempDirectory(tempDir =>
            {
                string source = CreateCompressionNoiseJpeg(tempDir, "noise.jpg", 256, 256, 95L);
                string output = Path.Combine(tempDir, "noise_compressed.jpg");
                byte[] originalBytes = File.ReadAllBytes(source);
                string originalLevel = ClickraStorage.GetSetting(ClickraSettings.ImageCompressLevel);
                string originalMax = ClickraStorage.GetSetting(ClickraSettings.ImageCompressMaxDimension);
                try
                {
                    ClickraStorage.SaveSetting(ClickraSettings.ImageCompressLevel, "0");
                    ClickraStorage.SaveSetting(ClickraSettings.ImageCompressMaxDimension, "0");
                    ConvertCommandRunner.Run(
                        "img-compress",
                        new List<string> { source },
                        new List<string> { output },
                        (_, _, _) => { },
                        new ConvertCommandRunner.ConversionOptions(
                            _ => System.Threading.Tasks.Task.FromResult<string?>(null),
                            (_, _) => System.Threading.Tasks.Task.FromResult<string?>(null)));
                }
                finally
                {
                    ClickraStorage.SaveSetting(ClickraSettings.ImageCompressLevel, originalLevel);
                    ClickraStorage.SaveSetting(ClickraSettings.ImageCompressMaxDimension, originalMax);
                }

                Assert.True(File.Exists(output), "img-compress must create the expected _compressed output.");
                Assert.True(new FileInfo(output).Length < new FileInfo(source).Length,
                    "Low-quality JPEG compression must produce a smaller file for deterministic noisy input.");
                Assert.True(File.ReadAllBytes(source).SequenceEqual(originalBytes),
                    "img-compress must never modify the source file.");
            });
        });

        runner.Run("Image compression: explicit resize caps the long edge", () =>
        {
            RunWithTempDirectory(tempDir =>
            {
                string source = Path.Combine(tempDir, "wide.png");
                string output = Path.Combine(tempDir, "wide_compressed.png");
                using (var image = new Bitmap(320, 160))
                {
                    using var graphics = Graphics.FromImage(image);
                    graphics.Clear(Color.DarkSlateBlue);
                    image.Save(source, ImageFormat.Png);
                }

                FileProcessor.CompressImage(source, output, level: 1, maxDimension: 80);

                using var compressed = new Bitmap(output);
                Assert.True(compressed.Width == 80 && compressed.Height == 40,
                    $"Expected 80x40 after long-edge resize, got {compressed.Width}x{compressed.Height}.");
            });
        });

        runner.Run("Image compression: processor rejects source overwrite", () =>
        {
            RunWithTempDirectory(tempDir =>
            {
                string source = Path.Combine(tempDir, "source.png");
                using (var image = new Bitmap(32, 32)) image.Save(source, ImageFormat.Png);
                Assert.Throws<InvalidOperationException>(() => FileProcessor.CompressImage(source, source));
            });
        });
    }

    private static string CreateCompressionNoiseJpeg(string directory, string fileName, int width, int height, long quality)
    {
        string path = Path.Combine(directory, fileName);
        var random = new Random(20261006);
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                bitmap.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
        }
        var encoder = ImageCodecInfo.GetImageEncoders()
            .First(codec => string.Equals(codec.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase));
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        bitmap.Save(path, encoder, parameters);
        return path;
    }
}

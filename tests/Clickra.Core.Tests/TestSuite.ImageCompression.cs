using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string ImageCompressionLevelKey = "level";
    private const string ImageCompressionQualityKey = "quality";
    private const string ImageCompressCommand = "img-compress";

    public static void RegisterImageCompressionTests(TestRunner runner)
    {
        runner.Run("Image compression presets live in one table shared by the processor and the UIs", TestImageCompressionPresets);

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

        runner.Run("Image compression: " + ImageCompressCommand + " is a production registry command", () =>
        {
            Assert.True(ConvertCommandRegistry.IsKnownCommand(ImageCompressCommand),
                ImageCompressCommand + " must be registered before the Explorer menu can invoke it.");
            Assert.Equal("cmd_img_compress", ConvertCommandRegistry.GetLabelKey(ImageCompressCommand));
            Assert.True(ConvertCommandRegistry.GetCommandsForType("image").Contains(ImageCompressCommand, StringComparer.Ordinal),
                "Image command discovery must include " + ImageCompressCommand + ".");

            string inputDir = Path.Combine(Path.GetTempPath(), "clickra-registry-input");
            string outputDir = Path.Combine(Path.GetTempPath(), "clickra-registry-output");
            var outputs = ConvertCommandRegistry.EstimateImageCompressionOutputs(
                new List<string> { Path.Combine(inputDir, "photo.jpg") },
                outputDir);
            string expectedOutput = Path.GetFullPath(Path.Combine(outputDir, "photo_compressed.jpg"));
            Assert.True(outputs.Count == 1 && string.Equals(Path.GetFullPath(outputs[0]), expectedOutput, StringComparison.OrdinalIgnoreCase),
                ImageCompressCommand + " must honor the output-directory override and preserve the source extension.");

            string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(ImageCompressCommand);
            foreach (string extension in new[] { ".tif", ".tiff", ".heic", ".heif", ".hif" })
            {
                Assert.True(allowed.Contains(extension, StringComparer.OrdinalIgnoreCase),
                    $"{ImageCompressCommand} must accept the processor-supported extension {extension}.");
            }
        });

        runner.Run("Image compression: runner produces a smaller JPEG without overwriting input", () =>
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
                        ImageCompressCommand,
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

                Assert.True(File.Exists(output), ImageCompressCommand + " must create the expected _compressed output.");
                Assert.True(new FileInfo(output).Length < new FileInfo(source).Length,
                    "Low-quality JPEG compression must produce a smaller file for deterministic noisy input.");
                Assert.True(File.ReadAllBytes(source).SequenceEqual(originalBytes),
                    ImageCompressCommand + " must never modify the source file.");
            }));

        runner.Run("Image compression: explicit resize caps the long edge", () =>
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
            }));

        runner.Run("Image compression: processor rejects source overwrite", () =>
            RunWithTempDirectory(tempDir =>
            {
                string source = Path.Combine(tempDir, "source.png");
                using (var image = new Bitmap(32, 32)) image.Save(source, ImageFormat.Png);
                Assert.Throws<InvalidOperationException>(() => FileProcessor.CompressImage(source, source));
            }));

        runner.Run("Image compression: interface dispatch initializes processor state", () =>
            RunWithTempDirectory(tempDir =>
            {
                string source = Path.Combine(tempDir, "interface.png");
                string output = Path.Combine(tempDir, "interface_compressed.png");
                using (var image = new Bitmap(40, 40)) image.Save(source, ImageFormat.Png);

                IFileProcessor processor = new ImageCompressionProcessor();
                processor.Process(
                    new List<string> { source },
                    output,
                    ConvertCommandRegistry.ImageCompressionOptions());

                Assert.True(File.Exists(output),
                    "IFileProcessor dispatch must run ImageCompressionProcessor initialization before processing.");
            }));

        runner.RunGuard("Image compression guard: orientation and multi-frame safety stay explicit", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException("Could not locate repository root.");
            string processor = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "FileProcessing", "ImageCompressionProcessor.cs"));
            string wic = File.ReadAllText(Path.Combine(root, "src", "Clickra.Core", "Processors", "FileProcessing", "WicImageHelper.cs"));

            Assert.True(processor.Contains("IsMultiFrameImage(filePath, extension)", StringComparison.Ordinal)
                        && processor.Contains("error_img_compress_multiframe_resize", StringComparison.Ordinal),
                "Compression must preserve or reject multi-frame/page media before single-frame re-encoding.");
            Assert.True(wic.Contains("ApplyExifOrientation(original, bitmap)", StringComparison.Ordinal)
                        && wic.Contains("OrientationPropertyId = 0x0112", StringComparison.Ordinal),
                "Decoded JPEG/TIFF pixels must apply EXIF orientation before resize or re-encode.");
        });

        runner.Run("Image compression: output planning rejects collisions with selected inputs", () =>
            RunWithTempDirectory(tempDir =>
            {
                string first = Path.Combine(tempDir, "photo.jpg");
                string second = Path.Combine(tempDir, "photo_compressed.jpg");
                File.WriteAllBytes(first, new byte[] { 1 });
                File.WriteAllBytes(second, new byte[] { 2 });

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                    ConvertCommandRegistry.EstimateImageCompressionOutputs(new List<string> { first, second }, tempDir));
                Assert.True(ex.Message.Contains("photo_compressed.jpg", StringComparison.OrdinalIgnoreCase),
                    "Collision error must identify the selected input that would be overwritten.");
                Assert.True(File.ReadAllBytes(second).SequenceEqual(new byte[] { 2 }),
                    "Planning a rejected compression batch must leave every selected input untouched.");
            }));

        runner.Run("Image compression: batch snapshots settings before processing", () =>
            RunWithTempDirectory(tempDir =>
            {
                string first = CreateCompressionNoiseJpeg(tempDir, "first.jpg", 192, 192, 95L);
                string second = Path.Combine(tempDir, "second.jpg");
                File.Copy(first, second);
                var files = new List<string> { first, second };
                var outputs = ConvertCommandRegistry.EstimateImageCompressionOutputs(files, tempDir);
                string originalLevel = ClickraStorage.GetSetting(ClickraSettings.ImageCompressLevel);
                try
                {
                    ClickraStorage.SaveSetting(ClickraSettings.ImageCompressLevel, "0");
                    bool changed = false;
                    ConvertCommandRunner.Run(
                        ImageCompressCommand,
                        files,
                        outputs,
                        (_, _, _) =>
                        {
                            if (changed) return;
                            changed = true;
                            ClickraStorage.SaveSetting(ClickraSettings.ImageCompressLevel, "3");
                        },
                        new ConvertCommandRunner.ConversionOptions(
                            _ => Task.FromResult<string?>(null),
                            (_, _) => Task.FromResult<string?>(null)));

                    Assert.True(new FileInfo(outputs[0]).Length == new FileInfo(outputs[1]).Length,
                        "One compression batch must use one settings snapshot even if the saved setting changes mid-run.");
                }
                finally
                {
                    ClickraStorage.SaveSetting(ClickraSettings.ImageCompressLevel, originalLevel);
                }
            }));

        runner.Run("Image compression: newer concurrent request wins output promotion", () =>
            RunWithTempDirectory(tempDir =>
            {
                string source = CreateCompressionNoiseJpeg(tempDir, "race.jpg", 256, 256, 95L);
                string output = Path.Combine(tempDir, "race_compressed.jpg");
                string expected = Path.Combine(tempDir, "expected.jpg");
                var highOptions = new Dictionary<string, object>
                {
                    ["level"] = ImageCompressionOptions.OptionHigh,
                    ["quality"] = ImageCompressionOptions.GetQuality(ImageCompressionLevel.High),
                    ["max_dimension"] = 0
                };
                var lowOptions = new Dictionary<string, object>
                {
                    ["level"] = ImageCompressionOptions.OptionMin,
                    ["quality"] = ImageCompressionOptions.GetQuality(ImageCompressionLevel.Minimum),
                    ["max_dimension"] = 0
                };
                FileProcessor.CompressImage(source, expected, highOptions);
                long expectedLength = new FileInfo(expected).Length;

                using var oldStarted = new ManualResetEventSlim(false);
                using var releaseOld = new ManualResetEventSlim(false);
                bool held = false;
                Task older = Task.Run(() => FileProcessor.CompressImage(
                    source,
                    output,
                    lowOptions,
                    (_, _, _) =>
                    {
                        if (held) return;
                        held = true;
                        oldStarted.Set();
                        releaseOld.Wait(TimeSpan.FromSeconds(10));
                    }));

                Assert.True(oldStarted.Wait(TimeSpan.FromSeconds(10)), "Older request did not reach the compression stage.");
                Task newer = Task.Run(() => FileProcessor.CompressImage(source, output, highOptions));
                Assert.True(newer.Wait(TimeSpan.FromSeconds(10)), "Newer request did not finish while the older request was paused.");
                releaseOld.Set();
                Assert.True(older.Wait(TimeSpan.FromSeconds(10)), "Older request did not finish after release.");

                Assert.True(new FileInfo(output).Length == expectedLength,
                    "An older request finishing later must not replace the newer request's output.");
            }));

        runner.Run("Image compression: progress error logging does not rethrow output planning failures", () =>
        {
            string? root = FindRepoRoot();
            if (root is null) throw new TestSkippedException("Could not locate repository root.");
            string progress = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Progress", "ProgressWindow.Process.cs"));
            string normalizedProgress = progress.Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.True(progress.Contains("GetOutputPathForError(cmd, currentFiles, outputDir, _outputDirOverride)", StringComparison.Ordinal),
                "Progress failure handling must use the non-throwing output-path logger.");
            Assert.True(progress.Contains("private static string GetOutputPathForError", StringComparison.Ordinal)
                        && normalizedProgress.Contains("catch\n            {\n                return \"\";\n            }", StringComparison.Ordinal),
                "Failure-history output rendering must not mask the original exception when output planning also fails.");
        });
    }

    private static void TestImageCompressionPresets()
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
    }

    private static string CreateCompressionNoiseJpeg(string directory, string fileName, int width, int height, long quality)
    {
        string path = Path.Combine(directory, fileName);
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int value = unchecked(((x + 1) * 73_856_093) ^ ((y + 1) * 19_349_663));
                bitmap.SetPixel(x, y, Color.FromArgb(value & 0xFF, (value >> 8) & 0xFF, (value >> 16) & 0xFF));
            }
        }
        var encoder = ImageCodecInfo.GetImageEncoders()
            .First(codec => string.Equals(codec.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase));
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        bitmap.Save(path, encoder, parameters);
        return path;
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
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
                Assert.Equal(ImageCompressionOptions.OptionMin, (string)opt0["level"]);
                Assert.Equal(ImageCompressionOptions.GetQuality(ImageCompressionLevel.Minimum), (int)opt0["quality"]);

                ClickraStorage.SaveSetting(ClickraSettings.ImageCompressLevel, "1");
                var opt1 = ConvertCommandRegistry.ImageCompressionOptions();
                Assert.Equal(ImageCompressionOptions.OptionSmall, (string)opt1["level"]);
                Assert.Equal(ImageCompressionOptions.GetQuality(ImageCompressionLevel.Small), (int)opt1["quality"]);

                ClickraStorage.SaveSetting(ClickraSettings.ImageCompressLevel, "2");
                var opt2 = ConvertCommandRegistry.ImageCompressionOptions();
                Assert.Equal(ImageCompressionOptions.OptionStandard, (string)opt2["level"]);
                Assert.Equal(ImageCompressionOptions.GetQuality(ImageCompressionLevel.Standard), (int)opt2["quality"]);

                ClickraStorage.SaveSetting(ClickraSettings.ImageCompressLevel, "3");
                var opt3 = ConvertCommandRegistry.ImageCompressionOptions();
                Assert.Equal(ImageCompressionOptions.OptionHigh, (string)opt3["level"]);
                Assert.Equal(ImageCompressionOptions.GetQuality(ImageCompressionLevel.High), (int)opt3["quality"]);
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
    }
}

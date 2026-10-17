using System;
using System.Collections.Generic;
using System.IO;
using Clickra.Core.Application;
using Clickra.Core.Modules.Image.Application;
using Clickra.Core.Modules.Image.Processing;
using Clickra.Core.Modules.Markdown.Application;
using Clickra.Core.Modules.Office.Application;
using Clickra.Core.Modules.Office.Processing;
using Clickra.Core.Modules.PdfCompression.Application;
using Clickra.Core.Modules.PdfCompression.Processing;
using Clickra.Core.Modules.PdfDecrypt.Application;
using Clickra.Core.Modules.PdfTranslation.Application;
using Clickra.Core.Modules.Settings.Application;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterUseCaseSettingsInjectionTests(TestRunner runner)
    {
        runner.Run("Use case options injection: PdfDecrypt honors caller language override", () =>
        {
            var useCase = new DecryptPdfUseCase();
            var requestWithLang = new ConversionRequest(
                DecryptPdfUseCase.CommandName,
                new[] { "test.pdf" },
                new Dictionary<string, object>
                {
                    [ExecutionLocalizationOptions.LanguageKey] = "ja"
                });
            ConversionPlan plan = useCase.Plan(requestWithLang);
            Assert.Equal("ja", (string)plan.NormalizedOptions[ExecutionLocalizationOptions.LanguageKey]);

            var requestWithoutLang = new ConversionRequest(
                DecryptPdfUseCase.CommandName,
                new[] { "test.pdf" });
            ConversionPlan defaultPlan = useCase.Plan(requestWithoutLang);
            Assert.Equal(SettingsController.Language, (string)defaultPlan.NormalizedOptions[ExecutionLocalizationOptions.LanguageKey]);
        });

        runner.Run("Use case options injection: Markdown honors caller language override", () =>
        {
            var useCase = new MarkdownToPdfUseCase();
            string testLang = SettingsController.Language == "fr" ? "de" : "fr";
            var requestWithLang = new ConversionRequest(
                MarkdownToPdfUseCase.CommandName,
                new[] { "notes.md" },
                new Dictionary<string, object>
                {
                    [ExecutionLocalizationOptions.LanguageKey] = testLang
                });
            ConversionPlan plan = useCase.Plan(requestWithLang);
            Assert.Equal(testLang, (string)plan.NormalizedOptions[ExecutionLocalizationOptions.LanguageKey]);

            var requestWithoutLang = new ConversionRequest(
                MarkdownToPdfUseCase.CommandName,
                new[] { "notes.md" });
            ConversionPlan defaultPlan = useCase.Plan(requestWithoutLang);
            Assert.Equal(SettingsController.Language, (string)defaultPlan.NormalizedOptions[ExecutionLocalizationOptions.LanguageKey]);
        });

        runner.Run("Use case options injection: ImageFormatConvert honors caller language override", () =>
        {
            var useCase = new ImageFormatConvertUseCase(ImageFormatConvertUseCase.PngCommand);
            var requestWithLang = new ConversionRequest(
                ImageFormatConvertUseCase.PngCommand,
                new[] { "photo.jpg" },
                new Dictionary<string, object>
                {
                    [ExecutionLocalizationOptions.LanguageKey] = "zh-CN"
                });
            ConversionPlan plan = useCase.Plan(requestWithLang);
            Assert.Equal("zh-CN", (string)plan.NormalizedOptions[ExecutionLocalizationOptions.LanguageKey]);

            var requestWithoutLang = new ConversionRequest(
                ImageFormatConvertUseCase.PngCommand,
                new[] { "photo.jpg" });
            ConversionPlan defaultPlan = useCase.Plan(requestWithoutLang);
            Assert.Equal(SettingsController.Language, (string)defaultPlan.NormalizedOptions[ExecutionLocalizationOptions.LanguageKey]);
        });

        runner.Run("Use case options injection: ImageCompress honors caller level, max_dimension, and language", () =>
        {
            var useCase = new ImgCompressUseCase();
            var requestWithOptions = new ConversionRequest(
                ImgCompressUseCase.CommandName,
                new[] { "photo.png" },
                new Dictionary<string, object>
                {
                    ["level"] = "low",
                    ["max_dimension"] = 1280,
                    [ExecutionLocalizationOptions.LanguageKey] = "de"
                });
            ConversionPlan plan = useCase.Plan(requestWithOptions);
            Assert.Equal("low", (string)plan.NormalizedOptions["level"]);
            Assert.Equal(1280, Convert.ToInt64(plan.NormalizedOptions["max_dimension"]));
            Assert.Equal("de", (string)plan.NormalizedOptions[ExecutionLocalizationOptions.LanguageKey]);

            var requestWithoutOptions = new ConversionRequest(
                ImgCompressUseCase.CommandName,
                new[] { "photo.png" });
            ConversionPlan defaultPlan = useCase.Plan(requestWithoutOptions);
            Assert.Equal(SettingsController.Language, (string)defaultPlan.NormalizedOptions[ExecutionLocalizationOptions.LanguageKey]);
        });

        runner.Run("Use case options injection: PdfCompression honors caller level, strip_fonts, and minify_content", () =>
        {
            var useCase = new CompressPdfUseCase();
            bool targetStripFonts = !SettingsController.PdfCompressionStripFonts;
            bool targetMinify = !SettingsController.PdfCompressionMinifyContent;
            var requestWithOptions = new ConversionRequest(
                CompressPdfUseCase.CommandName,
                new[] { "sample.pdf" },
                new Dictionary<string, object>
                {
                    [CompressPdfUseCase.LevelOptionKey] = "high",
                    ["strip_fonts"] = targetStripFonts,
                    ["minify_content"] = targetMinify
                });
            ConversionPlan plan = useCase.Plan(requestWithOptions);
            Assert.Equal("high", (string)plan.NormalizedOptions[CompressPdfUseCase.LevelOptionKey]);
            Assert.True((bool)plan.NormalizedOptions["strip_fonts"] == targetStripFonts, $"strip_fonts should be {targetStripFonts}");
            Assert.True((bool)plan.NormalizedOptions["minify_content"] == targetMinify, $"minify_content should be {targetMinify}");

            var requestWithoutOptions = new ConversionRequest(
                CompressPdfUseCase.CommandName,
                new[] { "sample.pdf" });
            ConversionPlan defaultPlan = useCase.Plan(requestWithoutOptions);
            Assert.True((bool)defaultPlan.NormalizedOptions["strip_fonts"] == SettingsController.PdfCompressionStripFonts, "Default strip_fonts should match SettingsController");
            Assert.True((bool)defaultPlan.NormalizedOptions["minify_content"] == SettingsController.PdfCompressionMinifyContent, "Default minify_content should match SettingsController");
        });

        runner.Run("Use case options injection: PdfTranslation honors caller target_language, language, and data_dir", () =>
        {
            var useCase = new TranslatePdfUseCase();
            var requestWithOptions = new ConversionRequest(
                TranslatePdfUseCase.CommandName,
                new[] { "paper.pdf" },
                new Dictionary<string, object>
                {
                    [TranslatePdfUseCase.TargetLanguageOptionKey] = "ja",
                    [ExecutionLocalizationOptions.LanguageKey] = "en",
                    [ProcessorExecutionOptions.DataDirectoryKey] = "C:\\CustomData"
                });
            ConversionPlan plan = useCase.Plan(requestWithOptions);
            Assert.Equal("ja", (string)plan.NormalizedOptions[TranslatePdfUseCase.TargetLanguageOptionKey]);
            Assert.Equal("en", (string)plan.NormalizedOptions[ExecutionLocalizationOptions.LanguageKey]);
            Assert.Equal("C:\\CustomData", (string)plan.NormalizedOptions[ProcessorExecutionOptions.DataDirectoryKey]);

            var requestWithoutOptions = new ConversionRequest(
                TranslatePdfUseCase.CommandName,
                new[] { "paper.pdf" });
            ConversionPlan defaultPlan = useCase.Plan(requestWithoutOptions);
            Assert.Equal(SettingsController.TranslationTargetLanguage, (string)defaultPlan.NormalizedOptions[TranslatePdfUseCase.TargetLanguageOptionKey]);
            Assert.Equal(SettingsController.Language, (string)defaultPlan.NormalizedOptions[ExecutionLocalizationOptions.LanguageKey]);
        });

        runner.Run("Use case options injection: Office conversion honors caller engine, language, and preserves trusted path", () =>
        {
            var useCase = new WordToPdfUseCase();
            var requestWithOptions = new ConversionRequest(
                WordToPdfUseCase.CommandName,
                new[] { "doc.docx" },
                new Dictionary<string, object>
                {
                    [OfficeConversionOptions.EngineKey] = "custom-libreoffice",
                    [OfficeConversionOptions.LanguageKey] = "fr",
                    [OfficeConversionOptions.LibreOfficePathKey] = "C:\\Malicious\\soffice.exe",
                    [OfficeConversionOptions.LibreOfficeRemovalPendingRestartKey] = true
                });
            ConversionPlan plan = useCase.Plan(requestWithOptions);
            Assert.Equal("custom-libreoffice", (string)plan.NormalizedOptions[OfficeConversionOptions.EngineKey]);
            Assert.Equal("fr", (string)plan.NormalizedOptions[OfficeConversionOptions.LanguageKey]);
            // Security: path must be strictly taken from SettingsController, ignoring caller override
            Assert.Equal(SettingsController.LibreOfficePath, (string)plan.NormalizedOptions[OfficeConversionOptions.LibreOfficePathKey]);
            Assert.True((bool)plan.NormalizedOptions[OfficeConversionOptions.LibreOfficeRemovalPendingRestartKey], "removal pending restart should be true");

            var requestWithoutOptions = new ConversionRequest(
                WordToPdfUseCase.CommandName,
                new[] { "doc.docx" });
            ConversionPlan defaultPlan = useCase.Plan(requestWithoutOptions);
            Assert.Equal(SettingsController.OfficeEngineValue, (string)defaultPlan.NormalizedOptions[OfficeConversionOptions.EngineKey]);
            Assert.Equal(SettingsController.Language, (string)defaultPlan.NormalizedOptions[OfficeConversionOptions.LanguageKey]);
        });
    }
}

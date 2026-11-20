using System;
using System.Collections.Generic;
using System.IO;
using Clickra.Core.Application;
using Clickra.Core.Modules.Img.Compression.Application;
using Clickra.Core.Modules.Img.FormatConvert.Application;
using Clickra.Core.Modules.Img.Compression.Processing;
using Clickra.Core.Modules.Img.FormatConvert.Processing;
using Clickra.Core.Modules.Doc.MarkdownPdf.Application;
using Clickra.Core.Modules.Doc.MarkdownWord.Application;
using Clickra.Core.Modules.Doc.Shared.Application;
using Clickra.Core.Modules.Doc.Word.Application;
using Clickra.Core.Modules.Doc.Excel.Application;
using Clickra.Core.Modules.Doc.Ppt.Application;
using Clickra.Core.Modules.Doc.Word.Processing;
using Clickra.Core.Modules.Doc.Excel.Processing;
using Clickra.Core.Modules.Doc.Ppt.Processing;
using Clickra.Core.Modules.Pdf.Compression.Application;
using Clickra.Core.Modules.Pdf.Compression.Processing;
using Clickra.Core.Modules.Pdf.Decrypt.Application;
using Clickra.Core.Modules.Pdf.Translation.Application;
using Clickra.Core.Modules.Pdf.ToImage.Application;
using Clickra.Core.Modules.Pdf.Print.Application;
using Clickra.Core.Storage;
using Clickra.Core.Modules.Sys.Settings.Application;
using Clickra.Core.Presentation.Convert;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string SamplePdfFile = "sample.pdf";
    private const string NotesMdFile = "notes.md";

    public static void RegisterUseCaseSettingsInjectionTests(TestRunner runner)
    {
        runner.Run("Use case options injection: PdfToImage falls back to the saved format, dpi and stitch", () =>
        {
            var useCase = new PdfToImageUseCase();
            ConversionPlan defaultPlan = useCase.Plan(new ConversionRequest(PdfToImageUseCase.CommandName, new[] { SamplePdfFile }));
            Assert.Equal(PdfToImageOptions.NormalizeFormat(SettingsController.PdfToImageFormat), (string)defaultPlan.NormalizedOptions[PdfToImageOptions.FormatOptionKey]);
            Assert.Equal(PdfToImageOptions.NormalizeDpi(SettingsController.PdfToImageDpi), (int)defaultPlan.NormalizedOptions[PdfToImageOptions.DpiOptionKey]);
            Assert.True((bool)defaultPlan.NormalizedOptions[PdfToImageOptions.StitchOptionKey] == SettingsController.PdfToImageStitch, "Default stitch should match SettingsController");

            ConversionPlan plan = useCase.Plan(new ConversionRequest(PdfToImageUseCase.CommandName, new[] { SamplePdfFile },
                new Dictionary<string, object>
                {
                    [PdfToImageOptions.FormatOptionKey] = "webp",
                    [PdfToImageOptions.DpiOptionKey] = 300,
                    [PdfToImageOptions.StitchOptionKey] = !SettingsController.PdfToImageStitch
                }));
            Assert.Equal("webp", (string)plan.NormalizedOptions[PdfToImageOptions.FormatOptionKey]);
            Assert.Equal(300, (int)plan.NormalizedOptions[PdfToImageOptions.DpiOptionKey]);
            Assert.True((bool)plan.NormalizedOptions[PdfToImageOptions.StitchOptionKey] == !SettingsController.PdfToImageStitch, "Caller stitch must win");
        });

        runner.Run("Use case options injection: Markdown falls back to the saved theme, paper, text size and code theme", () =>
        {
            var useCase = new MarkdownToPdfUseCase();
            ConversionPlan defaultPlan = useCase.Plan(new ConversionRequest(MarkdownToPdfUseCase.CommandName, new[] { NotesMdFile }));
            var expected = new Dictionary<string, object>
            {
                [MarkdownPdfOptions.PaperKey] = SettingsController.MarkdownPaper,
                [MarkdownPdfOptions.TextSizeKey] = SettingsController.MarkdownTextSize,
                [MarkdownPdfOptions.CodeThemeKey] = SettingsController.MarkdownCodeTheme
            };
            if (SettingsController.MarkdownTheme != ClickraSettings.MarkdownThemeWordTemplate)
            {
                expected[MarkdownPdfOptions.ThemeKey] = SettingsController.MarkdownTheme;
                Assert.Equal(MarkdownPdfOptions.GetTheme(expected), (string)defaultPlan.NormalizedOptions[MarkdownPdfOptions.ThemeKey]);
            }
            Assert.Equal(MarkdownPdfOptions.GetPaper(expected), (string)defaultPlan.NormalizedOptions[MarkdownPdfOptions.PaperKey]);
            Assert.Equal(MarkdownPdfOptions.GetTextSize(expected), (string)defaultPlan.NormalizedOptions[MarkdownPdfOptions.TextSizeKey]);
            Assert.Equal(MarkdownPdfOptions.GetCodeTheme(expected), (string)defaultPlan.NormalizedOptions[MarkdownPdfOptions.CodeThemeKey]);

            ConversionPlan plan = useCase.Plan(new ConversionRequest(MarkdownToPdfUseCase.CommandName, new[] { NotesMdFile },
                new Dictionary<string, object>
                {
                    [MarkdownPdfOptions.ThemeKey] = MarkdownPdfOptions.ThemeAcademic,
                    [MarkdownPdfOptions.PaperKey] = MarkdownPdfOptions.PaperLetter,
                    [MarkdownPdfOptions.TextSizeKey] = MarkdownPdfOptions.TextLarge,
                    [MarkdownPdfOptions.CodeThemeKey] = MarkdownPdfOptions.CodeLight
                }));
            Assert.Equal(MarkdownPdfOptions.ThemeAcademic, (string)plan.NormalizedOptions[MarkdownPdfOptions.ThemeKey]);
            Assert.Equal(MarkdownPdfOptions.PaperLetter, (string)plan.NormalizedOptions[MarkdownPdfOptions.PaperKey]);
            Assert.Equal(MarkdownPdfOptions.TextLarge, (string)plan.NormalizedOptions[MarkdownPdfOptions.TextSizeKey]);
            Assert.Equal(MarkdownPdfOptions.CodeLight, (string)plan.NormalizedOptions[MarkdownPdfOptions.CodeThemeKey]);
        });

        runner.Run("Use case options injection: Print falls back to the saved copies, duplex, orientation, fit and darken", () =>
        {
            var useCase = new PrintPdfUseCase();
            ConversionPlan defaultPlan = useCase.Plan(new ConversionRequest(PrintPdfUseCase.CommandName, new[] { SamplePdfFile }));
            var saved = PrintPdfOptions.FromDictionary(new Dictionary<string, object>
            {
                [PrintPdfOptions.CopiesKey] = SettingsController.PrintCopies,
                [PrintPdfOptions.DuplexKey] = SettingsController.PrintDuplex,
                [PrintPdfOptions.OrientationKey] = SettingsController.PrintOrientation,
                [PrintPdfOptions.FitToPageKey] = SettingsController.PrintFitToPage,
                [PrintPdfOptions.DarkenTextKey] = SettingsController.PrintDarkenText
            });
            var planned = PrintPdfOptions.FromDictionary(defaultPlan.NormalizedOptions);
            Assert.Equal(saved.Copies, planned.Copies);
            Assert.True(saved.Duplex == planned.Duplex, "Default duplex should match SettingsController");
            Assert.True(saved.Orientation == planned.Orientation, "Default orientation should match SettingsController");
            Assert.True(saved.FitToPage == planned.FitToPage, "Default fit-to-page should match SettingsController");
            Assert.True(saved.DarkenText == planned.DarkenText, "Default darken-text should match SettingsController");

            ConversionPlan plan = useCase.Plan(new ConversionRequest(PrintPdfUseCase.CommandName, new[] { SamplePdfFile },
                new Dictionary<string, object> { [PrintPdfOptions.CopiesKey] = 7 }));
            Assert.Equal(7, PrintPdfOptions.FromDictionary(plan.NormalizedOptions).Copies);
        });

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
                new[] { NotesMdFile },
                new Dictionary<string, object>
                {
                    [ExecutionLocalizationOptions.LanguageKey] = testLang
                });
            ConversionPlan plan = useCase.Plan(requestWithLang);
            Assert.Equal(testLang, (string)plan.NormalizedOptions[ExecutionLocalizationOptions.LanguageKey]);

            var requestWithoutLang = new ConversionRequest(
                MarkdownToPdfUseCase.CommandName,
                new[] { NotesMdFile });
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
                new[] { SamplePdfFile },
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
                new[] { SamplePdfFile });
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
                    [TranslatePdfUseCase.EngineOptionKey] = "custom-ai",
                    [TranslatePdfUseCase.AiEndpointOptionKey] = "http://localhost:11434/v1",
                    [TranslatePdfUseCase.AiApiKeyOptionKey] = "test-key",
                    [TranslatePdfUseCase.AiModelOptionKey] = "llama3",
                    [ExecutionLocalizationOptions.LanguageKey] = "en",
                    [ProcessorExecutionOptions.DataDirectoryKey] = "C:\\CustomData"
                });
            ConversionPlan plan = useCase.Plan(requestWithOptions);
            Assert.Equal("ja", (string)plan.NormalizedOptions[TranslatePdfUseCase.TargetLanguageOptionKey]);
            Assert.Equal("custom-ai", (string)plan.NormalizedOptions[TranslatePdfUseCase.EngineOptionKey]);
            Assert.Equal("http://localhost:11434/v1", (string)plan.NormalizedOptions[TranslatePdfUseCase.AiEndpointOptionKey]);
            Assert.Equal("test-key", (string)plan.NormalizedOptions[TranslatePdfUseCase.AiApiKeyOptionKey]);
            Assert.Equal("llama3", (string)plan.NormalizedOptions[TranslatePdfUseCase.AiModelOptionKey]);
            Assert.Equal("en", (string)plan.NormalizedOptions[ExecutionLocalizationOptions.LanguageKey]);
            Assert.Equal("C:\\CustomData", (string)plan.NormalizedOptions[ProcessorExecutionOptions.DataDirectoryKey]);

            var requestWithoutOptions = new ConversionRequest(
                TranslatePdfUseCase.CommandName,
                new[] { "paper.pdf" });
            ConversionPlan defaultPlan = useCase.Plan(requestWithoutOptions);
            Assert.Equal(SettingsController.TranslationTargetLanguage, (string)defaultPlan.NormalizedOptions[TranslatePdfUseCase.TargetLanguageOptionKey]);
            Assert.Equal(SettingsController.TranslateEngine, (string)defaultPlan.NormalizedOptions[TranslatePdfUseCase.EngineOptionKey]);
            Assert.Equal(SettingsController.TranslateAiEndpoint, (string)defaultPlan.NormalizedOptions[TranslatePdfUseCase.AiEndpointOptionKey]);
            Assert.Equal(SettingsController.TranslateAiApiKey, (string)defaultPlan.NormalizedOptions[TranslatePdfUseCase.AiApiKeyOptionKey]);
            Assert.Equal(SettingsController.TranslateAiModel, (string)defaultPlan.NormalizedOptions[TranslatePdfUseCase.AiModelOptionKey]);
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

        runner.Run("Unified conversion execution: ConversionExecutionService plans and delegates properly", () =>
        {
            var options = new Dictionary<string, object>
            {
                [MarkdownPdfOptions.ThemeKey] = MarkdownPdfOptions.ThemeAcademic,
                [MarkdownPdfOptions.PaperKey] = MarkdownPdfOptions.PaperLetter,
                [ExecutionLocalizationOptions.LanguageKey] = "ja"
            };
            ConversionPlan plan = ConversionExecutionService.Plan(
                MarkdownToPdfUseCase.CommandName,
                new[] { "test.md" },
                options: options,
                outputOverride: "C:\\out",
                TrackTaskLifecycle: false,
                planOptions: new ExecutionPlanOptions(ResumeStartIndex: 2));

            Assert.Equal(MarkdownToPdfUseCase.CommandName, plan.Command);
            Assert.False(plan.TrackTaskLifecycle, "TrackTaskLifecycle should be false");
            Assert.Equal(2, plan.ResumeStartIndex);
            Assert.False(plan.RequiredCapabilities.Contains(ConversionCapability.MarkdownOptions), "Plan drops MarkdownOptions capability when options are already supplied");
            Assert.Equal(MarkdownPdfOptions.ThemeAcademic, (string)plan.NormalizedOptions[MarkdownPdfOptions.ThemeKey]);
            Assert.Equal(MarkdownPdfOptions.PaperLetter, (string)plan.NormalizedOptions[MarkdownPdfOptions.PaperKey]);
            Assert.Equal("ja", (string)plan.NormalizedOptions[ExecutionLocalizationOptions.LanguageKey]);

            Assert.True(ConversionExecutionController.RequiresOptions(MarkdownToPdfUseCase.CommandName), "Controller detects md2pdf options");
            Assert.True(ConversionExecutionController.RequiresOptions(MarkdownToWordUseCase.CommandName), "Controller detects md2word options");
            Assert.False(ConversionExecutionController.RequiresOptions(WordToPdfUseCase.CommandName), "Controller rejects word2pdf options");
            Assert.False(ConversionExecutionController.RequiresOptions(DecryptPdfUseCase.CommandName), "Controller rejects decrypt-pdf options");
            Assert.True(ConversionExecutionController.HasCapability(DecryptPdfUseCase.CommandName, ConversionCapability.Password), "Controller has password capability for decrypt-pdf");
        });
    }
}

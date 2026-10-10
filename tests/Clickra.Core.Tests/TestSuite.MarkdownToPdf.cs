using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Clickra.Core;
using Clickra.Core.Application;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string MarkdownToPdfCommand = "md2pdf";
    private const string TimesFontToken = "Times";
    private const string UnknownMarkdownOptionValue = "unknown";
    private const string MarkdownPdfCliDirectoryName = "Clickra.CLI";

    private static string[] GetMarkdownPdfFontNames(string path)
    {
        using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(path, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        return pdf.Internals.GetAllObjects()
            .OfType<PdfSharp.Pdf.PdfDictionary>()
            .Where(dictionary => dictionary.Elements.GetName("/Type") == "/Font")
            .Select(dictionary => dictionary.Elements.GetName("/BaseFont"))
            .Where(name => !string.IsNullOrEmpty(name))
            .ToArray();
    }

    private static string ConvertMarkdownWithWordTemplate(string tempDir, string stem, string markdown)
    {
        string input = Path.Combine(tempDir, stem + ".md");
        string output = Path.Combine(tempDir, stem + ".pdf");
        string templatePath = Path.Combine(tempDir, stem + ".docx");
        CreateWordTemplateFixture(templatePath);
        File.WriteAllText(input, markdown);
        FileProcessor.ConvertMarkdownToPdf(input, output, MarkdownPdfOptions.Create(templatePath: templatePath));
        return output;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S3011", Justification = "The test intentionally reflects a private renderer classifier to verify typography behavior without widening production visibility.")]
    private static bool MarkdownRendererUsesCjkTypography(char ch)
    {
        Type rendererType = typeof(MarkdownToPdfProcessor).GetNestedType(
            "Renderer", System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Markdown PDF renderer type was not found.");
        var classifier = rendererType.GetMethod(
            "IsCjkTypographyChar",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("Markdown CJK typography classifier was not found.");
        return (bool)(classifier.Invoke(null, new object[] { ch })
            ?? throw new InvalidOperationException("Markdown CJK typography classifier returned no result."));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S3776", Justification = "This method registers independent Markdown PDF test cases; splitting registration adds indirection without reducing test logic.")]
    public static void RegisterMarkdownToPdfTests(TestRunner runner)
    {
        runner.Run("Markdown to PDF use case snapshots options and output planning", () =>
            RunWithTempDirectory(tempDir =>
            {
                string sourceDir = Path.Combine(tempDir, "source");
                string outputDir = Path.Combine(tempDir, "output");
                Directory.CreateDirectory(sourceDir);
                Directory.CreateDirectory(outputDir);
                string input = Path.Combine(sourceDir, "notes.md");
                string template = Path.Combine(tempDir, "template.docx");
                File.WriteAllText(input, "# Notes");
                CreateWordTemplateFixture(template);

                var requested = MarkdownPdfOptions.Create(
                    MarkdownPdfOptions.ThemeAcademic,
                    MarkdownPdfOptions.PaperLetter,
                    MarkdownPdfOptions.TextLarge,
                    MarkdownPdfOptions.CodeLight,
                    template);
                var useCase = new MarkdownToPdfUseCase();
                ConversionPlan plan = useCase.Plan(new ConversionRequest(
                    MarkdownToPdfUseCase.CommandName,
                    new[] { input },
                    requested,
                    OutputOverride: outputDir,
                    TrackTaskLifecycle: false));

                Assert.Equal(Path.Combine(Path.GetFullPath(outputDir), "notes.pdf"), plan.Outputs[0]);
                Assert.Equal(MarkdownPdfOptions.ThemeAcademic, MarkdownPdfOptions.GetTheme(plan.NormalizedOptions));
                Assert.Equal(MarkdownPdfOptions.PaperLetter, MarkdownPdfOptions.GetPaper(plan.NormalizedOptions));
                Assert.Equal(MarkdownPdfOptions.TextLarge, MarkdownPdfOptions.GetTextSize(plan.NormalizedOptions));
                Assert.Equal(MarkdownPdfOptions.CodeLight, MarkdownPdfOptions.GetCodeTheme(plan.NormalizedOptions));
                Assert.Equal(Path.GetFullPath(template), MarkdownPdfOptions.GetTemplatePath(plan.NormalizedOptions) ?? "");
                requested[MarkdownPdfOptions.ThemeKey] = MarkdownPdfOptions.ThemeDefault;
                Assert.Equal(MarkdownPdfOptions.ThemeAcademic, MarkdownPdfOptions.GetTheme(plan.NormalizedOptions));
                Assert.False(plan.TrackTaskLifecycle, "Quiet Markdown execution must be able to disable task tracking.");
            }));

        runner.Run("Markdown to PDF: pre-cancel preserves existing output", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "cancel.md");
                string output = Path.Combine(tempDir, "existing.pdf");
                File.WriteAllText(input, "# Must not convert");
                byte[] original = { 11, 12, 13, 14 };
                File.WriteAllBytes(output, original);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    new MarkdownToPdfProcessor().Process(new List<string> { input }, output,
                        cancellationToken: cancellation.Token));
                Assert.True(File.ReadAllBytes(output).SequenceEqual(original),
                    "Cancellation before parsing must preserve existing PDF bytes.");
            }));

        runner.Run("Markdown to PDF: registry exposes Markdown inputs and PDF outputs", () =>
        {
            Assert.True(ConvertCommandRegistry.IsKnownCommand(MarkdownToPdfCommand), MarkdownToPdfCommand + " must be a registered conversion command.");
            string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(MarkdownToPdfCommand);
            Assert.True(allowed.Contains(".md", StringComparer.OrdinalIgnoreCase), MarkdownToPdfCommand + " must accept .md files.");
            Assert.True(allowed.Contains(".markdown", StringComparer.OrdinalIgnoreCase), MarkdownToPdfCommand + " must accept .markdown files.");
            Assert.True(ConvertCommandRegistry.GetCommandsForType("markdown").Contains(MarkdownToPdfCommand, StringComparer.Ordinal),
                "Markdown command discovery must include " + MarkdownToPdfCommand + ".");

            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "notes.md");
                File.WriteAllText(input, "# Notes");
                List<string> outputs = ConvertCommandRegistry.EstimateOutputs(MarkdownToPdfCommand, new List<string> { input });
                Assert.True(outputs.Count == 1, MarkdownToPdfCommand + " must plan one PDF output per Markdown input.");
                Assert.Equal(Path.GetFullPath(Path.Combine(tempDir, "notes.pdf")), Path.GetFullPath(outputs[0]));
            });
        });

        runner.Run("Markdown to PDF: Microsoft JhengHei TTC faces embed as standalone fonts", () =>
        {
            var resolver = new ClickraFontResolver();
            var regular = resolver.ResolveTypeface("Microsoft JhengHei", false, false);
            var bold = resolver.ResolveTypeface("Microsoft JhengHei", true, false);
            Assert.True(regular is not null && bold is not null,
                "Microsoft JhengHei regular and bold faces must resolve for Markdown output.");

            byte[] regularBytes = resolver.GetFont(regular!.FaceName) ?? Array.Empty<byte>();
            byte[] boldBytes = resolver.GetFont(bold!.FaceName) ?? Array.Empty<byte>();
            Assert.True(regularBytes.Length > 12 && boldBytes.Length > 12,
                "Resolved JhengHei faces must provide embeddable font payloads.");
            Assert.False(regularBytes.AsSpan(0, 4).SequenceEqual("ttcf"u8),
                "Regular JhengHei must be extracted from the TTC into a standalone sfnt face.");
            Assert.False(boldBytes.AsSpan(0, 4).SequenceEqual("ttcf"u8),
                "Bold JhengHei must be extracted from the TTC into a standalone sfnt face.");
            Assert.False(regularBytes.SequenceEqual(boldBytes),
                "Regular and bold Markdown CJK faces must remain distinct payloads.");
        });

        runner.Run("Markdown templates differ in layout as well as color", () =>
        {
            MarkdownDocumentTemplate modern = MarkdownTemplateCatalog.Default;
            MarkdownDocumentTemplate minimal = MarkdownTemplateCatalog.Minimal;
            MarkdownDocumentTemplate academic = MarkdownTemplateCatalog.Academic;

            Assert.False(Math.Abs(modern.Layout.MarginPoints - minimal.Layout.MarginPoints) < 0.01 && Math.Abs(minimal.Layout.MarginPoints - academic.Layout.MarginPoints) < 0.01,
                "Built-in Markdown templates must not share one page layout.");
            Assert.False(Math.Abs(modern.Typography.BodySizePoints - minimal.Typography.BodySizePoints) < 0.01 && Math.Abs(minimal.Typography.BodySizePoints - academic.Typography.BodySizePoints) < 0.01,
                "Built-in Markdown templates must not share one typography scale.");
            Assert.True(modern.Layout.DrawH2Bar && !minimal.Layout.DrawH2Bar && !academic.Layout.DrawH2Bar,
                "Only Clickra Default should use the branded H2 accent rule.");
            Assert.True(modern.Layout.FillTableHeader && !minimal.Layout.FillTableHeader && !academic.Layout.FillTableHeader,
                "Minimal and Academic tables must remain visually quieter than Clickra Default.");
            Assert.Equal("Times New Roman", academic.Typography.LatinFont);
            Assert.Equal("KaiU", academic.Typography.CjkFont);
            Assert.True(Math.Abs(academic.Typography.BodySizePoints - 12d) < 0.01 && Math.Abs(academic.Typography.LineHeightPoints - 18d) < 0.01,
                "Academic body typography must use conventional 12pt text with 18pt leading.");
            Assert.True(Math.Abs(academic.Layout.EffectiveMarginTopPoints - 70.87) < 0.01
                        && Math.Abs(academic.Layout.EffectiveMarginRightPoints - 56.69) < 0.01
                        && Math.Abs(academic.Layout.EffectiveMarginBottomPoints - 70.87) < 0.01
                        && Math.Abs(academic.Layout.EffectiveMarginLeftPoints - 85.04) < 0.01
                        && Math.Abs(academic.Layout.FirstLineIndentPoints - 24d) < 0.01,
                "Academic layout must use 2.5/2/2.5/3 cm thesis margins and a two-em first-line indent.");
            Assert.True(academic.Layout.CenterH1 && academic.Layout.JustifyBody,
                "Academic documents must center a single-line level-one title instead of reusing report-style heading alignment.");
            Assert.True(Math.Abs(academic.Layout.BlockGapPoints - 0d) < 0.01,
                "Academic paragraphs must rely on first-line indentation instead of large card-like gaps.");
            Assert.True(Math.Abs(academic.Typography.Headings.H1 - 18d) < 0.01
                        && Math.Abs(academic.Typography.Headings.H2 - 16d) < 0.01
                        && Math.Abs(academic.Typography.Headings.H3 - 14d) < 0.01
                        && Math.Abs(academic.Typography.Headings.H4 - 12d) < 0.01,
                "Academic heading sizes must follow a restrained thesis hierarchy.");
            Assert.Equal("000000", academic.Palette.Strong.Hex);
            Assert.Equal("000000", academic.Palette.Accent.Hex);
            Assert.True(academic.Layout.MarginPoints > modern.Layout.MarginPoints && modern.Layout.MarginPoints > minimal.Layout.MarginPoints,
                "Academic, Default, and Minimal should expose visibly different page densities.");
        });

        runner.Run("Markdown templates: KaiU academic CJK face embeds as a real font", () =>
        {
            var resolver = new ClickraFontResolver();
            var face = resolver.ResolveTypeface("KaiU", false, false);
            Assert.True(face is not null, "Academic KaiU must resolve for PDF output.");
            byte[] bytes = resolver.GetFont(face!.FaceName) ?? Array.Empty<byte>();
            Assert.True(bytes.Length > 12, "Academic KaiU must provide an embeddable font payload.");
            Assert.False(bytes.AsSpan(0, 4).SequenceEqual("ttcf"u8),
                "Academic KaiU must be provided as an embeddable standalone sfnt face.");
        });

        runner.Run("Markdown templates: custom JSON safely overrides a built-in base", () =>
            RunWithTempDirectory(tempDir =>
            {
                string templatePath = Path.Combine(tempDir, "custom-theme.json");
                File.WriteAllText(templatePath, """
                    {
                      "version": 1,
                      "name": "Course Handout",
                      "base": "academic",
                      "typography": {
                        "latinFont": "Segoe UI",
                        "bodySize": 12,
                        "headings": { "h1": 28, "h2": 19 }
                      },
                      "layout": {
                        "margin": 48,
                        "firstLineIndent": 18,
                        "centerH1": true,
                        "drawH2Bar": true,
                        "fillTableHeader": true
                      },
                      "palette": {
                        "body": "#334155",
                        "accent": "#0F766E"
                      }
                    }
                    """);

                MarkdownDocumentTemplate custom = MarkdownTemplateFile.Load(templatePath);
                Assert.Equal("custom:Course Handout", custom.Id);
                Assert.Equal("Segoe UI", custom.Typography.LatinFont);
                Assert.True(Math.Abs(custom.Typography.BodySizePoints - 12d) < 0.01, "Custom body size override must apply.");
                Assert.True(Math.Abs(custom.Typography.Headings.H1 - 28d) < 0.01, "Custom H1 override must apply.");
                Assert.True(Math.Abs(custom.Layout.MarginPoints - 48d) < 0.01, "Custom margin override must apply.");
                Assert.True(Math.Abs(custom.Layout.EffectiveMarginTopPoints - 48d) < 0.01
                            && Math.Abs(custom.Layout.EffectiveMarginRightPoints - 48d) < 0.01
                            && Math.Abs(custom.Layout.EffectiveMarginBottomPoints - 48d) < 0.01
                            && Math.Abs(custom.Layout.EffectiveMarginLeftPoints - 48d) < 0.01,
                    "Legacy uniform margin overrides must continue to override every Academic side margin.");
                Assert.True(Math.Abs(custom.Layout.FirstLineIndentPoints - 18d) < 0.01, "Custom first-line indent override must apply.");
                Assert.True(custom.Layout.CenterH1, "Custom H1 alignment override must apply.");
                Assert.True(custom.Layout.DrawH2Bar && custom.Layout.FillTableHeader,
                    "Custom layout overrides must apply on top of the selected base.");
                Assert.Equal("0F766E", custom.Palette.Accent.Hex);
                Assert.Equal(MarkdownTemplateCatalog.Academic.Palette.Strong.Hex, custom.Palette.Strong.Hex);

                var options = MarkdownPdfOptions.Create(templatePath: templatePath);
                string? storedTemplatePath = MarkdownPdfOptions.GetTemplatePath(options);
                Assert.True(storedTemplatePath is not null, "Custom template path must be retained in one-shot options.");
                Assert.Equal(Path.GetFullPath(templatePath), storedTemplatePath!);
                Assert.Equal(custom.Id, MarkdownTemplateCatalog.Resolve(MarkdownPdfOptions.GetTheme(options), MarkdownPdfOptions.GetTemplatePath(options)).Id);
            }));

        runner.Run("Markdown templates: side margins override the legacy uniform margin independently", () =>
            RunWithTempDirectory(tempDir =>
            {
                string templatePath = Path.Combine(tempDir, "mixed-margins.json");
                File.WriteAllText(templatePath, """
                    {
                      "version": 1,
                      "base": "academic",
                      "layout": { "margin": 48, "marginLeft": 72 }
                    }
                    """);

                MarkdownDocumentTemplate custom = MarkdownTemplateFile.Load(templatePath);
                Assert.True(Math.Abs(custom.Layout.EffectiveMarginTopPoints - 48d) < 0.01
                            && Math.Abs(custom.Layout.EffectiveMarginRightPoints - 48d) < 0.01
                            && Math.Abs(custom.Layout.EffectiveMarginBottomPoints - 48d) < 0.01,
                    "Unspecified sides must inherit the explicit uniform margin.");
                Assert.True(Math.Abs(custom.Layout.EffectiveMarginLeftPoints - 72d) < 0.01,
                    "An explicit side margin must override only that side.");
            }));

        runner.Run("Markdown templates: invalid custom JSON fails closed", () =>
            RunWithTempDirectory(tempDir =>
            {
                string unknownPath = Path.Combine(tempDir, "unknown.json");
                File.WriteAllText(unknownPath, "{\"version\":1,\"layout\":{\"margin\":48,\"mystery\":true}}");
                Assert.Throws<InvalidDataException>(() => MarkdownTemplateFile.Load(unknownPath));

                string colorPath = Path.Combine(tempDir, "bad-color.json");
                File.WriteAllText(colorPath, "{\"version\":1,\"palette\":{\"accent\":\"blue\"}}");
                Assert.Throws<InvalidDataException>(() => MarkdownTemplateFile.Load(colorPath));

                string marginPath = Path.Combine(tempDir, "bad-margin.json");
                File.WriteAllText(marginPath, "{\"version\":1,\"layout\":{\"margin\":500}}");
                Assert.Throws<InvalidDataException>(() => MarkdownTemplateFile.Load(marginPath));

                string duplicatePath = Path.Combine(tempDir, "duplicate.json");
                File.WriteAllText(duplicatePath, "{\"version\":1,\"layout\":{\"margin\":48,\"margin\":52}}");
                Assert.Throws<InvalidDataException>(() => MarkdownTemplateFile.Load(duplicatePath));

                string lineHeightPath = Path.Combine(tempDir, "bad-line-height.json");
                File.WriteAllText(lineHeightPath, "{\"version\":1,\"typography\":{\"bodySize\":36,\"lineHeight\":8}}");
                Assert.Throws<InvalidDataException>(() => MarkdownTemplateFile.Load(lineHeightPath));

                string unsupportedFontPath = Path.Combine(tempDir, "unsupported-font.json");
                File.WriteAllText(unsupportedFontPath, "{\"version\":1,\"typography\":{\"latinFont\":\"Aptos\"}}");
                Assert.Throws<InvalidDataException>(() => MarkdownTemplateFile.Load(unsupportedFontPath));
            }));

        runner.Run("Markdown templates: supported font aliases canonicalize for PDF and DOCX parity", () =>
            RunWithTempDirectory(tempDir =>
            {
                string templatePath = Path.Combine(tempDir, "font-case.json");
                File.WriteAllText(templatePath, "{\"version\":1,\"typography\":{\"latinFont\":\"calibri\",\"monospaceFont\":\"courier new\"}}");

                MarkdownDocumentTemplate template = MarkdownTemplateFile.Load(templatePath);
                Assert.Equal("Calibri", template.Typography.LatinFont);
                Assert.Equal("Courier New", template.Typography.MonospaceFont);
                Assert.True(ClickraFontResolver.TryGetCanonicalTemplateFamily("constantia", out string constantia),
                    "Constantia must be a supported canonical template family.");
                Assert.Equal("Constantia", constantia);
                var resolver = new ClickraFontResolver();
                Assert.True(resolver.GetFont("calibri") is { Length: > 0 }, "Calibri must resolve to real font bytes for PDF output.");
                Assert.True(resolver.GetFont("constantia") is { Length: > 0 }, "Constantia must resolve to real font bytes for PDF output.");
            }));

        runner.Run("Markdown to PDF: custom template changes rendered page geometry", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "custom-margin.md");
                string defaultOutput = Path.Combine(tempDir, "default-margin.pdf");
                string customOutput = Path.Combine(tempDir, "custom-margin.pdf");
                string templatePath = Path.Combine(tempDir, "wide-margin.json");
                File.WriteAllText(input, "# Geometry\n\nRendered body text.");
                File.WriteAllText(templatePath, "{\"version\":1,\"layout\":{\"margin\":96}}");

                FileProcessor.ConvertMarkdownToPdf(input, defaultOutput, MarkdownPdfOptions.Create());
                FileProcessor.ConvertMarkdownToPdf(input, customOutput, MarkdownPdfOptions.Create(templatePath: templatePath));

                using var defaultPdf = UglyToad.PdfPig.PdfDocument.Open(defaultOutput);
                using var customPdf = UglyToad.PdfPig.PdfDocument.Open(customOutput);
                double defaultLeft = defaultPdf.GetPage(1).Letters.Min(letter => letter.BoundingBox.Left);
                double customLeft = customPdf.GetPage(1).Letters.Min(letter => letter.BoundingBox.Left);
                Assert.True(customLeft > defaultLeft + 30,
                    $"A 96pt custom margin must shift rendered text right; default={defaultLeft:0.##}, custom={customLeft:0.##}.");
            }));

        runner.Run("Markdown to PDF: imported DOCX template controls PDF layout", () =>
            RunWithTempDirectory(tempDir =>
            {
                string output = ConvertMarkdownWithWordTemplate(
                    tempDir,
                    "thesis",
                    "# Thesis\n\nZebra body paragraph uses the Word template margins and paragraph formatting.");

                using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
                var page = pdf.GetPage(1);
                double bodyLeft = page.Letters.First(letter => letter.Value == "Z").BoundingBox.Left;
                Assert.True(bodyLeft > 112 && bodyLeft < 118,
                    $"Imported DOCX left margin plus first-line indent must reach PDF geometry; actual={bodyLeft:0.##}.");
            }));

        runner.Run("Markdown to PDF: mixed Latin and CJK text keeps imported font families", () =>
            RunWithTempDirectory(tempDir =>
            {
                string output = ConvertMarkdownWithWordTemplate(
                    tempDir,
                    "mixed-fonts",
                    "# Mixed fonts\n\nZebra 測試 text. Qello，vorld.");

                using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
                var page = pdf.GetPage(1);
                string latinFont = page.Letters.First(letter => letter.Value == "Z").FontName!;
                string[] embeddedFonts = GetMarkdownPdfFontNames(output);
                string attachedLatinBefore = page.Letters.First(letter => letter.Value == "Q").FontName!;
                string attachedLatinAfter = page.Letters.First(letter => letter.Value == "v").FontName!;
                Assert.True(latinFont.Contains(TimesFontToken, StringComparison.OrdinalIgnoreCase),
                    $"Latin text must use the imported Times New Roman family; actual={latinFont}.");
                Assert.True(attachedLatinBefore.Contains(TimesFontToken, StringComparison.OrdinalIgnoreCase)
                            && attachedLatinAfter.Contains(TimesFontToken, StringComparison.OrdinalIgnoreCase),
                    $"Latin text attached to full-width punctuation must stay in the imported Latin family; actual={attachedLatinBefore}, {attachedLatinAfter}.");
                Assert.True(embeddedFonts.Any(font => font.Contains(TimesFontToken, StringComparison.OrdinalIgnoreCase)),
                    "Mixed Latin/CJK text must retain a separate Times font resource.");
                Assert.True(embeddedFonts.Any(font => !font.Contains(TimesFontToken, StringComparison.OrdinalIgnoreCase)),
                    $"Mixed Latin/CJK text must embed a distinct CJK-capable font resource; fonts={string.Join(", ", embeddedFonts)}.");
                Assert.True(MarkdownRendererUsesCjkTypography('測') && MarkdownRendererUsesCjkTypography('，'),
                    "CJK ideographs and full-width punctuation must route through the renderer's CJK typography path.");
            }));

        runner.Run("Markdown to PDF: hyperlink annotations align with rendered link text", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "link.md");
                string output = Path.Combine(tempDir, "link.pdf");
                File.WriteAllText(input, "# Links\n\nOpen [Root-Me #101](https://www.root-me.org/) now.");

                FileProcessor.ConvertMarkdownToPdf(input, output);

                using var textPdf = UglyToad.PdfPig.PdfDocument.Open(output);
                var textPage = textPdf.GetPage(1);
                var linkWord = textPage.GetWords().FirstOrDefault(word => word.Text == "Root-Me");
                Assert.True(linkWord is not null, "The rendered hyperlink text must be present in the PDF text layer.");
                double textBottom = linkWord!.BoundingBox.Bottom;
                double textTop = linkWord.BoundingBox.Top;

                using var annotationPdf = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
                Assert.True(annotationPdf.Pages[0].Annotations.Count > 0, "Markdown links must create a PDF hyperlink annotation.");
                var rect = annotationPdf.Pages[0].Annotations[0].Rectangle;

                Assert.True(rect.Y1 <= textTop + 3 && rect.Y2 >= textBottom - 3,
                    $"The hyperlink rectangle must overlap the rendered link text vertically; text={textBottom:0.##}-{textTop:0.##}, rect={rect.Y1:0.##}-{rect.Y2:0.##}.");
                Assert.True(Math.Abs(((rect.Y1 + rect.Y2) / 2.0) - ((textBottom + textTop) / 2.0)) < 8,
                    $"The hyperlink rectangle must not be vertically mirrored away from its text; text={textBottom:0.##}-{textTop:0.##}, rect={rect.Y1:0.##}-{rect.Y2:0.##}.");
            }));

        runner.Run("Markdown to PDF: imported CJK punctuation does not render as null glyphs", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "cjk-punctuation.md");
                string output = Path.Combine(tempDir, "cjk-punctuation.pdf");
                string templatePath = Path.Combine(tempDir, "cjk-punctuation.docx");
                CreateWordTemplateFixture(templatePath);
                const string sentence = "Alpha\u3001Beta\u3002Gamma\uff1a\u300cDelta\u300d\u300eEcho\u300f\u3010Foxtrot\u3011";
                File.WriteAllText(input, "# Punctuation\n\n" + sentence);

                FileProcessor.ConvertMarkdownToPdf(input, output, MarkdownPdfOptions.Create(templatePath: templatePath));

                using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
                var page = pdf.GetPage(1);
                string text = page.Text;
                string[] fonts = GetMarkdownPdfFontNames(output);
                Assert.True(fonts.Any(font => !font.Contains(TimesFontToken, StringComparison.OrdinalIgnoreCase)),
                    $"CJK punctuation must select a font resource separate from imported Latin text; fonts={string.Join(", ", fonts)}.");
                foreach (string marker in new[] { "Alpha", "Beta", "Gamma", "Delta", "Echo", "Foxtrot" })
                    Assert.True(text.Contains(marker, StringComparison.Ordinal),
                        $"The PDF must retain the Latin text surrounding CJK punctuation: {marker}.");
                foreach (char punctuation in "\u3001\u3002\uff1a\u300c\u300d\u300e\u300f\u3010\u3011")
                    Assert.True(MarkdownRendererUsesCjkTypography(punctuation),
                        $"Full-width punctuation U+{(int)punctuation:X4} must route through the CJK typography path.");
            }));

        runner.Run("Markdown to PDF: Academic indents only the first body line", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "academic-indent.md");
                string output = Path.Combine(tempDir, "academic-indent.pdf");
                File.WriteAllText(input, "# Academic\n\nZebra begins the academic paragraph with enough repeated words to wrap onto another line while preserving the first-line indentation convention. " +
                    "More words ensure wrapping occurs on an A4 page at the configured academic body size and margin.");

                FileProcessor.ConvertMarkdownToPdf(input, output,
                    MarkdownPdfOptions.Create(MarkdownPdfOptions.ThemeAcademic));

                using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
                var page = pdf.GetPage(1);
                double paragraphLeft = page.Letters.First(letter => letter.Value == "Z").BoundingBox.Left;
                Assert.True(paragraphLeft > 106 && paragraphLeft < 112,
                    $"Academic body first line must begin near the 3 cm left thesis margin + 24pt indent; actual={paragraphLeft:0.##}.");
            }));

        runner.Run("Markdown to PDF: Academic fully justifies wrapped body lines", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "academic-justify.md");
                string output = Path.Combine(tempDir, "academic-justify.pdf");
                File.WriteAllText(input,
                    "# Academic\n\nZebra begins a deliberately long academic paragraph whose first physical line must be expanded across the available thesis measure while the final line remains naturally ragged. " +
                    "Additional carefully spaced words force several wraps so the geometry check observes a real non-final justified line rather than a short paragraph.");

                FileProcessor.ConvertMarkdownToPdf(input, output,
                    MarkdownPdfOptions.Create(MarkdownPdfOptions.ThemeAcademic));

                using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
                var page = pdf.GetPage(1);
                var zebra = page.Letters.First(letter => letter.Value == "Z");
                var firstBodyLine = page.Letters
                    .Where(letter => Math.Abs(letter.BoundingBox.Bottom - zebra.BoundingBox.Bottom) < 1.5)
                    .ToList();
                double rightmostGlyph = firstBodyLine.Max(letter => letter.BoundingBox.Right);
                double expectedRight = page.Width - MarkdownTemplateCatalog.Academic.Layout.EffectiveMarginRightPoints;
                Assert.True(rightmostGlyph > expectedRight - 8 && rightmostGlyph <= expectedRight + 2,
                    $"Academic wrapped body lines must visually reach the thesis right margin; rightmost={rightmostGlyph:0.##}, expected={expectedRight:0.##}.");
            }));

        runner.Run("Markdown to PDF: Academic CJK justification keeps punctuation attached", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "academic-cjk-justify.md");
                string output = Path.Combine(tempDir, "academic-cjk-justify.pdf");
                File.WriteAllText(input,
                    "# 學術排版\n\n中文測試Q，v標點應該貼近文字而不是被左右對齊拉開。中文測試，標點應該貼近文字而不是被左右對齊拉開。" +
                    "中文測試，標點應該貼近文字而不是被左右對齊拉開。中文測試，標點應該貼近文字而不是被左右對齊拉開。");

                FileProcessor.ConvertMarkdownToPdf(input, output,
                    MarkdownPdfOptions.Create(MarkdownPdfOptions.ThemeAcademic));

                using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
                var letters = pdf.GetPage(1).Letters.ToList();
                int leftIndex = letters.FindIndex(letter => letter.Value == "Q");
                int rightIndex = letters.FindIndex(letter => letter.Value == "v");
                Assert.True(leftIndex >= 0 && rightIndex > leftIndex,
                    "ASCII anchor glyphs surrounding the full-width comma must remain readable.");
                double anchorGap = letters[rightIndex].BoundingBox.Left - letters[leftIndex].BoundingBox.Right;
                Assert.True(anchorGap >= 0 && anchorGap < 35,
                    $"CJK justification must not expand the punctuation interval between adjacent anchors; gap={anchorGap:0.##}.");
                string[] fonts = GetMarkdownPdfFontNames(output);
                Assert.True(fonts.Any(font => !font.Contains(TimesFontToken, StringComparison.OrdinalIgnoreCase)),
                    $"Justified CJK content must retain a font resource separate from Latin text; fonts={string.Join(", ", fonts)}.");
                Assert.True(MarkdownRendererUsesCjkTypography('，'),
                    "Full-width comma must remain on the renderer's CJK typography path during justification.");
            }));

        runner.Run("Markdown to PDF: Academic does not indent blockquote text as body prose", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "academic-quote.md");
                string output = Path.Combine(tempDir, "academic-quote.pdf");
                File.WriteAllText(input, "# Academic\n\n> Quoted evidence should use the quote inset without the ordinary body first-line indent.");

                FileProcessor.ConvertMarkdownToPdf(input, output,
                    MarkdownPdfOptions.Create(MarkdownPdfOptions.ThemeAcademic));

                using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
                double quoteLeft = pdf.GetPage(1).Letters.First(letter => letter.Value == "Q").BoundingBox.Left;
                Assert.True(quoteLeft > 98 && quoteLeft < 105,
                    $"Academic quote text must use only the quote inset, not the 24pt body first-line indent; actual={quoteLeft:0.##}.");
            }));

        runner.Run("Markdown to PDF: Academic does not indent later list paragraphs as body prose", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "academic-list.md");
                string output = Path.Combine(tempDir, "academic-list.pdf");
                File.WriteAllText(input, "# Academic\n\n- First list paragraph.\n\n  Second list paragraph in the same item.");

                FileProcessor.ConvertMarkdownToPdf(input, output,
                    MarkdownPdfOptions.Create(MarkdownPdfOptions.ThemeAcademic));

                using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
                var page = pdf.GetPage(1);
                double firstLeft = page.Letters.First(letter => letter.Value == "F").BoundingBox.Left;
                double secondLeft = page.Letters.First(letter => letter.Value == "S").BoundingBox.Left;
                Assert.True(Math.Abs(firstLeft - secondLeft) < 5,
                    $"Academic list continuation paragraphs must keep list indentation without body first-line indent; first={firstLeft:0.##}, second={secondLeft:0.##}.");
            }));

        runner.Run("Markdown to PDF: one-shot options use safe defaults and affect page setup", () =>
            RunWithTempDirectory(tempDir =>
            {
                var defaults = MarkdownPdfOptions.Create(UnknownMarkdownOptionValue, UnknownMarkdownOptionValue, UnknownMarkdownOptionValue, UnknownMarkdownOptionValue);
                Assert.Equal(MarkdownPdfOptions.ThemeDefault, MarkdownPdfOptions.GetTheme(defaults));
                Assert.Equal(MarkdownPdfOptions.PaperA4, MarkdownPdfOptions.GetPaper(defaults));
                Assert.Equal(MarkdownPdfOptions.TextStandard, MarkdownPdfOptions.GetTextSize(defaults));
                Assert.Equal(MarkdownPdfOptions.CodeDark, MarkdownPdfOptions.GetCodeTheme(defaults));

                string input = Path.Combine(tempDir, "letter.md");
                string output = Path.Combine(tempDir, "letter.pdf");
                File.WriteAllText(input, "# Options\n\nA short document with `inline code`.");
                var options = MarkdownPdfOptions.Create(
                    MarkdownPdfOptions.ThemeMinimal,
                    MarkdownPdfOptions.PaperLetter,
                    MarkdownPdfOptions.TextLarge,
                    MarkdownPdfOptions.CodeLight);

                FileProcessor.ConvertMarkdownToPdf(input, output, options);

                using var pdf = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
                double width = pdf.Pages[0].Width.Point;
                double height = pdf.Pages[0].Height.Point;
                Assert.True(Math.Abs(width - 612) < 1 && Math.Abs(height - 792) < 1,
                    $"Letter paper must render as 612x792pt, got {width:0.##}x{height:0.##}.");
            }));

        runner.Run("Markdown to PDF: interactive surfaces prompt before conversion", () =>
        {
            string root = FindRepoRoot() ?? throw new TestSkippedException(RepoRootNotFoundMessage);
            string fluentMain = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "MainPage.xaml.cs"));
            string fluentTask = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "TaskProgressPage.xaml.cs"));
            string nativeDashboard = File.ReadAllText(Path.Combine(root, "src", MarkdownPdfCliDirectoryName, "Dashboard", "DashboardWindow.ConvertRegistry.cs"));
            string nativeProgress = File.ReadAllText(Path.Combine(root, "src", MarkdownPdfCliDirectoryName, "Progress", "ProgressWindow.cs"));
            string nativeOptions = File.ReadAllText(Path.Combine(root, "src", MarkdownPdfCliDirectoryName, "Progress", "ProgressWindow.MarkdownOptions.cs"));

            Assert.True(fluentMain.Contains("PromptMarkdownPdfOptionsAsync", StringComparison.Ordinal),
                "The Fluent conversion workspace must prompt for one-shot Markdown PDF options.");
            Assert.True(fluentTask.Contains("PromptMarkdownPdfOptionsAsync", StringComparison.Ordinal),
                "The Explorer/Fluent task window must prompt for one-shot Markdown PDF options.");
            Assert.True(nativeDashboard.Contains("ProgressWindow.Show(", StringComparison.Ordinal)
                        && nativeDashboard.Contains("markdownDecision:", StringComparison.Ordinal)
                        && !nativeDashboard.Contains("MarkdownOptionsPrompt.Show", StringComparison.Ordinal),
                "The NativeAOT dashboard must hand Markdown conversion and its start/cancel decision directly to the shared Clickra progress window.");
            Assert.True(nativeProgress.Contains("_isPromptingMarkdownOptions", StringComparison.Ordinal)
                        && nativeProgress.Contains("StartProcessingThread", StringComparison.Ordinal),
                "Interactive NativeAOT progress launches must render Markdown choices in the existing Clickra conversion window before processing starts.");
            string fluentDialogs = File.ReadAllText(Path.Combine(root, "src", "Clickra.Fluent", "Controls", "FluentDialogs.cs"));
            Assert.True(fluentDialogs.Contains("MarkdownTemplateSource.Load", StringComparison.Ordinal)
                        && fluentDialogs.Contains(".docx", StringComparison.Ordinal)
                        && !fluentDialogs.Contains("FileTypeFilter.Add(\".json\")", StringComparison.Ordinal),
                "Fluent Markdown options must expose DOCX as the normal custom-template import path.");
            Assert.True(fluentDialogs.Contains("md_options_layout_source", StringComparison.Ordinal)
                        && fluentDialogs.Contains("selectedTemplatePath = layoutSource", StringComparison.Ordinal),
                "Fluent Markdown PDF and Word options must expose templates only through an explicit mutually exclusive layout-source choice.");
            Assert.True(nativeOptions.Contains("MarkdownTemplateSource.Load", StringComparison.Ordinal)
                        && nativeOptions.Contains("*.docx", StringComparison.Ordinal)
                        && !nativeOptions.Contains("*.json", StringComparison.Ordinal),
                "NativeAOT Markdown options must expose DOCX rather than JSON to ordinary users.");
            Assert.True(nativeOptions.Contains("md_options_layout_source", StringComparison.Ordinal)
                        && nativeOptions.Contains("_markdownLayoutSourceIndex == 1 ? _markdownTemplatePath : null", StringComparison.Ordinal),
                "NativeAOT Markdown PDF and Word options must make DOCX templates mutually exclusive with built-in styles.");
            Assert.True(nativeOptions.Contains("PaintMarkdownOptions", StringComparison.Ordinal)
                        && nativeOptions.Contains("ResizeWindowForMarkdownOptions", StringComparison.Ordinal)
                        && !File.Exists(Path.Combine(root, "src", MarkdownPdfCliDirectoryName, "Progress", "MarkdownOptionsPrompt.cs")),
                "NativeAOT Markdown options must reuse the custom-painted Clickra progress surface rather than a separate stock Win32 prompt.");
        });

        runner.Run("Markdown to PDF: common Markdown structures render to a readable PDF", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "document.md");
                string output = Path.Combine(tempDir, "document.pdf");
                string markdown = """
                    # Clickra Markdown

                    A paragraph with **bold**, *italic*, `inline code`, and a [link](https://example.com).

                    > A quoted paragraph.

                    - First item
                    - Second item

                    1. Ordered one
                    2. Ordered two

                    | Name | Value |
                    | --- | --- |
                    | Alpha | 1 |
                    | Beta | 2 |

                    ```csharp
                    Console.WriteLine("hello");
                    ```
                    """;
                File.WriteAllText(input, markdown);
                byte[] original = File.ReadAllBytes(input);

                FileProcessor.ConvertMarkdownToPdf(input, output);

                Assert.True(File.Exists(output), "Markdown conversion must create the PDF output.");
                Assert.True(File.ReadAllBytes(output).Take(4).SequenceEqual("%PDF"u8.ToArray()), "Markdown output must have a PDF header.");
                Assert.True(File.ReadAllBytes(input).SequenceEqual(original), "Markdown conversion must not modify the source file.");
                using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
                Assert.True(pdf.NumberOfPages >= 1, "Converted Markdown PDF must contain at least one page.");
                string text = string.Join("\n", pdf.GetPages().Select(page => page.Text));
                Assert.True(text.Contains("Clickra Markdown", StringComparison.Ordinal), "Heading text must survive Markdown rendering.");
                Assert.True(text.Contains("First item", StringComparison.Ordinal), "List text must survive Markdown rendering.");
                Assert.True(text.Contains("Alpha", StringComparison.Ordinal), "Table text must survive Markdown rendering.");
                Assert.True(text.Contains("Console.WriteLine", StringComparison.Ordinal), "Code block text must survive Markdown rendering.");
            }));

        runner.Run("Markdown to PDF: long documents paginate instead of clipping", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "long.md");
                string output = Path.Combine(tempDir, "long.pdf");
                string body = string.Join("\n\n", Enumerable.Range(1, 120).Select(i =>
                    $"Paragraph {i}: This is enough text to verify that Markdown rendering creates additional PDF pages rather than drawing beyond the A4 page boundary."));
                File.WriteAllText(input, "# Long document\n\n" + body);

                FileProcessor.ConvertMarkdownToPdf(input, output);

                using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
                Assert.True(pdf.NumberOfPages > 1, "Long Markdown documents must paginate across multiple PDF pages.");
                Assert.True(pdf.GetPages().Last().Text.Contains("Paragraph 120", StringComparison.Ordinal),
                    "The last Markdown paragraph must remain visible after pagination.");
            }));

        runner.Run("Markdown to PDF: section headings stay with following content", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "sections.md");
                string output = Path.Combine(tempDir, "sections.pdf");
                string lead = string.Join("\n\n", Enumerable.Range(1, 24).Select(i =>
                    $"Lead paragraph {i}: enough body text to place the next section close to a page boundary while preserving normal wrapping behavior."));
                File.WriteAllText(input,
                    "# Pagination test\n\n" + lead + "\n\n## NEXT_SECTION_HEADING\n\nSECTION_BODY_MARKER should travel with its heading.\n");

                FileProcessor.ConvertMarkdownToPdf(input, output);

                using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
                var pages = pdf.GetPages().ToList();
                int headingPage = pages.FindIndex(page => page.Text.Contains("NEXT_SECTION_HEADING", StringComparison.Ordinal));
                int bodyPage = pages.FindIndex(page => page.Text.Contains("SECTION_BODY_MARKER", StringComparison.Ordinal));
                Assert.True(headingPage >= 0 && headingPage == bodyPage,
                    "A section heading must not be stranded on a different page from its first body paragraph.");
            }));

        runner.Run("Markdown to PDF: relative local images render inline without network access", () =>
            RunWithTempDirectory(tempDir =>
            {
                string imagePath = Path.Combine(tempDir, "diagram.png");
                using (var bitmap = new Bitmap(80, 40))
                {
                    using Graphics graphics = Graphics.FromImage(bitmap);
                    graphics.Clear(Color.CornflowerBlue);
                    bitmap.Save(imagePath, ImageFormat.Png);
                }

                string input = Path.Combine(tempDir, "image.md");
                string output = Path.Combine(tempDir, "image.pdf");
                File.WriteAllText(input, "# Local image\n\nBefore image ![Diagram](diagram.png) after image.");

                FileProcessor.ConvertMarkdownToPdf(input, output);

                using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
                Assert.True(pdf.NumberOfPages >= 1, "Markdown with a local image must produce a readable PDF.");
                var page = pdf.GetPages().Single();
                Assert.True(page.GetImages().Any(), "A relative Markdown image embedded inside prose must remain an image in the PDF.");
                Assert.True(page.Text.Contains("Before image", StringComparison.Ordinal)
                            && page.Text.Contains("after image", StringComparison.Ordinal),
                    "Rendering an inline relative image must preserve the surrounding Markdown text.");
            }));

        runner.Run("Markdown to PDF: oversized table rows paginate without clipping", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "table.md");
                string output = Path.Combine(tempDir, "table.pdf");
                string longCell = string.Join(' ', Enumerable.Range(1, 700).Select(i => "cell" + i));
                File.WriteAllText(input,
                    "| Column | Value |\n| --- | --- |\n| Long | " + longCell + " TABLE_END_MARKER |\n\nAfter table.");

                FileProcessor.ConvertMarkdownToPdf(input, output);

                using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
                Assert.True(pdf.NumberOfPages > 1, "An oversized Markdown table row must split across pages.");
                string text = string.Join("\n", pdf.GetPages().Select(page => page.Text));
                Assert.True(text.Contains("TABLE_END_MARKER", StringComparison.Ordinal),
                    "The end of an oversized table row must not be clipped past the page boundary.");
                Assert.True(text.Contains("After table", StringComparison.Ordinal),
                    "Content after an oversized Markdown table must still render.");
            }));

        runner.Run("Markdown to PDF: CJK table and code text use Unicode-capable fonts", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "unicode.md");
                string output = Path.Combine(tempDir, "unicode.pdf");
                File.WriteAllText(input, """
                    | 類型 | 內容 |
                    | --- | --- |
                    | 測試 | 中文表格內容：完整標點 |

                    ```text
                    中文程式碼內容：File ➔ Export Objects
                    ```
                    """);

                FileProcessor.ConvertMarkdownToPdf(input, output);

                using var pdf = UglyToad.PdfPig.PdfDocument.Open(output);
                string text = string.Join("\n", pdf.GetPages().Select(page => page.Text));
                Assert.True(text.Contains("中文表格內容", StringComparison.Ordinal),
                    "CJK Markdown table text must remain extractable from the PDF.");
                Assert.True(text.Contains("中文程式碼內容", StringComparison.Ordinal),
                    "CJK fenced-code text must remain extractable from the PDF.");
                Assert.True(text.Contains("中文表格內容：完整標點", StringComparison.Ordinal),
                    "Full-width CJK punctuation must remain visible without rewriting the Markdown source text.");
                Assert.False(text.Contains("→", StringComparison.Ordinal),
                    "Technical arrow glyphs must not be rewritten to a different source character.");
            }));
    }
}

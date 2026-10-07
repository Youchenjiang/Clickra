using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Clickra.Core;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string MarkdownToPdfCommand = "md2pdf";

    public static void RegisterMarkdownToPdfTests(TestRunner runner)
    {
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

            Assert.False(modern.Layout.MarginPoints == minimal.Layout.MarginPoints && minimal.Layout.MarginPoints == academic.Layout.MarginPoints,
                "Built-in Markdown templates must not share one page layout.");
            Assert.False(modern.Typography.BodySizePoints == minimal.Typography.BodySizePoints && minimal.Typography.BodySizePoints == academic.Typography.BodySizePoints,
                "Built-in Markdown templates must not share one typography scale.");
            Assert.True(modern.Layout.DrawH2Bar && !minimal.Layout.DrawH2Bar && !academic.Layout.DrawH2Bar,
                "Only Clickra Default should use the branded H2 accent rule.");
            Assert.True(modern.Layout.FillTableHeader && !minimal.Layout.FillTableHeader && !academic.Layout.FillTableHeader,
                "Minimal and Academic tables must remain visually quieter than Clickra Default.");
            Assert.Equal("Times New Roman", academic.Typography.LatinFont);
            Assert.True(academic.Layout.MarginPoints > modern.Layout.MarginPoints && modern.Layout.MarginPoints > minimal.Layout.MarginPoints,
                "Academic, Default, and Minimal should expose visibly different page densities.");
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
                Assert.True(custom.Typography.BodySizePoints == 12d, "Custom body size override must apply.");
                Assert.True(custom.Typography.Headings.H1 == 28d, "Custom H1 override must apply.");
                Assert.True(custom.Layout.MarginPoints == 48d, "Custom margin override must apply.");
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
                File.WriteAllText(unsupportedFontPath, "{\"version\":1,\"typography\":{\"latinFont\":\"Calibri\"}}");
                Assert.Throws<InvalidDataException>(() => MarkdownTemplateFile.Load(unsupportedFontPath));
            }));

        runner.Run("Markdown templates: supported font aliases canonicalize for PDF and DOCX parity", () =>
            RunWithTempDirectory(tempDir =>
            {
                string templatePath = Path.Combine(tempDir, "font-case.json");
                File.WriteAllText(templatePath, "{\"version\":1,\"typography\":{\"latinFont\":\"segoe ui\",\"monospaceFont\":\"courier new\"}}");

                MarkdownDocumentTemplate template = MarkdownTemplateFile.Load(templatePath);
                Assert.Equal("Segoe UI", template.Typography.LatinFont);
                Assert.Equal("Courier New", template.Typography.MonospaceFont);
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

        runner.Run("Markdown to PDF: one-shot options use safe defaults and affect page setup", () =>
            RunWithTempDirectory(tempDir =>
            {
                var defaults = MarkdownPdfOptions.Create("unknown", "unknown", "unknown", "unknown");
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
            string nativeDashboard = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Dashboard", "DashboardWindow.ConvertRegistry.cs"));
            string nativeProgress = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Progress", "ProgressWindow.cs"));

            Assert.True(fluentMain.Contains("PromptMarkdownPdfOptionsAsync", StringComparison.Ordinal),
                "The Fluent conversion workspace must prompt for one-shot Markdown PDF options.");
            Assert.True(fluentTask.Contains("PromptMarkdownPdfOptionsAsync", StringComparison.Ordinal),
                "The Explorer/Fluent task window must prompt for one-shot Markdown PDF options.");
            int nativePrompt = nativeDashboard.IndexOf("MarkdownOptionsPrompt.Show()", StringComparison.Ordinal);
            int nativeClear = nativePrompt >= 0
                ? nativeDashboard.IndexOf("_selectedFiles.Clear()", nativePrompt, StringComparison.Ordinal)
                : -1;
            Assert.True(nativePrompt >= 0 && nativeClear > nativePrompt,
                "The NativeAOT dashboard must resolve Markdown options before clearing the user's selection.");
            Assert.True(nativeProgress.Contains("MarkdownOptionsPrompt.Show()", StringComparison.Ordinal),
                "Interactive NativeAOT progress launches must prompt when Markdown options were not preselected.");
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
                Assert.True(text.Contains("中文表格內容: 完整標點", StringComparison.Ordinal),
                    "Full-width CJK punctuation must normalize to a visible supported glyph instead of degrading to a missing glyph.");
                Assert.True(text.Contains("→", StringComparison.Ordinal),
                    "Common technical arrow glyphs must render as a supported arrow instead of degrading to a missing glyph.");
            }));
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Clickra.Core;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string MarkdownToWordCommand = "md2word";

    public static void RegisterMarkdownToWordTests(TestRunner runner)
    {
        runner.Run("Markdown to Word: pre-cancel preserves existing output", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "cancel.md");
                string output = Path.Combine(tempDir, "existing.docx");
                File.WriteAllText(input, "# Must not convert");
                byte[] original = { 1, 2, 3, 4, 5 };
                File.WriteAllBytes(output, original);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    new MarkdownToWordProcessor().Process(new List<string> { input }, output,
                        cancellationToken: cancellation.Token));
                Assert.True(File.ReadAllBytes(output).SequenceEqual(original),
                    "Cancellation before processing must preserve the existing Word output exactly.");
                Assert.False(Directory.GetFiles(tempDir, "*.tmp-*").Any(),
                    "Pre-cancel must leave no temporary Word packages.");
            }));

        runner.Run("Markdown to Word: invalid local image preserves existing output", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "broken-image.md");
                string output = Path.Combine(tempDir, "existing.docx");
                File.WriteAllText(input, "# Image\n\n![invalid](broken.png)");
                File.WriteAllText(Path.Combine(tempDir, "broken.png"), "not an image");
                byte[] original = { 6, 7, 8, 9 };
                File.WriteAllBytes(output, original);
                bool failed = false;
                try { FileProcessor.ConvertMarkdownToWord(input, output, MarkdownPdfOptions.Create()); }
                catch (ArgumentException) { failed = true; }
                catch (OutOfMemoryException) { failed = true; }
                Assert.True(failed, "Corrupt local image bytes must fail before replacing the document.");
                Assert.True(File.ReadAllBytes(output).SequenceEqual(original),
                    "An image rendering failure must preserve existing Word bytes.");
                Assert.False(Directory.GetFiles(tempDir, "*.tmp-*").Any(),
                    "Image failures must not leave partial Word packages.");
            }));

        runner.Run("Markdown templates: DOCX import rejects missing parts and package size limit", () =>
            RunWithTempDirectory(tempDir =>
            {
                string missingStyles = Path.Combine(tempDir, "missing-styles.docx");
                using (ZipArchive archive = ZipFile.Open(missingStyles, ZipArchiveMode.Create))
                {
                    ZipArchiveEntry entry = archive.CreateEntry("word/document.xml");
                    using var writer = new StreamWriter(entry.Open());
                    writer.Write("<document />");
                }
                Assert.Throws<InvalidDataException>(() => MarkdownDocxTemplateFile.Load(missingStyles));

                string oversized = Path.Combine(tempDir, "oversized.docx");
                using (FileStream stream = File.Create(oversized))
                    stream.SetLength(32L * 1024 * 1024 + 1);
                Assert.Throws<InvalidDataException>(() => MarkdownDocxTemplateFile.Load(oversized));
            }));

        runner.Run("Markdown to Word: registry and runner expose DOCX conversion", () =>
            RunWithTempDirectory(tempDir =>
            {
                Assert.True(ConvertCommandRegistry.IsKnownCommand(MarkdownToWordCommand),
                    "md2word must be a registered conversion command.");
                string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(MarkdownToWordCommand);
                Assert.True(allowed.Contains(".md", StringComparer.OrdinalIgnoreCase), "md2word must accept .md files.");
                Assert.True(allowed.Contains(".markdown", StringComparer.OrdinalIgnoreCase), "md2word must accept .markdown files.");
                Assert.True(ConvertCommandRegistry.GetCommandsForType("markdown").Contains(MarkdownToWordCommand, StringComparer.Ordinal),
                    "Markdown command discovery must include md2word.");

                string input = Path.Combine(tempDir, "runner.md");
                string output = Path.Combine(tempDir, "runner.docx");
                File.WriteAllText(input, "# Runner\n\nDOCX dispatch works.");
                var outputs = new List<string> { output };
                var options = new ConvertCommandRunner.ConversionOptions(
                    _ => System.Threading.Tasks.Task.FromResult<string?>(null),
                    (_, _) => System.Threading.Tasks.Task.FromResult<string?>(null),
                    CommandOptions: MarkdownPdfOptions.Create(MarkdownPdfOptions.ThemeMinimal));

                ConvertCommandRunner.Run(MarkdownToWordCommand, new List<string> { input }, outputs, (_, _, _) => { }, options);

                Assert.True(File.Exists(output), "The shared command runner must produce the planned DOCX output.");
            }));

        runner.Run("Markdown to Word: common structures produce a valid DOCX package", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "guide.md");
                string output = Path.Combine(tempDir, "guide.docx");
                File.WriteAllText(input, """
                    # 文件標題

                    正文包含 **粗體**、*斜體*、`inline code` 與 [連結](https://example.com)。

                    > 引用內容

                    - 項目一
                    - 項目二

                    | 較長欄位名稱 | 值 |
                    | --- | --- |
                    | 中文 | 測試 |

                    ```text
                    code block
                    ```
                    """);

                FileProcessor.ConvertMarkdownToWord(input, output, MarkdownPdfOptions.Create());

                Assert.True(File.Exists(output) && new FileInfo(output).Length > 0, "DOCX output must be created.");
                using ZipArchive archive = ZipFile.OpenRead(output);
                string[] required = { "[Content_Types].xml", "_rels/.rels", "word/document.xml", "word/styles.xml" };
                foreach (string entry in required)
                    Assert.True(archive.GetEntry(entry) is not null, $"DOCX must contain {entry}.");

                XDocument document = ReadXml(archive, "word/document.xml");
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                string text = string.Concat(document.Descendants(w + "t").Select(e => e.Value));
                Assert.True(text.Contains("文件標題", StringComparison.Ordinal), "Heading text must survive DOCX conversion.");
                Assert.True(text.Contains("引用內容", StringComparison.Ordinal), "Quote text must survive DOCX conversion.");
                Assert.True(text.Contains("code block", StringComparison.Ordinal), "Code block text must survive DOCX conversion.");
                Assert.True(document.Descendants(w + "tbl").Any(), "Markdown tables must become Word tables.");
                XElement table = document.Descendants(w + "tbl")
                    .Single(tbl => tbl.Element(w + "tblPr")?.Element(w + "tblStyle")?.Attribute(w + "val")?.Value == "TableGrid");
                Assert.Equal("fixed", table.Element(w + "tblPr")?.Element(w + "tblLayout")?.Attribute(w + "type")?.Value ?? "");
                Assert.True(table.Element(w + "tblGrid")?.Elements(w + "gridCol").Select(c => c.Attribute(w + "w")?.Value).Distinct().Count() > 1,
                    "Word tables must retain the shared intrinsic-content column proportions instead of forcing equal widths.");
                Assert.True(archive.GetEntry("word/numbering.xml") is not null,
                    "Markdown lists must create a Word numbering part instead of embedding bullet characters in paragraph text.");
                Assert.True(document.Descendants(w + "numPr").Any(),
                    "Markdown list paragraphs must use Word numbering properties.");

                XElement codeParagraph = document.Descendants(w + "p")
                    .Single(p => p.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val")?.Value == "CodeBlock");
                Assert.Equal("0F172A", codeParagraph.Element(w + "pPr")?.Element(w + "shd")?.Attribute(w + "fill")?.Value ?? "");
                Assert.True(codeParagraph.Descendants(w + "r")
                    .Where(run => run.Descendants(w + "t").Any())
                    .All(run => run.Element(w + "rPr")?.Element(w + "shd") is null),
                    "Fenced code blocks must use only paragraph shading; run shading creates white bars over the dark block.");
                Assert.True(codeParagraph.Descendants(w + "r")
                    .Where(run => run.Descendants(w + "t").Any())
                    .All(run => run.Element(w + "rPr")?.Element(w + "color")?.Attribute(w + "val")?.Value == "F1F5F9"),
                    "Fenced code block runs must remain light text over the dark paragraph background.");
                XElement codeBorders = codeParagraph.Element(w + "pPr")!.Element(w + "pBdr")!;
                Assert.Equal("11", codeBorders.Element(w + "left")?.Attribute(w + "space")?.Value ?? "");
                Assert.Equal("11", codeBorders.Element(w + "right")?.Attribute(w + "space")?.Value ?? "");
                Assert.Equal("5", codeBorders.Element(w + "top")?.Attribute(w + "space")?.Value ?? "");
                Assert.Equal("5", codeBorders.Element(w + "bottom")?.Attribute(w + "space")?.Value ?? "");

                ZipArchiveEntry? relEntry = archive.GetEntry("word/_rels/document.xml.rels");
                Assert.True(relEntry is not null, "A hyperlink must create document relationships.");
                string relationships = ReadAllText(relEntry!);
                Assert.True(relationships.Contains("https://example.com", StringComparison.Ordinal),
                    "External Markdown links must remain hyperlinks in DOCX.");
            }));

        runner.Run("Markdown to Word: local images are packaged without network access", () =>
            RunWithTempDirectory(tempDir =>
            {
                string imagePath = Path.Combine(tempDir, "pixel.png");
                File.WriteAllBytes(imagePath, Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
                string input = Path.Combine(tempDir, "image.md");
                string output = Path.Combine(tempDir, "image.docx");
                File.WriteAllText(input, "# Image\n\n![pixel](pixel.png)\n\n![remote](https://example.com/remote.png)");

                FileProcessor.ConvertMarkdownToWord(input, output, MarkdownPdfOptions.Create());

                using ZipArchive archive = ZipFile.OpenRead(output);
                Assert.True(archive.GetEntry("word/media/image1.png") is not null,
                    "A relative local Markdown image must be embedded in the DOCX package.");
                string relationships = ReadAllText(archive.GetEntry("word/_rels/document.xml.rels")!);
                Assert.True(relationships.Contains("relationships/image", StringComparison.Ordinal),
                    "An embedded local image must have a Word image relationship.");
                Assert.False(relationships.Contains("remote.png", StringComparison.Ordinal),
                    "Remote Markdown images must not be fetched or embedded by the offline converter.");
            }));

        runner.Run("Markdown to Word: shared templates change Word page and typography styles", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "template.md");
                File.WriteAllText(input, "# Heading\n\nBody text.\n\nSecond body paragraph.");

                string minimalPath = Path.Combine(tempDir, "minimal.docx");
                string academicPath = Path.Combine(tempDir, "academic.docx");
                FileProcessor.ConvertMarkdownToWord(input, minimalPath,
                    MarkdownPdfOptions.Create(MarkdownPdfOptions.ThemeMinimal));
                FileProcessor.ConvertMarkdownToWord(input, academicPath,
                    MarkdownPdfOptions.Create(MarkdownPdfOptions.ThemeAcademic));

                using ZipArchive minimal = ZipFile.OpenRead(minimalPath);
                using ZipArchive academic = ZipFile.OpenRead(academicPath);
                string minimalStyles = ReadAllText(minimal.GetEntry("word/styles.xml")!);
                string academicStyles = ReadAllText(academic.GetEntry("word/styles.xml")!);
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                Assert.True(minimalStyles.Contains("Segoe UI", StringComparison.Ordinal),
                    "Minimal DOCX must use its configured Latin font.");
                Assert.True(academicStyles.Contains("Times New Roman", StringComparison.Ordinal),
                    "Academic DOCX must use its configured Latin font.");
                Assert.True(academicStyles.Contains("KaiU", StringComparison.Ordinal),
                    "Academic DOCX must use the conventional Traditional Chinese KaiU family rather than JhengHei.");
                XDocument academicStylesXml = XDocument.Parse(academicStyles);
                XElement heading1Style = academicStylesXml.Descendants(w + "style")
                    .Single(style => style.Attribute(w + "styleId")?.Value == "Heading1");
                Assert.Equal("center", heading1Style.Descendants(w + "jc").Single().Attribute(w + "val")!.Value);
                XElement heading1Spacing = heading1Style.Descendants(w + "spacing").Single();
                Assert.Equal("486", heading1Spacing.Attribute(w + "line")?.Value ?? "");
                Assert.Equal("exact", heading1Spacing.Attribute(w + "lineRule")?.Value ?? "");
                Assert.Equal("240", heading1Spacing.Attribute(w + "after")?.Value ?? "");

                XDocument minimalDocument = ReadXml(minimal, "word/document.xml");
                XDocument academicDocument = ReadXml(academic, "word/document.xml");
                XElement academicHeading = academicDocument.Descendants(w + "p")
                    .Single(p => p.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val")?.Value == "Heading1");
                Assert.True(academicHeading.Descendants(w + "rPr").All(rPr => rPr.Element(w + "sz") is null),
                    "Heading runs must inherit Heading style size instead of overriding it with body text size.");
                XElement academicMargins = academicDocument.Descendants(w + "pgMar").Single();
                string minimalMargin = minimalDocument.Descendants(w + "pgMar").Single().Attribute(w + "left")!.Value;
                string academicMargin = academicMargins.Attribute(w + "left")!.Value;
                Assert.False(minimalMargin == academicMargin,
                    "Minimal and Academic DOCX templates must use different page margins.");
                Assert.Equal("1701", academicMargins.Attribute(w + "left")!.Value);
                Assert.Equal("1134", academicMargins.Attribute(w + "right")!.Value);
                Assert.Equal("1417", academicMargins.Attribute(w + "top")!.Value);
                Assert.Equal("1417", academicMargins.Attribute(w + "bottom")!.Value);
                var bodyParagraphs = academicDocument.Descendants(w + "p")
                    .Where(p => p.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val")?.Value == "Normal")
                    .ToList();
                var bodyIndents = bodyParagraphs
                    .Select(p => p.Element(w + "pPr")?.Element(w + "ind")?.Attribute(w + "firstLine")?.Value)
                    .Where(value => value is not null)
                    .ToList();
                Assert.True(bodyIndents.Count >= 2 && bodyIndents.All(value => value == "480"),
                    "Academic DOCX body paragraphs must use a 24pt first-line indent without applying it to every Normal-style container.");
                Assert.True(bodyParagraphs.Count >= 2
                            && bodyParagraphs.All(p => p.Element(w + "pPr")?.Element(w + "jc")?.Attribute(w + "val")?.Value == "both"),
                    "Academic DOCX body paragraphs must be fully justified.");
            }));

        runner.Run("Markdown PDF and Word share resolved print typography", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "parity.md");
                string pdfPath = Path.Combine(tempDir, "parity.pdf");
                string wordPath = Path.Combine(tempDir, "parity.docx");
                File.WriteAllText(input, """
                    # ZHEADING

                    ZBODY paragraph with `ZINLINE`.

                    | Column |
                    | --- |
                    | ZTABLE `ZTABLECODE` |

                    ```text
                    ZCODE
                    ```
                    """);

                FileProcessor.ConvertMarkdownToPdf(input, pdfPath);
                FileProcessor.ConvertMarkdownToWord(input, wordPath);

                MarkdownResolvedLayout layout = MarkdownResolvedLayout.Create(MarkdownTemplateCatalog.Default, 1);
                using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
                var page = pdf.GetPage(1);
                double PdfWordSize(string value) => page.GetWords()
                    .Single(word => word.Text == value)
                    .Letters.Average(letter => letter.PointSize);

                Assert.True(Math.Abs(PdfWordSize("ZHEADING") - layout.HeadingSizePoints(MarkdownTemplateCatalog.Default, 1)) < 0.25,
                    "PDF heading typography must consume the shared resolved layout.");
                Assert.True(Math.Abs(PdfWordSize("ZBODY") - layout.BodySizePoints) < 0.25,
                    "PDF body typography must consume the shared resolved layout.");
                Assert.True(Math.Abs(PdfWordSize("ZTABLE") - layout.TableFontSizePoints) < 0.25,
                    "PDF table typography must consume the shared resolved layout.");
                Assert.True(Math.Abs(PdfWordSize("ZCODE") - layout.CodeFontSizePoints) < 0.25,
                    "PDF code typography must consume the shared resolved layout.");
                Assert.True(Math.Abs(PdfWordSize("ZINLINE") - layout.CodeFontSizePoints) < 0.25,
                    "PDF inline code typography must match the shared code size used by DOCX runs.");
                Assert.True(Math.Abs(PdfWordSize("ZTABLECODE") - layout.CodeFontSizePoints) < 0.25,
                    "PDF table inline code typography must match the shared code size used by DOCX runs.");

                using ZipArchive archive = ZipFile.OpenRead(wordPath);
                XDocument styles = ReadXml(archive, "word/styles.xml");
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                double WordStyleSize(string styleId)
                {
                    XElement style = styles.Descendants(w + "style")
                        .Single(element => element.Attribute(w + "styleId")?.Value == styleId);
                    return double.Parse(style.Element(w + "rPr")!.Element(w + "sz")!.Attribute(w + "val")!.Value) / 2.0;
                }

                Assert.True(Math.Abs(WordStyleSize("Heading1") - layout.HeadingSizePoints(MarkdownTemplateCatalog.Default, 1)) < 0.01,
                    "DOCX Heading1 typography must consume the shared resolved layout.");
                Assert.True(Math.Abs(WordStyleSize("Normal") - layout.BodySizePoints) < 0.01,
                    "DOCX Normal typography must consume the shared resolved layout.");
                Assert.True(Math.Abs(WordStyleSize("TableText") - layout.TableFontSizePoints) <= 0.25,
                    "DOCX table typography must stay within Word's half-point quantization of the shared resolved layout.");
                Assert.True(Math.Abs(WordStyleSize("CodeBlock") - layout.CodeFontSizePoints) <= 0.25,
                    "DOCX code typography must stay within Word's half-point quantization of the shared resolved layout.");

                XDocument document = ReadXml(archive, "word/document.xml");
                foreach (string marker in new[] { "ZINLINE", "ZTABLECODE" })
                {
                    XElement run = document.Descendants(w + "r")
                        .Single(element => element.Element(w + "t")?.Value == marker);
                    double runSize = double.Parse(run.Element(w + "rPr")!.Element(w + "sz")!.Attribute(w + "val")!.Value) / 2.0;
                    Assert.True(Math.Abs(runSize - layout.CodeFontSizePoints) <= 0.25,
                        $"DOCX inline code run {marker} must stay within Word's half-point quantization of the shared PDF code size.");
                }
            }));

        runner.Run("Markdown to Word: imported template changes DOCX styles and layout", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "custom.md");
                string output = Path.Combine(tempDir, "custom.docx");
                string templatePath = Path.Combine(tempDir, "custom-template.json");
                File.WriteAllText(input, "# Heading\n\n## Accent heading\n\nBody text.");
                File.WriteAllText(templatePath, """
                    {
                      "version": 1,
                      "base": "academic",
                      "typography": { "latinFont": "Segoe UI", "bodySize": 12, "lineHeight": 18 },
                      "layout": { "margin": 48, "drawH2Bar": true },
                      "palette": { "accent": "#0F766E" }
                    }
                    """);

                FileProcessor.ConvertMarkdownToWord(input, output, MarkdownPdfOptions.Create(templatePath: templatePath));

                using ZipArchive archive = ZipFile.OpenRead(output);
                string styles = ReadAllText(archive.GetEntry("word/styles.xml")!);
                Assert.True(styles.Contains("Segoe UI", StringComparison.Ordinal),
                    "Imported typography must reach DOCX styles instead of the Academic base font.");
                Assert.True(styles.Contains("0F766E", StringComparison.OrdinalIgnoreCase),
                    "Imported accent color must reach DOCX styles.");

                XDocument document = ReadXml(archive, "word/document.xml");
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                XElement margins = document.Descendants(w + "pgMar").Single();
                Assert.True(new[] { "top", "right", "bottom", "left" }.All(name => margins.Attribute(w + name)?.Value == "960"),
                    "A legacy uniform custom margin must still set all four DOCX margins.");
            }));

        runner.Run("Markdown templates: DOCX import extracts inherited Word styles and section layout", () =>
            RunWithTempDirectory(tempDir =>
            {
                string templatePath = Path.Combine(tempDir, "university-thesis.docx");
                CreateWordTemplateFixture(templatePath);

                MarkdownDocumentTemplate template = MarkdownTemplateSource.Load(templatePath);
                Assert.True(template.Id.StartsWith("docx:", StringComparison.Ordinal),
                    "A Word template must resolve through the DOCX template source.");
                Assert.Equal("Times New Roman", template.Typography.LatinFont);
                Assert.Equal("KaiU", template.Typography.CjkFont);
                Assert.True(Math.Abs(template.Typography.BodySizePoints - 12) < 0.01,
                    "DOCX Normal style body size must be imported.");
                Assert.True(Math.Abs(template.Typography.LineHeightPoints - 18) < 0.01,
                    "DOCX 1.5-line Normal spacing must be imported.");
                Assert.True(Math.Abs(template.Typography.Headings.H1 - 18) < 0.01 && Math.Abs(template.Typography.Headings.H2 - 16) < 0.01,
                    "DOCX heading sizes must be imported from heading styles.");
                Assert.True(template.Layout.CenterH1 && template.Layout.JustifyBody,
                    "DOCX heading/body alignment must be imported.");
                Assert.True(Math.Abs(template.Layout.FirstLineIndentPoints - 24) < 0.01 && Math.Abs(template.Layout.BlockGapPoints - 6) < 0.01,
                    "DOCX paragraph indentation and after-spacing must be imported.");
                Assert.True(Math.Abs(template.Layout.EffectiveMarginTopPoints - 72) < 0.01
                            && Math.Abs(template.Layout.EffectiveMarginRightPoints - 60) < 0.01
                            && Math.Abs(template.Layout.EffectiveMarginBottomPoints - 72) < 0.01
                            && Math.Abs(template.Layout.EffectiveMarginLeftPoints - 90) < 0.01,
                    "DOCX section margins must be imported independently.");
            }));

        runner.Run("Markdown templates: malformed DOCX imports fail closed", () =>
            RunWithTempDirectory(tempDir =>
            {
                string malformed = Path.Combine(tempDir, "malformed.docx");
                using (ZipArchive archive = ZipFile.Open(malformed, ZipArchiveMode.Create))
                {
                    ZipArchiveEntry document = archive.CreateEntry("word/document.xml");
                    using StreamWriter writer = new(document.Open());
                    writer.Write("<document />");
                }

                Assert.Throws<InvalidDataException>(() => MarkdownTemplateSource.Load(malformed));
                string unsupported = Path.Combine(tempDir, "template.txt");
                File.WriteAllText(unsupported, "not a template");
                Assert.Throws<InvalidDataException>(() => MarkdownTemplateSource.Load(unsupported));
            }));

        runner.Run("Markdown templates: unsupported DOCX fonts fail instead of silently changing typography", () =>
            RunWithTempDirectory(tempDir =>
            {
                string templatePath = Path.Combine(tempDir, "unsupported-font.docx");
                CreateWordTemplateFixture(templatePath, "Aptos");
                Assert.Throws<InvalidDataException>(() => MarkdownTemplateSource.Load(templatePath));
            }));

        runner.Run("Markdown templates: theme fonts override direct fonts at the same style level", () =>
            RunWithTempDirectory(tempDir =>
            {
                string templatePath = Path.Combine(tempDir, "mixed-font-precedence.docx");
                CreateWordTemplateFixture(templatePath, includeDirectAlongsideTheme: true);

                MarkdownDocumentTemplate template = MarkdownTemplateSource.Load(templatePath);
                Assert.Equal("Times New Roman", template.Typography.LatinFont);
                Assert.Equal("KaiU", template.Typography.CjkFont);
            }));

        runner.Run("Markdown templates: East Asian theme fonts follow the effective Word language", () =>
            RunWithTempDirectory(tempDir =>
            {
                string templatePath = Path.Combine(tempDir, "japanese-theme.docx");
                CreateWordTemplateFixture(templatePath, eastAsiaLanguage: "ja-JP", multipleEastAsiaFonts: true);

                MarkdownDocumentTemplate template = MarkdownTemplateSource.Load(templatePath);
                Assert.Equal("MS Gothic", template.Typography.CjkFont);
            }));

        runner.Run("Markdown templates: ambiguous East Asian theme fonts fail closed without a language", () =>
            RunWithTempDirectory(tempDir =>
            {
                string templatePath = Path.Combine(tempDir, "ambiguous-east-asia.docx");
                CreateWordTemplateFixture(templatePath, multipleEastAsiaFonts: true);
                Assert.Throws<InvalidDataException>(() => MarkdownTemplateSource.Load(templatePath));
            }));

        runner.Run("Markdown templates: unresolved explicit theme fonts fail closed", () =>
            RunWithTempDirectory(tempDir =>
            {
                string templatePath = Path.Combine(tempDir, "missing-theme.docx");
                CreateWordTemplateFixture(templatePath, includeTheme: false);
                Assert.Throws<InvalidDataException>(() => MarkdownTemplateSource.Load(templatePath));
            }));

        runner.Run("Markdown to Word: imported DOCX template drives generated Word formatting", () =>
            RunWithTempDirectory(tempDir =>
            {
                string templatePath = Path.Combine(tempDir, "school-format.docx");
                string input = Path.Combine(tempDir, "paper.md");
                string output = Path.Combine(tempDir, "paper.docx");
                CreateWordTemplateFixture(templatePath);
                File.WriteAllText(input, "# Thesis title\n\nBody paragraph for imported formatting.");

                FileProcessor.ConvertMarkdownToWord(input, output, MarkdownPdfOptions.Create(templatePath: templatePath));

                using ZipArchive archive = ZipFile.OpenRead(output);
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                XDocument styles = ReadXml(archive, "word/styles.xml");
                XDocument document = ReadXml(archive, "word/document.xml");
                XElement normal = styles.Descendants(w + "style")
                    .Single(style => style.Attribute(w + "styleId")?.Value == "Normal");
                XElement heading1 = styles.Descendants(w + "style")
                    .Single(style => style.Attribute(w + "styleId")?.Value == "Heading1");
                XElement margins = document.Descendants(w + "pgMar").Single();
                XElement body = document.Descendants(w + "p")
                    .First(p => p.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val")?.Value == "Normal");

                Assert.Equal("Times New Roman", normal.Descendants(w + "rFonts").Single().Attribute(w + "ascii")!.Value);
                Assert.Equal("KaiU", normal.Descendants(w + "rFonts").Single().Attribute(w + "eastAsia")!.Value);
                Assert.Equal("36", heading1.Descendants(w + "sz").Single().Attribute(w + "val")!.Value);
                Assert.Equal("center", heading1.Descendants(w + "jc").Single().Attribute(w + "val")!.Value);
                Assert.Equal("1800", margins.Attribute(w + "left")!.Value);
                Assert.Equal("1200", margins.Attribute(w + "right")!.Value);
                Assert.Equal("480", body.Element(w + "pPr")!.Element(w + "ind")!.Attribute(w + "firstLine")!.Value);
                Assert.Equal("both", body.Element(w + "pPr")!.Element(w + "jc")!.Attribute(w + "val")!.Value);
            }));

        runner.Run("Markdown to Word: Academic list paragraphs do not inherit body first-line indent", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "academic-list.md");
                string output = Path.Combine(tempDir, "academic-list.docx");
                File.WriteAllText(input, "- First list paragraph.\n\n  Second list paragraph in the same item.\n\n> Quoted evidence.");

                FileProcessor.ConvertMarkdownToWord(input, output,
                    MarkdownPdfOptions.Create(MarkdownPdfOptions.ThemeAcademic));

                using ZipArchive archive = ZipFile.OpenRead(output);
                XDocument document = ReadXml(archive, "word/document.xml");
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                var normalParagraphs = document.Descendants(w + "p")
                    .Where(p => p.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val")?.Value == "Normal")
                    .ToList();
                XElement firstListParagraph = normalParagraphs
                    .Single(p => p.Descendants(w + "t").Any(t => t.Value.Contains("First list paragraph", StringComparison.Ordinal)));
                XElement continuationParagraph = normalParagraphs
                    .Single(p => p.Descendants(w + "t").Any(t => t.Value.Contains("Second list paragraph", StringComparison.Ordinal)));
                Assert.True(firstListParagraph.Element(w + "pPr")?.Element(w + "numPr") is not null,
                    "The first paragraph in a Markdown list item must use genuine Word numbering.");
                Assert.Equal("400", continuationParagraph.Element(w + "pPr")?.Element(w + "ind")?.Attribute(w + "left")?.Value ?? "");
                Assert.True(firstListParagraph.Element(w + "pPr")?.Element(w + "ind")?.Attribute(w + "firstLine") is null
                            && continuationParagraph.Element(w + "pPr")?.Element(w + "ind")?.Attribute(w + "firstLine") is null,
                    "Academic list paragraphs must not inherit the ordinary body first-line indent.");
                Assert.True(firstListParagraph.Element(w + "pPr")?.Element(w + "jc") is null
                            && continuationParagraph.Element(w + "pPr")?.Element(w + "jc") is null,
                    "Academic list paragraphs must not inherit ordinary-body justification.");
                XElement quote = document.Descendants(w + "p")
                    .Single(p => p.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val")?.Value == "Quote");
                Assert.True(quote.Element(w + "pPr")?.Element(w + "jc") is null,
                    "Academic quote paragraphs must not inherit ordinary-body justification.");
            }));

        runner.Run("Markdown to Word: nested list blocks keep shared print indentation", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "nested-list.md");
                string output = Path.Combine(tempDir, "nested-list.docx");
                File.WriteAllText(input, """
                    - Item paragraph

                      ```text
                      nested code
                      ```

                      > nested quote

                      | A | B |
                      | --- | --- |
                      | one | two |
                    """);

                FileProcessor.ConvertMarkdownToWord(input, output, MarkdownPdfOptions.Create());

                using ZipArchive archive = ZipFile.OpenRead(output);
                XDocument document = ReadXml(archive, "word/document.xml");
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                XElement code = document.Descendants(w + "p")
                    .Single(p => p.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val")?.Value == "CodeBlock");
                Assert.Equal("300", code.Element(w + "pPr")?.Element(w + "ind")?.Attribute(w + "left")?.Value ?? "");

                XElement quote = document.Descendants(w + "p")
                    .Single(p => p.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val")?.Value == "Quote");
                Assert.Equal("270", quote.Element(w + "pPr")?.Element(w + "ind")?.Attribute(w + "left")?.Value ?? "");

                XElement quoteTable = document.Descendants(w + "tbl")
                    .Single(tbl => tbl.Element(w + "tblPr")?.Element(w + "tblBorders")?.Element(w + "left")?.Attribute(w + "val")?.Value == "single"
                                   && tbl.Element(w + "tblPr")?.Element(w + "tblStyle") is null);
                Assert.Equal("300", quoteTable.Element(w + "tblPr")?.Element(w + "tblInd")?.Attribute(w + "w")?.Value ?? "");

                XElement table = document.Descendants(w + "tbl")
                    .Single(tbl => tbl.Element(w + "tblPr")?.Element(w + "tblStyle")?.Attribute(w + "val")?.Value == "TableGrid");
                Assert.Equal("300", table.Element(w + "tblPr")?.Element(w + "tblInd")?.Attribute(w + "w")?.Value ?? "");
            }));

        runner.Run("Markdown to Word: quote container keeps one bar across nested blocks", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "quote-blocks.md");
                string output = Path.Combine(tempDir, "quote-blocks.docx");
                File.WriteAllText(input, """
                    > Quoted paragraph.
                    >
                    > ```text
                    > quoted code
                    > ```
                    >
                    > | A | B |
                    > | --- | --- |
                    > | one | two |
                    """);

                FileProcessor.ConvertMarkdownToWord(input, output, MarkdownPdfOptions.Create());

                using ZipArchive archive = ZipFile.OpenRead(output);
                XDocument document = ReadXml(archive, "word/document.xml");
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                XElement quoteTable = document.Descendants(w + "tbl")
                    .Single(tbl => tbl.Element(w + "tblPr")?.Element(w + "tblBorders")?.Element(w + "left")?.Attribute(w + "val")?.Value == "single"
                                   && tbl.Element(w + "tblPr")?.Element(w + "tblStyle") is null);
                Assert.True(quoteTable.Descendants(w + "p")
                    .Any(p => p.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val")?.Value == "CodeBlock"),
                    "Quoted fenced code must remain inside the continuous quote-bar container.");
                Assert.True(quoteTable.Descendants(w + "tbl")
                    .Any(tbl => tbl.Element(w + "tblPr")?.Element(w + "tblStyle")?.Attribute(w + "val")?.Value == "TableGrid"),
                    "Quoted tables must remain inside the continuous quote-bar container.");
            }));

        runner.Run("Markdown to Word: text-size scaling includes academic first-line indent", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "scaled-indent.md");
                string output = Path.Combine(tempDir, "scaled-indent.docx");
                File.WriteAllText(input, "# Academic\n\nScaled body paragraph.");

                FileProcessor.ConvertMarkdownToWord(input, output,
                    MarkdownPdfOptions.Create(
                        MarkdownPdfOptions.ThemeAcademic,
                        textSize: MarkdownPdfOptions.TextLarge));

                using ZipArchive archive = ZipFile.OpenRead(output);
                XDocument document = ReadXml(archive, "word/document.xml");
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                XElement body = document.Descendants(w + "p")
                    .Single(p => p.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val")?.Value == "Normal");
                Assert.Equal("538", body.Element(w + "pPr")?.Element(w + "ind")?.Attribute(w + "firstLine")?.Value ?? "");
            }));

        runner.Run("Markdown to Word: preserves full-width punctuation and real list semantics", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "punctuation-list.md");
                string output = Path.Combine(tempDir, "punctuation-list.docx");
                File.WriteAllText(input,
                    "## 一、作業封包來源\n\n取得：保留中文標點。File ➔ Export Objects\n\n- 題目連結：Root-Me\n- 學習核心：保留原文\n\n3. Third\n4. Fourth");

                FileProcessor.ConvertMarkdownToWord(input, output, MarkdownPdfOptions.Create());

                using ZipArchive archive = ZipFile.OpenRead(output);
                XDocument document = ReadXml(archive, "word/document.xml");
                XDocument numbering = ReadXml(archive, "word/numbering.xml");
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                string text = string.Concat(document.Descendants(w + "t").Select(e => e.Value));
                Assert.True(text.Contains("一、作業封包來源", StringComparison.Ordinal)
                            && text.Contains("取得：保留中文標點。", StringComparison.Ordinal)
                            && text.Contains("題目連結：Root-Me", StringComparison.Ordinal)
                            && text.Contains("File ➔ Export Objects", StringComparison.Ordinal),
                    "DOCX conversion must preserve source punctuation and technical symbols without rewriting them.");
                Assert.False(text.Contains("• 題目連結", StringComparison.Ordinal),
                    "Bullet markers must come from Word numbering rather than literal bullet text.");
                Assert.True(document.Descendants(w + "numPr").Count() == 4,
                    "Each list item must carry genuine Word numbering properties.");
                Assert.True(numbering.Descendants(w + "numFmt").Any(e => e.Attribute(w + "val")?.Value == "bullet")
                            && numbering.Descendants(w + "numFmt").Any(e => e.Attribute(w + "val")?.Value == "decimal"),
                    "The numbering part must define both bullet and ordered-list formats.");
                Assert.True(numbering.Descendants(w + "startOverride").Any(e => e.Attribute(w + "val")?.Value == "3"),
                    "An ordered Markdown list with a non-one start must retain its starting number in Word numbering.");
            }));
    }

    private static XDocument ReadXml(ZipArchive archive, string name)
    {
        ZipArchiveEntry entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Missing DOCX part: {name}");
        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string ReadAllText(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void CreateWordTemplateFixture(
        string path,
        string latinTypeface = "Times New Roman",
        bool includeDirectAlongsideTheme = false,
        string? eastAsiaLanguage = null,
        bool multipleEastAsiaFonts = false,
        bool includeTheme = true)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);

        var defaultFonts = new XElement(w + "rFonts",
            new XAttribute(w + "ascii", "Arial"),
            new XAttribute(w + "hAnsi", "Arial"),
            new XAttribute(w + "eastAsia", "Microsoft JhengHei"));
        var styles = new XElement(w + "styles",
            new XElement(w + "docDefaults",
                new XElement(w + "rPrDefault",
                    new XElement(w + "rPr",
                        defaultFonts,
                        new XElement(w + "sz", new XAttribute(w + "val", "20"))))));

        var styleFonts = new XElement(w + "rFonts",
            new XAttribute(w + "asciiTheme", "minorHAnsi"),
            new XAttribute(w + "hAnsiTheme", "minorHAnsi"),
            new XAttribute(w + "eastAsiaTheme", "minorEastAsia"));
        if (includeDirectAlongsideTheme)
        {
            styleFonts.SetAttributeValue(w + "ascii", "Arial");
            styleFonts.SetAttributeValue(w + "hAnsi", "Arial");
            styleFonts.SetAttributeValue(w + "eastAsia", "Microsoft JhengHei");
        }

        var baseRunProperties = new XElement(w + "rPr",
            styleFonts,
            new XElement(w + "sz", new XAttribute(w + "val", "24")));
        if (!string.IsNullOrWhiteSpace(eastAsiaLanguage))
            baseRunProperties.Add(new XElement(w + "lang", new XAttribute(w + "eastAsia", eastAsiaLanguage)));

        styles.Add(new XElement(w + "style",
            new XAttribute(w + "type", "paragraph"),
            new XAttribute(w + "styleId", "BaseBody"),
            new XElement(w + "name", new XAttribute(w + "val", "Base Body")),
            new XElement(w + "pPr",
                new XElement(w + "spacing",
                    new XAttribute(w + "after", "120"),
                    new XAttribute(w + "line", "360"),
                    new XAttribute(w + "lineRule", "auto")),
                new XElement(w + "ind", new XAttribute(w + "firstLine", "480")),
                new XElement(w + "jc", new XAttribute(w + "val", "both"))),
            baseRunProperties));
        styles.Add(new XElement(w + "style",
            new XAttribute(w + "type", "paragraph"),
            new XAttribute(w + "default", "1"),
            new XAttribute(w + "styleId", "Normal"),
            new XElement(w + "name", new XAttribute(w + "val", "Normal")),
            new XElement(w + "basedOn", new XAttribute(w + "val", "BaseBody"))));
        styles.Add(new XElement(w + "style",
            new XAttribute(w + "type", "paragraph"),
            new XAttribute(w + "styleId", "Heading1"),
            new XElement(w + "name", new XAttribute(w + "val", "heading 1")),
            new XElement(w + "basedOn", new XAttribute(w + "val", "Normal")),
            new XElement(w + "pPr",
                new XElement(w + "outlineLvl", new XAttribute(w + "val", "0")),
                new XElement(w + "jc", new XAttribute(w + "val", "center"))),
            new XElement(w + "rPr", new XElement(w + "sz", new XAttribute(w + "val", "36")))));
        styles.Add(new XElement(w + "style",
            new XAttribute(w + "type", "paragraph"),
            new XAttribute(w + "styleId", "ThesisSection"),
            new XElement(w + "name", new XAttribute(w + "val", "Custom section")),
            new XElement(w + "basedOn", new XAttribute(w + "val", "Normal")),
            new XElement(w + "pPr", new XElement(w + "outlineLvl", new XAttribute(w + "val", "1"))),
            new XElement(w + "rPr", new XElement(w + "sz", new XAttribute(w + "val", "32")))));
        WriteXml(archive, "word/styles.xml", new XDocument(styles));

        if (includeTheme)
        {
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var minorFont = new XElement(a + "minorFont",
                new XElement(a + "latin", new XAttribute("typeface", latinTypeface)),
                new XElement(a + "ea", new XAttribute("typeface", "")),
                new XElement(a + "font", new XAttribute("script", "Hant"), new XAttribute("typeface", "DFKai-SB")));
            if (multipleEastAsiaFonts)
                minorFont.Add(new XElement(a + "font", new XAttribute("script", "Jpan"), new XAttribute("typeface", "MS Gothic")));

            var theme = new XElement(a + "theme",
                new XElement(a + "themeElements",
                    new XElement(a + "fontScheme",
                        new XAttribute("name", "Fixture fonts"),
                        new XElement(a + "majorFont",
                            new XElement(a + "latin", new XAttribute("typeface", "Cambria")),
                            new XElement(a + "ea", new XAttribute("typeface", ""))),
                        minorFont)));
            WriteXml(archive, "word/theme/theme1.xml", new XDocument(theme));
        }

        var body = new XElement(w + "body",
            new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", "Template"))),
            new XElement(w + "sectPr",
                new XElement(w + "pgMar",
                    new XAttribute(w + "top", "1440"),
                    new XAttribute(w + "right", "1200"),
                    new XAttribute(w + "bottom", "1440"),
                    new XAttribute(w + "left", "1800"))));
        WriteXml(archive, "word/document.xml", new XDocument(new XElement(w + "document", body)));
    }

    private static void WriteXml(ZipArchive archive, string name, XDocument document)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using Stream stream = entry.Open();
        document.Save(stream);
    }
}

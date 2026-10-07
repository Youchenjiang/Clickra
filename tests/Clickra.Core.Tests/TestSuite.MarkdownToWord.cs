using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Clickra.Core;
using Clickra.Core.Processors;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private const string MarkdownToWordCommand = "md2word";

    public static void RegisterMarkdownToWordTests(TestRunner runner)
    {
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

                    | 欄位 | 值 |
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
                Assert.True(academicStyles.Contains("PMingLiU", StringComparison.Ordinal),
                    "Academic DOCX must use a serif Traditional Chinese family rather than JhengHei.");
                XDocument academicStylesXml = XDocument.Parse(academicStyles);
                XElement heading1Style = academicStylesXml.Descendants(w + "style")
                    .Single(style => style.Attribute(w + "styleId")?.Value == "Heading1");
                Assert.Equal("center", heading1Style.Descendants(w + "jc").Single().Attribute(w + "val")!.Value);

                XDocument minimalDocument = ReadXml(minimal, "word/document.xml");
                XDocument academicDocument = ReadXml(academic, "word/document.xml");
                string minimalMargin = minimalDocument.Descendants(w + "pgMar").Single().Attribute(w + "left")!.Value;
                string academicMargin = academicDocument.Descendants(w + "pgMar").Single().Attribute(w + "left")!.Value;
                Assert.False(minimalMargin == academicMargin,
                    "Minimal and Academic DOCX templates must use different page margins.");
                Assert.Equal("1440", academicMargin);
                var bodyIndents = academicDocument.Descendants(w + "p")
                    .Where(p => p.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val")?.Value == "Normal")
                    .Select(p => p.Element(w + "pPr")?.Element(w + "ind")?.Attribute(w + "firstLine")?.Value)
                    .Where(value => value is not null)
                    .ToList();
                Assert.True(bodyIndents.Count >= 2 && bodyIndents.All(value => value == "480"),
                    "Academic DOCX body paragraphs must use a 24pt first-line indent without applying it to every Normal-style container.");
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
                string leftMargin = document.Descendants(w + "pgMar").Single().Attribute(w + "left")!.Value;
                Assert.Equal("960", leftMargin);
            }));

        runner.Run("Markdown to Word: Academic list paragraphs do not inherit body first-line indent", () =>
            RunWithTempDirectory(tempDir =>
            {
                string input = Path.Combine(tempDir, "academic-list.md");
                string output = Path.Combine(tempDir, "academic-list.docx");
                File.WriteAllText(input, "- First list paragraph.\n\n  Second list paragraph in the same item.");

                FileProcessor.ConvertMarkdownToWord(input, output,
                    MarkdownPdfOptions.Create(MarkdownPdfOptions.ThemeAcademic));

                using ZipArchive archive = ZipFile.OpenRead(output);
                XDocument document = ReadXml(archive, "word/document.xml");
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                var listIndents = document.Descendants(w + "p")
                    .Select(p => p.Element(w + "pPr")?.Element(w + "ind"))
                    .Where(ind => ind?.Attribute(w + "left")?.Value == "360")
                    .ToList();
                Assert.True(listIndents.Count >= 2,
                    "Both paragraphs in a multi-paragraph list item must retain list indentation.");
                Assert.True(listIndents.All(ind => ind!.Attribute(w + "firstLine") is null),
                    "Academic list paragraphs must not inherit the ordinary body first-line indent.");
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
}

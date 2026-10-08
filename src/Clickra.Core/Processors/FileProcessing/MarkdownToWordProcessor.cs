using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Clickra.Core.Processors;

/// <summary>Converts Markdown to a self-contained DOCX using Markdig and WordprocessingML.</summary>
public sealed class MarkdownToWordProcessor : MultiFileProcessorBase
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private string? _outputPath;

    /// <summary>Processes the supplied Markdown files with the configured conversion options.</summary>
    public override void Process(
        List<string> files,
        string? outputPath,
        Dictionary<string, object>? options = null,
        Action<int, int, string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (files.Count != 1)
            throw new ArgumentException("Markdown to Word converts one input file per output.", nameof(files));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is required for Markdown to Word conversion.", nameof(outputPath));

        _outputPath = outputPath;
        base.Process(files, outputPath, options, onProgress, cancellationToken);
    }

    /// <summary>Renders one Markdown input file into the target Word document.</summary>
    protected override void ProcessFile(
        string filePath,
        int fileIndex,
        int totalFiles,
        Dictionary<string, object>? options,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(filePath)) throw new FileNotFoundException("Markdown file not found", filePath);

        onProgress?.Invoke(15, 100, Localization.T("md_word_progress_parsing", Path.GetFileName(filePath)));
        MarkdownDocument document = Markdown.Parse(File.ReadAllText(filePath), Pipeline);
        cancellationToken.ThrowIfCancellationRequested();

        onProgress?.Invoke(45, 100, Localization.T("md_word_progress_rendering", Path.GetFileName(filePath)));
        string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? Directory.GetCurrentDirectory();
        var writer = new DocxWriter(
            MarkdownTemplateCatalog.Resolve(
                MarkdownPdfOptions.GetTheme(options),
                MarkdownPdfOptions.GetTemplatePath(options)),
            MarkdownPdfOptions.GetPaper(options),
            MarkdownPdfOptions.GetTextSize(options),
            MarkdownPdfOptions.GetCodeTheme(options),
            baseDirectory,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke(90, 100, Localization.T("md_word_progress_saving", Path.GetFileName(_outputPath!)));
        writer.Save(document, _outputPath!);
    }

    /// <summary>Finalizes the completed Markdown-to-Word conversion batch.</summary>
    protected override void OnAllFilesProcessed(
        string? outputPath,
        int totalFiles,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken) =>
        onProgress?.Invoke(100, 100, Localization.T("md_word_progress_done", Path.GetFileName(outputPath ?? "")));

    private sealed class DocxWriter
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private static readonly XNamespace PIC = "http://schemas.openxmlformats.org/drawingml/2006/picture";

        private readonly MarkdownDocumentTemplate _template;
        private readonly MarkdownResolvedLayout _layout;
        private readonly MarkdownResolvedPalette _resolvedPalette;
        private readonly string _paper;
        private readonly double _scale;
        private readonly bool _lightCode;
        private readonly string _baseDirectory;
        private readonly CancellationToken _token;
        private readonly List<Relationship> _relationships = new();
        private readonly List<ImagePart> _images = new();
        private readonly List<NumberingInstance> _numberingInstances = new();
        private int _nextRelationshipId = 1;
        private int _nextNumberingId = 1;

        public DocxWriter(
            MarkdownDocumentTemplate template,
            string paper,
            string textSize,
            string codeTheme,
            string baseDirectory,
            CancellationToken token)
        {
            _template = template;
            _paper = paper;
            _scale = textSize switch
            {
                MarkdownPdfOptions.TextSmall => 0.9,
                MarkdownPdfOptions.TextLarge => 1.12,
                _ => 1.0
            };
            _layout = MarkdownResolvedLayout.Create(_template, _scale);
            _resolvedPalette = MarkdownResolvedPalette.Create(_template);
            _lightCode = codeTheme == MarkdownPdfOptions.CodeLight;
            _baseDirectory = baseDirectory;
            _token = token;
        }

        public void Save(MarkdownDocument document, string outputPath)
        {
            var body = new XElement(W + "body");
            foreach (Block block in document)
            {
                _token.ThrowIfCancellationRequested();
                RenderBlock(block, body, 0);
            }
            body.Add(CreateSectionProperties());

            var root = new XElement(W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W),
                new XAttribute(XNamespace.Xmlns + "r", R),
                new XAttribute(XNamespace.Xmlns + "wp", WP),
                new XAttribute(XNamespace.Xmlns + "a", A),
                new XAttribute(XNamespace.Xmlns + "pic", PIC),
                body);

            string tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
            using FileBackupScope backupScope = FileBackupScope.Create(outputPath);
            try
            {
                string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
                {
                    WriteXml(archive, "[Content_Types].xml", CreateContentTypes());
                    WriteXml(archive, "_rels/.rels", CreateRootRelationships());
                    WriteXml(archive, "word/document.xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root));
                    WriteXml(archive, "word/styles.xml", CreateStyles());
                    if (_numberingInstances.Count > 0) WriteXml(archive, "word/numbering.xml", CreateNumbering());
                    WriteXml(archive, "word/_rels/document.xml.rels", CreateDocumentRelationships());
                    foreach (ImagePart image in _images)
                    {
                        ZipArchiveEntry entry = archive.CreateEntry("word/media/" + image.FileName, CompressionLevel.Optimal);
                        using Stream target = entry.Open();
                        using FileStream source = File.OpenRead(image.SourcePath);
                        source.CopyTo(target);
                    }
                }
                _token.ThrowIfCancellationRequested();
                File.Move(tempPath, outputPath, true);
                backupScope.Commit();
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        private void RenderBlock(
            Block block,
            XElement parent,
            int listDepth,
            int blockIndentTwips = 0,
            int containerWidthTwips = 0)
        {
            if (containerWidthTwips <= 0) containerWidthTwips = PointsToTwips(UsablePageWidthPoints);
            switch (block)
            {
                case HeadingBlock heading:
                    parent.Add(CreateInlineParagraph(
                        heading.Inline,
                        $"Heading{Math.Clamp(heading.Level, 1, 6)}",
                        indentTwips: blockIndentTwips));
                    break;
                case ParagraphBlock paragraph:
                    parent.Add(CreateInlineParagraph(
                        paragraph.Inline,
                        "Normal",
                        indentTwips: blockIndentTwips,
                        bodyParagraph: blockIndentTwips == 0));
                    break;
                case QuoteBlock quote:
                    parent.Add(CreateQuoteBlock(quote, listDepth, blockIndentTwips, containerWidthTwips));
                    break;
                case ListBlock list:
                    RenderList(list, parent, listDepth, blockIndentTwips, containerWidthTwips);
                    break;
                case FencedCodeBlock fenced:
                    parent.Add(CreateCodeParagraph(fenced.Lines.ToString(), blockIndentTwips));
                    break;
                case CodeBlock code:
                    parent.Add(CreateCodeParagraph(code.Lines.ToString(), blockIndentTwips));
                    break;
                case Table table:
                    parent.Add(CreateTable(table, blockIndentTwips, containerWidthTwips));
                    break;
                case ThematicBreakBlock:
                    parent.Add(CreateRuleParagraph(blockIndentTwips));
                    break;
                case HtmlBlock html:
                    string text = StripHtml(html.Lines.ToString());
                    if (!string.IsNullOrWhiteSpace(text))
                        parent.Add(CreateTextParagraph(text, "Normal", blockIndentTwips, bodyParagraph: blockIndentTwips == 0));
                    break;
                case ContainerBlock container:
                    foreach (Block child in container)
                        RenderBlock(child, parent, listDepth, blockIndentTwips, containerWidthTwips);
                    break;
                case LeafBlock leaf when leaf.Inline is not null:
                    parent.Add(CreateInlineParagraph(leaf.Inline, "Normal", indentTwips: blockIndentTwips));
                    break;
            }
        }

        private void RenderList(
            ListBlock list,
            XElement parent,
            int depth,
            int baseIndentTwips,
            int containerWidthTwips)
        {
            int level = Math.Clamp(depth, 0, 8);
            int start = int.TryParse(list.OrderedStart, out int orderedStart) ? orderedStart : 1;
            int numberingId = _nextNumberingId++;
            _numberingInstances.Add(new NumberingInstance(numberingId, list.IsOrdered, level, start));

            foreach (Block child in list)
            {
                if (child is not ListItemBlock item) continue;
                bool first = true;
                foreach (Block itemChild in item)
                {
                    if (itemChild is ParagraphBlock paragraph)
                    {
                        XElement p = first
                            ? CreateInlineParagraph(
                                paragraph.Inline,
                                "Normal",
                                indentTwips: baseIndentTwips,
                                numberingId: numberingId,
                                numberingLevel: level)
                            : CreateInlineParagraph(
                                paragraph.Inline,
                                "Normal",
                                indentTwips: baseIndentTwips + PointsToTwips((level + 1) * _layout.ListIndentPoints));
                        parent.Add(p);
                        first = false;
                    }
                    else if (itemChild is ListBlock nested)
                    {
                        RenderList(nested, parent, depth + 1, baseIndentTwips, containerWidthTwips);
                    }
                    else
                    {
                        int contentIndent = baseIndentTwips + PointsToTwips((level + 1) * _layout.ListIndentPoints);
                        RenderBlock(itemChild, parent, depth + 1, contentIndent, containerWidthTwips);
                    }
                }
            }

            if (_layout.ListAfterPoints > 0)
            {
                XElement? lastParagraph = parent.Elements(W + "p").LastOrDefault();
                XElement? spacing = lastParagraph?.Element(W + "pPr")?.Element(W + "spacing");
                spacing?.SetAttributeValue(W + "after", PointsToTwips(_layout.ListAfterPoints));
            }
        }

        private XElement CreateInlineParagraph(
            ContainerInline? inline,
            string style,
            int indentTwips = 0,
            bool bodyParagraph = false,
            int? numberingId = null,
            int numberingLevel = 0)
        {
            var pPr = ParagraphProperties(style, indentTwips, bodyParagraph);
            if (numberingId is not null)
            {
                pPr.Element(W + "ind")?.Remove();
                int textIndent = indentTwips + PointsToTwips((numberingLevel + 1) * _layout.ListIndentPoints);
                pPr.Add(new XElement(W + "ind",
                    new XAttribute(W + "left", textIndent),
                    new XAttribute(W + "hanging", PointsToTwips(_layout.ListIndentPoints))));
                pPr.Add(new XElement(W + "numPr",
                    new XElement(W + "ilvl", new XAttribute(W + "val", numberingLevel)),
                    new XElement(W + "numId", new XAttribute(W + "val", numberingId.Value))));
                pPr.Add(new XElement(W + "spacing",
                    new XAttribute(W + "after", 0),
                    new XAttribute(W + "line", PointsToTwips(_layout.BodyLineHeightPoints)),
                    new XAttribute(W + "lineRule", "exact")));
            }
            var p = new XElement(W + "p", pPr);
            AppendInlines(p, inline, false, false, null);
            return p;
        }

        private XElement CreateTextParagraph(string text, string style, int indentTwips = 0, bool bodyParagraph = false) =>
            new(W + "p", ParagraphProperties(style, indentTwips, bodyParagraph), CreateRun(text, false, false, false, null));

        private XElement CreateCodeParagraph(string code, int indentTwips)
        {
            string background = _lightCode ? "F1F5F9" : "0F172A";
            string foreground = _lightCode ? _template.Palette.Strong.Hex : "F1F5F9";
            var pPr = ParagraphProperties("CodeBlock", indentTwips);
            pPr.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "fill", background)));
            pPr.Add(new XElement(W + "pBdr",
                CodePaddingBorder("top", _layout.CodeVerticalPaddingPoints, background),
                CodePaddingBorder("left", _layout.CodeHorizontalPaddingPoints, background),
                CodePaddingBorder("bottom", _layout.CodeVerticalPaddingPoints, background),
                CodePaddingBorder("right", _layout.CodeHorizontalPaddingPoints, background)));
            var p = new XElement(W + "p", pPr);
            string[] lines = NormalizeNewlines(code).TrimEnd('\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) p.Add(new XElement(W + "r", new XElement(W + "br")));
                p.Add(CreateRun(lines[i], false, false, true, foreground, shadeCode: false));
            }
            return p;
        }

        private XElement CreateQuoteBlock(
            QuoteBlock quote,
            int listDepth,
            int indentTwips,
            int containerWidthTwips)
        {
            int quoteWidth = Math.Max(1, containerWidthTwips - indentTwips);
            int quoteIndent = PointsToTwips(_layout.QuoteIndentPoints);
            var table = new XElement(W + "tbl",
                new XElement(W + "tblPr",
                    new XElement(W + "tblW", new XAttribute(W + "w", quoteWidth), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "tblLayout", new XAttribute(W + "type", "fixed")),
                    indentTwips > 0
                        ? new XElement(W + "tblInd", new XAttribute(W + "w", indentTwips), new XAttribute(W + "type", "dxa"))
                        : null,
                    new XElement(W + "tblBorders",
                        QuoteBorder("left"),
                        HiddenBorder("top"),
                        HiddenBorder("bottom"),
                        HiddenBorder("right"),
                        HiddenBorder("insideH"),
                        HiddenBorder("insideV"))),
                new XElement(W + "tblGrid",
                    new XElement(W + "gridCol", new XAttribute(W + "w", quoteWidth))));

            var cell = new XElement(W + "tc",
                new XElement(W + "tcPr",
                    new XElement(W + "tcW", new XAttribute(W + "w", quoteWidth), new XAttribute(W + "type", "dxa")),
                    _layout.QuoteVerticalPaddingPoints > 0
                        ? new XElement(W + "tcMar",
                            CellMargin("top", _layout.QuoteVerticalPaddingPoints),
                            CellMargin("bottom", _layout.QuoteVerticalPaddingPoints))
                        : null,
                    _template.Id == MarkdownPdfOptions.ThemeDefault
                        ? new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "fill", _resolvedPalette.Surface.Hex))
                        : null));
            foreach (Block child in quote)
            {
                if (child is ParagraphBlock quoteParagraph)
                    cell.Add(CreateInlineParagraph(quoteParagraph.Inline, "Quote", indentTwips: quoteIndent));
                else
                    RenderBlock(child, cell, listDepth, quoteIndent, quoteWidth);
            }
            if (!cell.Elements().Any()) cell.Add(CreateTextParagraph("", "Quote", quoteIndent));
            if (cell.Elements().Last().Name == W + "tbl") cell.Add(CreateTextParagraph("", "Quote", quoteIndent));
            table.Add(new XElement(W + "tr", cell));
            return table;
        }

        private XElement CreateTable(Table table, int indentTwips, int containerWidthTwips)
        {
            int columnCount = Math.Max(1, table.OfType<TableRow>().Select(row => row.Count).DefaultIfEmpty(1).Max());
            int tableWidth = Math.Max(1, containerWidthTwips - indentTwips);
            IReadOnlyList<double> fractions = MarkdownTableColumnSizer.ResolveFractions(table, columnCount);
            int[] columnWidths = fractions.Select(fraction => Math.Max(1, (int)Math.Round(tableWidth * fraction))).ToArray();
            int widthDelta = tableWidth - columnWidths.Sum();
            if (columnWidths.Length > 0) columnWidths[^1] += widthDelta;
            var element = new XElement(W + "tbl",
                new XElement(W + "tblPr",
                    new XElement(W + "tblStyle", new XAttribute(W + "val", "TableGrid")),
                    new XElement(W + "tblW", new XAttribute(W + "w", tableWidth), new XAttribute(W + "type", "dxa")),
                    indentTwips > 0
                        ? new XElement(W + "tblInd", new XAttribute(W + "w", indentTwips), new XAttribute(W + "type", "dxa"))
                        : null,
                    new XElement(W + "tblLayout", new XAttribute(W + "type", "fixed")),
                    new XElement(W + "tblCellMar",
                        CellMargin("top", _layout.TableVerticalPaddingPoints),
                        CellMargin("left", _layout.TableHorizontalPaddingPoints),
                        CellMargin("bottom", _layout.TableVerticalPaddingPoints),
                        CellMargin("right", _layout.TableHorizontalPaddingPoints))),
                new XElement(W + "tblGrid", Enumerable.Range(0, columnCount)
                    .Select(index => new XElement(W + "gridCol", new XAttribute(W + "w", columnWidths[index])))));

            foreach (TableRow row in table)
            {
                var tr = new XElement(W + "tr");
                int columnIndex = 0;
                foreach (TableCell cell in row)
                {
                    int columnWidth = columnWidths[Math.Min(columnIndex, columnWidths.Length - 1)];
                    var tcPr = new XElement(W + "tcPr",
                        new XElement(W + "tcW", new XAttribute(W + "w", columnWidth), new XAttribute(W + "type", "dxa")));
                    if (row.IsHeader && _template.Layout.FillTableHeader)
                        tcPr.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "fill", _template.Palette.SoftAccent.Hex)));
                    else if (_template.Id == MarkdownPdfOptions.ThemeDefault)
                        tcPr.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "fill", _resolvedPalette.Surface.Hex)));
                    var tc = new XElement(W + "tc", tcPr);
                    bool added = false;
                    foreach (Block child in cell)
                    {
                        if (child is ParagraphBlock paragraph)
                        {
                            XElement p = CreateInlineParagraph(paragraph.Inline, row.IsHeader ? "TableHeader" : "TableText");
                            if (row.IsHeader) MakeRunsBold(p);
                            tc.Add(p);
                            added = true;
                        }
                        else
                        {
                            int cellWidth = Math.Max(1, columnWidth - PointsToTwips(_layout.TableHorizontalPaddingPoints * 2));
                            RenderBlock(child, tc, 0, 0, cellWidth);
                            added = true;
                        }
                    }
                    if (!added) tc.Add(CreateTextParagraph("", row.IsHeader ? "TableHeader" : "TableText"));
                    tr.Add(tc);
                    columnIndex++;
                }
                element.Add(tr);
            }
            return element;
        }

        private XElement CreateRuleParagraph(int indentTwips) =>
            new(W + "p",
                new XElement(W + "pPr",
                    indentTwips > 0 ? new XElement(W + "ind", new XAttribute(W + "left", indentTwips)) : null,
                    new XElement(W + "spacing",
                        new XAttribute(W + "after", 0),
                        new XAttribute(W + "line", PointsToTwips(_layout.RuleBlockHeightPoints(_template))),
                        new XAttribute(W + "lineRule", "exact")),
                    new XElement(W + "pBdr",
                        new XElement(W + "bottom",
                            new XAttribute(W + "val", "single"),
                            new XAttribute(W + "sz", "6"),
                            new XAttribute(W + "space", "1"),
                            new XAttribute(W + "color", _template.Palette.Border.Hex)))));

        private void AppendInlines(XElement paragraph, ContainerInline? inline, bool bold, bool italic, string? url)
        {
            if (inline is null) return;
            for (Inline? current = inline.FirstChild; current is not null; current = current.NextSibling)
                AppendInline(paragraph, current, bold, italic, url);
        }

        private void AppendInline(XElement paragraph, Inline inline, bool bold, bool italic, string? url)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    AddRunOrHyperlink(paragraph, literal.Content.ToString(), bold, italic, false, url);
                    break;
                case CodeInline code:
                    AddRunOrHyperlink(paragraph, code.Content, bold, italic, true, url);
                    break;
                case EmphasisInline emphasis:
                    bool nextBold = bold || emphasis.DelimiterCount >= 2;
                    bool nextItalic = italic || emphasis.DelimiterCount == 1;
                    foreach (Inline child in emphasis) AppendInline(paragraph, child, nextBold, nextItalic, url);
                    break;
                case LinkInline link when link.IsImage:
                    if (!TryAddImage(paragraph, link.Url))
                        AddRunOrHyperlink(paragraph, link.Title ?? link.Url ?? "image", bold, italic, false, null);
                    break;
                case LinkInline link:
                    string? target = IsWebUrl(link.Url) ? link.Url : null;
                    foreach (Inline child in link) AppendInline(paragraph, child, bold, italic, target);
                    break;
                case LineBreakInline:
                    paragraph.Add(new XElement(W + "r", new XElement(W + "br")));
                    break;
                case ContainerInline container:
                    foreach (Inline child in container) AppendInline(paragraph, child, bold, italic, url);
                    break;
            }
        }

        private void AddRunOrHyperlink(XElement paragraph, string text, bool bold, bool italic, bool code, string? url)
        {
            string normalized = NormalizeDisplayGlyphs(text);
            XElement run = CreateRun(normalized, bold, italic, code, url is not null ? _template.Palette.Accent.Hex : null);
            if (url is null)
            {
                paragraph.Add(run);
                return;
            }

            string id = AddRelationship(
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink",
                url,
                external: true);
            paragraph.Add(new XElement(W + "hyperlink", new XAttribute(R + "id", id), new XAttribute(W + "history", "1"), run));
        }

        private XElement CreateRun(
            string text,
            bool bold,
            bool italic,
            bool code,
            string? colorHex,
            bool shadeCode = true)
        {
            string latin = code ? _template.Typography.MonospaceFont : _template.Typography.LatinFont;
            string eastAsia = _template.Typography.CjkFont;
            var rPr = new XElement(W + "rPr",
                new XElement(W + "rFonts",
                    new XAttribute(W + "ascii", latin),
                    new XAttribute(W + "hAnsi", latin),
                    new XAttribute(W + "eastAsia", eastAsia)));
            if (code)
            {
                rPr.Add(
                    new XElement(W + "sz", new XAttribute(W + "val", HalfPoints(_layout.CodeFontSizePoints))),
                    new XElement(W + "szCs", new XAttribute(W + "val", HalfPoints(_layout.CodeFontSizePoints))));
            }
            if (bold) rPr.Add(new XElement(W + "b"));
            if (italic) rPr.Add(new XElement(W + "i"));
            if (code && shadeCode)
                rPr.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "fill", "F1F5F9")));
            if (!string.IsNullOrEmpty(colorHex)) rPr.Add(new XElement(W + "color", new XAttribute(W + "val", colorHex)));
            return new XElement(W + "r", rPr,
                new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));
        }

        private bool TryAddImage(XElement paragraph, string? url)
        {
            if (!TryResolveLocalImagePath(url, out string path)) return false;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            string contentType = extension switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".tif" or ".tiff" => "image/tiff",
                _ => ""
            };
            if (contentType.Length == 0) return false;

            string fileName = $"image{_images.Count + 1}{extension}";
            string id = AddRelationship(
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image",
                "media/" + fileName,
                external: false);
            _images.Add(new ImagePart(path, fileName, contentType));

            (long cx, long cy) = GetImageExtent(path);
            long docPrId = _images.Count;
            var picture = new XElement(PIC + "pic",
                new XElement(PIC + "nvPicPr",
                    new XElement(PIC + "cNvPr", new XAttribute("id", 0), new XAttribute("name", fileName)),
                    new XElement(PIC + "cNvPicPr")),
                new XElement(PIC + "blipFill",
                    new XElement(A + "blip", new XAttribute(R + "embed", id)),
                    new XElement(A + "stretch", new XElement(A + "fillRect"))),
                new XElement(PIC + "spPr",
                    new XElement(A + "xfrm",
                        new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                        new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                    new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst"))));
            var drawing = new XElement(W + "drawing",
                new XElement(WP + "inline",
                    new XElement(WP + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                    new XElement(WP + "docPr", new XAttribute("id", docPrId), new XAttribute("name", fileName)),
                    new XElement(A + "graphic",
                        new XElement(A + "graphicData",
                            new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/picture"),
                            picture))));
            paragraph.Add(new XElement(W + "r", drawing));
            return true;
        }

        private (long Cx, long Cy) GetImageExtent(string path)
        {
            using Image image = Image.FromFile(path);
            double usablePoints = UsablePageWidthPoints;
            double widthPoints = Math.Min(usablePoints, image.Width * 72d / Math.Max(image.HorizontalResolution, 96));
            double scale = widthPoints / Math.Max(1, image.Width * 72d / Math.Max(image.HorizontalResolution, 96));
            double heightPoints = image.Height * 72d / Math.Max(image.VerticalResolution, 96) * scale;
            return ((long)Math.Round(widthPoints * 12700), (long)Math.Round(heightPoints * 12700));
        }

        private double UsablePageWidthPoints =>
            (_paper == MarkdownPdfOptions.PaperLetter ? 612 : 595.28)
            - _template.Layout.EffectiveMarginLeftPoints
            - _template.Layout.EffectiveMarginRightPoints;

        private bool TryResolveLocalImagePath(string? url, out string path)
        {
            path = "";
            if (string.IsNullOrWhiteSpace(url) || IsWebUrl(url)) return false;
            string decoded = Uri.UnescapeDataString(url);
            if (Uri.TryCreate(decoded, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                decoded = uri.LocalPath;
            string candidate = Path.IsPathRooted(decoded) ? decoded : Path.Combine(_baseDirectory, decoded);
            try { candidate = Path.GetFullPath(candidate); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
            if (!File.Exists(candidate)) return false;
            path = candidate;
            return true;
        }

        private XDocument CreateStyles()
        {
            var styles = new XElement(W + "styles",
                new XAttribute(XNamespace.Xmlns + "w", W),
                CreateParagraphStyle("Normal", "Normal", _layout.BodySizePoints, _template.Palette.Body.Hex, false),
                CreateParagraphStyle("Quote", "Quote", _layout.BodySizePoints, _template.Palette.Body.Hex, false),
                CreateParagraphStyle("CodeBlock", "Code Block", _layout.CodeFontSizePoints, _template.Palette.Strong.Hex, false,
                    lineHeightPoints: _layout.CodeLineHeightPoints, afterPoints: _layout.CodeAfterPoints(_template)),
                CreateParagraphStyle("TableText", "Table Text", _layout.TableFontSizePoints, _template.Palette.Body.Hex, false,
                    lineHeightPoints: _layout.TableLineHeightPoints, afterPoints: 0),
                CreateParagraphStyle("TableHeader", "Table Header", _layout.TableFontSizePoints, _template.Palette.Body.Hex, true,
                    lineHeightPoints: _layout.TableLineHeightPoints, afterPoints: 0));

            for (int level = 1; level <= 6; level++)
            {
                double size = _layout.HeadingSizePoints(_template, level);
                string color = level == 2 && _template.Layout.AccentH2 ? _template.Palette.Accent.Hex : _template.Palette.Strong.Hex;
                styles.Add(CreateParagraphStyle(
                    $"Heading{level}", $"Heading {level}", size, color, true, keepNext: true,
                    lineHeightPoints: _layout.HeadingLineHeightPoints(_template, level),
                    afterPoints: _layout.HeadingAfterPoints(_template, level)));
            }
            styles.Add(new XElement(W + "style",
                new XAttribute(W + "type", "table"),
                new XAttribute(W + "styleId", "TableGrid"),
                new XElement(W + "name", new XAttribute(W + "val", "Table Grid")),
                new XElement(W + "tblPr",
                    new XElement(W + "tblBorders",
                        TableBorder("top"), TableBorder("left"), TableBorder("bottom"), TableBorder("right"),
                        TableBorder("insideH"), TableBorder("insideV")))));
            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), styles);
        }

        private XElement CreateParagraphStyle(
            string id, string name, double size, string color, bool bold,
            int leftIndent = 0, bool keepNext = false,
            double? lineHeightPoints = null, double? afterPoints = null)
        {
            double lineHeight = lineHeightPoints ?? _layout.BodyLineHeightPoints;
            double after = afterPoints ?? _layout.BlockGapPoints;
            var pPr = new XElement(W + "pPr",
                new XElement(W + "spacing",
                    id == "Heading1" && _layout.HeadingBeforePoints(_template, 1) > 0
                        ? new XAttribute(W + "before", PointsToTwips(_layout.HeadingBeforePoints(_template, 1)))
                        : null,
                    new XAttribute(W + "after", PointsToTwips(after)),
                    new XAttribute(W + "line", PointsToTwips(lineHeight)),
                    new XAttribute(W + "lineRule", "exact")));
            if (leftIndent > 0) pPr.Add(new XElement(W + "ind", new XAttribute(W + "left", leftIndent)));
            if (keepNext) pPr.Add(new XElement(W + "keepNext"));
            if (id == "Heading1" && _template.Layout.CenterH1)
                pPr.Add(new XElement(W + "jc", new XAttribute(W + "val", "center")));
            if (id == "Heading2" && _template.Layout.DrawH2Bar)
            {
                pPr.Add(new XElement(W + "pBdr",
                    new XElement(W + "left",
                        new XAttribute(W + "val", "single"),
                        new XAttribute(W + "sz", "18"),
                        new XAttribute(W + "space", "8"),
                        new XAttribute(W + "color", _template.Palette.Accent.Hex))));
            }

            string latinFont = id == "CodeBlock" ? _template.Typography.MonospaceFont : _template.Typography.LatinFont;
            string cjkFont = _template.Typography.CjkFont;

            var rPr = new XElement(W + "rPr",
                new XElement(W + "rFonts",
                    new XAttribute(W + "ascii", latinFont), new XAttribute(W + "hAnsi", latinFont), new XAttribute(W + "eastAsia", cjkFont)),
                new XElement(W + "color", new XAttribute(W + "val", color)),
                new XElement(W + "sz", new XAttribute(W + "val", HalfPoints(size))),
                new XElement(W + "szCs", new XAttribute(W + "val", HalfPoints(size))));
            if (bold) rPr.Add(new XElement(W + "b"));

            return new XElement(W + "style",
                new XAttribute(W + "type", "paragraph"),
                new XAttribute(W + "styleId", id),
                id == "Normal" ? new XAttribute(W + "default", "1") : null,
                new XElement(W + "name", new XAttribute(W + "val", name)), pPr, rPr);
        }

        private XElement TableBorder(string name) => new(W + name,
            new XAttribute(W + "val", "single"),
            new XAttribute(W + "sz", "4"),
            new XAttribute(W + "space", "0"),
            new XAttribute(W + "color", _template.Palette.Border.Hex));

        private XElement QuoteBorder(string name) => new(W + name,
            new XAttribute(W + "val", "single"),
            new XAttribute(W + "sz", Math.Max(2, (int)Math.Round(_template.Layout.QuoteBarWidthPoints * 8))),
            new XAttribute(W + "space", "0"),
            new XAttribute(W + "color", _resolvedPalette.QuoteBar.Hex));

        private static XElement HiddenBorder(string name) => new(W + name,
            new XAttribute(W + "val", "nil"));

        private static XElement CellMargin(string name, double points) => new(W + name,
            new XAttribute(W + "w", PointsToTwips(points)),
            new XAttribute(W + "type", "dxa"));

        private static XElement CodePaddingBorder(string name, double spacePoints, string color) => new(W + name,
            new XAttribute(W + "val", "single"),
            new XAttribute(W + "sz", "2"),
            new XAttribute(W + "space", Math.Max(0, (int)Math.Round(spacePoints))),
            new XAttribute(W + "color", color));

        private XElement ParagraphProperties(string style, int indentTwips, bool bodyParagraph = false)
        {
            var pPr = new XElement(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", style)));
            if (indentTwips > 0) pPr.Add(new XElement(W + "ind", new XAttribute(W + "left", indentTwips)));
            else if (bodyParagraph && _template.Layout.FirstLineIndentPoints > 0)
                pPr.Add(new XElement(W + "ind", new XAttribute(W + "firstLine", PointsToTwips(_template.Layout.FirstLineIndentPoints * _scale))));
            if (bodyParagraph && _template.Layout.JustifyBody)
                pPr.Add(new XElement(W + "jc", new XAttribute(W + "val", "both")));
            if (!bodyParagraph && style == "Normal" && indentTwips > 0)
                pPr.Add(new XElement(W + "spacing",
                    new XAttribute(W + "after", 0),
                    new XAttribute(W + "line", PointsToTwips(_layout.BodyLineHeightPoints)),
                    new XAttribute(W + "lineRule", "exact")));
            return pPr;
        }

        private XElement CreateSectionProperties()
        {
            int width = _paper == MarkdownPdfOptions.PaperLetter ? 12240 : 11906;
            int height = _paper == MarkdownPdfOptions.PaperLetter ? 15840 : 16838;
            return new XElement(W + "sectPr",
                new XElement(W + "pgSz", new XAttribute(W + "w", width), new XAttribute(W + "h", height)),
                new XElement(W + "pgMar",
                    new XAttribute(W + "top", PointsToTwips(_template.Layout.EffectiveMarginTopPoints)),
                    new XAttribute(W + "right", PointsToTwips(_template.Layout.EffectiveMarginRightPoints)),
                    new XAttribute(W + "bottom", PointsToTwips(_template.Layout.EffectiveMarginBottomPoints)),
                    new XAttribute(W + "left", PointsToTwips(_template.Layout.EffectiveMarginLeftPoints)),
                    new XAttribute(W + "header", 720), new XAttribute(W + "footer", 720), new XAttribute(W + "gutter", 0)));
        }

        private XDocument CreateContentTypes()
        {
            XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
            var types = new XElement(ct + "Types",
                new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/word/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")));
            if (_numberingInstances.Count > 0)
                types.Add(new XElement(ct + "Override", new XAttribute("PartName", "/word/numbering.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml")));
            foreach (ImagePart image in _images.GroupBy(i => Path.GetExtension(i.FileName), StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                types.Add(new XElement(ct + "Default", new XAttribute("Extension", Path.GetExtension(image.FileName).TrimStart('.')), new XAttribute("ContentType", image.ContentType)));
            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), types);
        }

        private XDocument CreateNumbering()
        {
            var numbering = new XElement(W + "numbering", new XAttribute(XNamespace.Xmlns + "w", W));
            numbering.Add(CreateAbstractNumbering(0, ordered: false));
            numbering.Add(CreateAbstractNumbering(1, ordered: true));
            foreach (NumberingInstance instance in _numberingInstances)
            {
                var num = new XElement(W + "num",
                    new XAttribute(W + "numId", instance.NumberingId),
                    new XElement(W + "abstractNumId", new XAttribute(W + "val", instance.Ordered ? 1 : 0)));
                if (instance.Start != 1)
                {
                    num.Add(new XElement(W + "lvlOverride",
                        new XAttribute(W + "ilvl", instance.Level),
                        new XElement(W + "startOverride", new XAttribute(W + "val", instance.Start))));
                }
                numbering.Add(num);
            }
            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), numbering);
        }

        private XElement CreateAbstractNumbering(int abstractId, bool ordered)
        {
            var abstractNum = new XElement(W + "abstractNum",
                new XAttribute(W + "abstractNumId", abstractId),
                new XElement(W + "multiLevelType", new XAttribute(W + "val", "hybridMultilevel")));
            for (int level = 0; level <= 8; level++)
            {
                int left = PointsToTwips((level + 1) * _layout.ListIndentPoints);
                string levelText = ordered ? $"%{level + 1}." : "•";
                var lvl = new XElement(W + "lvl",
                    new XAttribute(W + "ilvl", level),
                    new XElement(W + "start", new XAttribute(W + "val", 1)),
                    new XElement(W + "numFmt", new XAttribute(W + "val", ordered ? "decimal" : "bullet")),
                    new XElement(W + "lvlText", new XAttribute(W + "val", levelText)),
                    new XElement(W + "lvlJc", new XAttribute(W + "val", "left")),
                    new XElement(W + "pPr",
                        new XElement(W + "tabs", new XElement(W + "tab", new XAttribute(W + "val", "num"), new XAttribute(W + "pos", left))),
                        new XElement(W + "ind", new XAttribute(W + "left", left), new XAttribute(W + "hanging", PointsToTwips(_layout.ListIndentPoints)))));
                if (!ordered)
                {
                    lvl.Add(new XElement(W + "rPr",
                        new XElement(W + "rFonts",
                            new XAttribute(W + "ascii", "Segoe UI Symbol"),
                            new XAttribute(W + "hAnsi", "Segoe UI Symbol"))));
                }
                abstractNum.Add(lvl);
            }
            return abstractNum;
        }

        private static XDocument CreateRootRelationships()
        {
            XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(rel + "Relationships",
                    new XElement(rel + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                        new XAttribute("Target", "word/document.xml"))));
        }

        private XDocument CreateDocumentRelationships()
        {
            XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
            var root = new XElement(rel + "Relationships",
                new XElement(rel + "Relationship",
                    new XAttribute("Id", "rIdStyles"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                    new XAttribute("Target", "styles.xml")));
            if (_numberingInstances.Count > 0)
                root.Add(new XElement(rel + "Relationship",
                    new XAttribute("Id", "rIdNumbering"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering"),
                    new XAttribute("Target", "numbering.xml")));
            foreach (Relationship relationship in _relationships)
            {
                var element = new XElement(rel + "Relationship",
                    new XAttribute("Id", relationship.Id),
                    new XAttribute("Type", relationship.Type),
                    new XAttribute("Target", relationship.Target));
                if (relationship.External) element.Add(new XAttribute("TargetMode", "External"));
                root.Add(element);
            }
            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root);
        }

        private string AddRelationship(string type, string target, bool external)
        {
            string id = "rId" + _nextRelationshipId++;
            _relationships.Add(new Relationship(id, type, target, external));
            return id;
        }

        private static void WriteXml(ZipArchive archive, string name, XDocument document)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using Stream stream = entry.Open();
            var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, CloseOutput = false };
            using XmlWriter writer = XmlWriter.Create(stream, settings);
            document.Save(writer);
        }

        private static void MakeRunsBold(XElement paragraph)
        {
            foreach (XElement run in paragraph.Descendants(W + "r"))
            {
                XElement? rPr = run.Element(W + "rPr");
                if (rPr is null)
                {
                    rPr = new XElement(W + "rPr");
                    run.AddFirst(rPr);
                }
                if (rPr.Element(W + "b") is null) rPr.Add(new XElement(W + "b"));
            }
        }

        private static bool IsWebUrl(string? value) =>
            Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        private static string NormalizeDisplayGlyphs(string text) => text;

        private static string NormalizeNewlines(string value) =>
            value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        private static string StripHtml(string value)
        {
            var builder = new StringBuilder(value.Length);
            bool inside = false;
            foreach (char ch in value)
            {
                if (ch == '<') { inside = true; continue; }
                if (ch == '>') { inside = false; continue; }
                if (!inside) builder.Append(ch);
            }
            return builder.ToString().Trim();
        }

        private static string HalfPoints(double points) => Math.Max(2, (int)Math.Round(points * 2)).ToString();
        private static int PointsToTwips(double points) => Math.Max(0, (int)Math.Round(points * 20));

        private sealed record Relationship(string Id, string Type, string Target, bool External);
        private sealed record ImagePart(string SourcePath, string FileName, string ContentType);
        private sealed record NumberingInstance(int NumberingId, bool Ordered, int Level, int Start);
    }
}

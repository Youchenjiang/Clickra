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
        private readonly string _paper;
        private readonly double _scale;
        private readonly bool _lightCode;
        private readonly string _baseDirectory;
        private readonly CancellationToken _token;
        private readonly List<Relationship> _relationships = new();
        private readonly List<ImagePart> _images = new();
        private int _nextRelationshipId = 1;

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

        private void RenderBlock(Block block, XElement parent, int listDepth)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    parent.Add(CreateInlineParagraph(heading.Inline, $"Heading{Math.Clamp(heading.Level, 1, 6)}"));
                    break;
                case ParagraphBlock paragraph:
                    parent.Add(CreateInlineParagraph(paragraph.Inline, "Normal", bodyParagraph: true));
                    break;
                case QuoteBlock quote:
                    foreach (Block child in quote)
                    {
                        if (child is ParagraphBlock quoteParagraph)
                            parent.Add(CreateInlineParagraph(quoteParagraph.Inline, "Quote"));
                        else
                            RenderBlock(child, parent, listDepth);
                    }
                    break;
                case ListBlock list:
                    RenderList(list, parent, listDepth);
                    break;
                case FencedCodeBlock fenced:
                    parent.Add(CreateCodeParagraph(fenced.Lines.ToString()));
                    break;
                case CodeBlock code:
                    parent.Add(CreateCodeParagraph(code.Lines.ToString()));
                    break;
                case Table table:
                    parent.Add(CreateTable(table));
                    break;
                case ThematicBreakBlock:
                    parent.Add(CreateRuleParagraph());
                    break;
                case HtmlBlock html:
                    string text = StripHtml(html.Lines.ToString());
                    if (!string.IsNullOrWhiteSpace(text)) parent.Add(CreateTextParagraph(text, "Normal", bodyParagraph: true));
                    break;
                case ContainerBlock container:
                    foreach (Block child in container) RenderBlock(child, parent, listDepth);
                    break;
                case LeafBlock leaf when leaf.Inline is not null:
                    parent.Add(CreateInlineParagraph(leaf.Inline, "Normal"));
                    break;
            }
        }

        private void RenderList(ListBlock list, XElement parent, int depth)
        {
            int number = int.TryParse(list.OrderedStart, out int orderedStart) ? orderedStart : 1;
            foreach (Block child in list)
            {
                if (child is not ListItemBlock item) continue;
                string marker = list.IsOrdered ? $"{number++}. " : "• ";
                bool first = true;
                foreach (Block itemChild in item)
                {
                    if (itemChild is ParagraphBlock paragraph)
                    {
                        XElement p = CreateInlineParagraph(paragraph.Inline, "Normal", marker: first ? marker : null, indentTwips: (depth + 1) * 360);
                        parent.Add(p);
                        first = false;
                    }
                    else if (itemChild is ListBlock nested)
                    {
                        RenderList(nested, parent, depth + 1);
                    }
                    else
                    {
                        RenderBlock(itemChild, parent, depth + 1);
                    }
                }
            }
        }

        private XElement CreateInlineParagraph(ContainerInline? inline, string style, string? marker = null, int indentTwips = 0, bool bodyParagraph = false)
        {
            var p = new XElement(W + "p", ParagraphProperties(style, indentTwips, bodyParagraph));
            if (marker is not null) p.Add(CreateRun(marker, false, false, false, null));
            AppendInlines(p, inline, false, false, null);
            return p;
        }

        private XElement CreateTextParagraph(string text, string style, bool bodyParagraph = false) =>
            new(W + "p", ParagraphProperties(style, 0, bodyParagraph), CreateRun(text, false, false, false, null));

        private XElement CreateCodeParagraph(string code)
        {
            string background = _lightCode ? "F1F5F9" : "0F172A";
            string foreground = _lightCode ? _template.Palette.Strong.Hex : "F1F5F9";
            var pPr = ParagraphProperties("CodeBlock", 0);
            pPr.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "fill", background)));
            var p = new XElement(W + "p", pPr);
            string[] lines = NormalizeNewlines(code).TrimEnd('\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) p.Add(new XElement(W + "r", new XElement(W + "br")));
                p.Add(CreateRun(lines[i], false, false, true, foreground));
            }
            return p;
        }

        private XElement CreateTable(Table table)
        {
            var element = new XElement(W + "tbl",
                new XElement(W + "tblPr",
                    new XElement(W + "tblStyle", new XAttribute(W + "val", "TableGrid")),
                    new XElement(W + "tblW", new XAttribute(W + "w", "0"), new XAttribute(W + "type", "auto"))));

            foreach (TableRow row in table)
            {
                var tr = new XElement(W + "tr");
                foreach (TableCell cell in row)
                {
                    var tcPr = new XElement(W + "tcPr");
                    if (row.IsHeader && _template.Layout.FillTableHeader)
                        tcPr.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "fill", _template.Palette.SoftAccent.Hex)));
                    var tc = new XElement(W + "tc", tcPr);
                    bool added = false;
                    foreach (Block child in cell)
                    {
                        if (child is ParagraphBlock paragraph)
                        {
                            XElement p = CreateInlineParagraph(paragraph.Inline, "Normal");
                            if (row.IsHeader) MakeRunsBold(p);
                            tc.Add(p);
                            added = true;
                        }
                        else
                        {
                            RenderBlock(child, tc, 0);
                            added = true;
                        }
                    }
                    if (!added) tc.Add(CreateTextParagraph("", "Normal"));
                    tr.Add(tc);
                }
                element.Add(tr);
            }
            return element;
        }

        private XElement CreateRuleParagraph() =>
            new(W + "p",
                new XElement(W + "pPr",
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

        private XElement CreateRun(string text, bool bold, bool italic, bool code, string? colorHex)
        {
            double size = code ? _template.Typography.BodySizePoints * _scale * 0.92 : _template.Typography.BodySizePoints * _scale;
            string latin = code ? _template.Typography.MonospaceFont : _template.Typography.LatinFont;
            string eastAsia = _template.Typography.CjkFont;
            var rPr = new XElement(W + "rPr",
                new XElement(W + "rFonts",
                    new XAttribute(W + "ascii", latin),
                    new XAttribute(W + "hAnsi", latin),
                    new XAttribute(W + "eastAsia", eastAsia)),
                new XElement(W + "sz", new XAttribute(W + "val", HalfPoints(size))),
                new XElement(W + "szCs", new XAttribute(W + "val", HalfPoints(size))));
            if (bold) rPr.Add(new XElement(W + "b"));
            if (italic) rPr.Add(new XElement(W + "i"));
            if (code)
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
            double usablePoints = (_paper == MarkdownPdfOptions.PaperLetter ? 612 : 595.28) - (_template.Layout.MarginPoints * 2);
            double widthPoints = Math.Min(usablePoints, image.Width * 72d / Math.Max(image.HorizontalResolution, 96));
            double scale = widthPoints / Math.Max(1, image.Width * 72d / Math.Max(image.HorizontalResolution, 96));
            double heightPoints = image.Height * 72d / Math.Max(image.VerticalResolution, 96) * scale;
            return ((long)Math.Round(widthPoints * 12700), (long)Math.Round(heightPoints * 12700));
        }

        private bool TryResolveLocalImagePath(string? url, out string path)
        {
            path = "";
            if (string.IsNullOrWhiteSpace(url) || IsWebUrl(url)) return false;
            string decoded = Uri.UnescapeDataString(url);
            if (Uri.TryCreate(decoded, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                decoded = uri.LocalPath;
            string candidate = Path.IsPathRooted(decoded) ? decoded : Path.Combine(_baseDirectory, decoded);
            try { candidate = Path.GetFullPath(candidate); }
            catch { return false; }
            if (!File.Exists(candidate)) return false;
            path = candidate;
            return true;
        }

        private XDocument CreateStyles()
        {
            var styles = new XElement(W + "styles",
                new XAttribute(XNamespace.Xmlns + "w", W),
                CreateParagraphStyle("Normal", "Normal", _template.Typography.BodySizePoints * _scale, _template.Palette.Body.Hex, false),
                CreateParagraphStyle("Quote", "Quote", _template.Typography.BodySizePoints * _scale, _template.Palette.Body.Hex, false, leftIndent: 360),
                CreateParagraphStyle("CodeBlock", "Code Block", _template.Typography.BodySizePoints * _scale * 0.92, _template.Palette.Strong.Hex, false));

            for (int level = 1; level <= 6; level++)
            {
                double size = level switch
                {
                    1 => _template.Typography.Headings.H1,
                    2 => _template.Typography.Headings.H2,
                    3 => _template.Typography.Headings.H3,
                    4 => _template.Typography.Headings.H4,
                    5 => _template.Typography.Headings.H5,
                    _ => _template.Typography.Headings.H6
                } * _scale;
                string color = level == 2 && _template.Layout.AccentH2 ? _template.Palette.Accent.Hex : _template.Palette.Strong.Hex;
                styles.Add(CreateParagraphStyle($"Heading{level}", $"Heading {level}", size, color, true, keepNext: true));
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
            int leftIndent = 0, bool keepNext = false)
        {
            var pPr = new XElement(W + "pPr",
                new XElement(W + "spacing",
                    new XAttribute(W + "after", PointsToTwips(_template.Layout.BlockGapPoints * _scale)),
                    new XAttribute(W + "line", LineSpacing(size)),
                    new XAttribute(W + "lineRule", "auto")));
            if (leftIndent > 0) pPr.Add(new XElement(W + "ind", new XAttribute(W + "left", leftIndent)));
            if (keepNext) pPr.Add(new XElement(W + "keepNext"));
            if (id == "Heading1" && _template.Layout.CenterH1)
                pPr.Add(new XElement(W + "jc", new XAttribute(W + "val", "center")));
            if (id == "Quote")
            {
                pPr.Add(new XElement(W + "pBdr",
                    new XElement(W + "left",
                        new XAttribute(W + "val", "single"),
                        new XAttribute(W + "sz", Math.Max(2, (int)Math.Round(_template.Layout.QuoteBarWidthPoints * 8))),
                        new XAttribute(W + "space", "8"),
                        new XAttribute(W + "color", _template.Palette.Border.Hex))));
            }
            else if (id == "Heading2" && _template.Layout.DrawH2Bar)
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

        private XElement ParagraphProperties(string style, int indentTwips, bool bodyParagraph = false)
        {
            var pPr = new XElement(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", style)));
            if (indentTwips > 0) pPr.Add(new XElement(W + "ind", new XAttribute(W + "left", indentTwips)));
            else if (bodyParagraph && _template.Layout.FirstLineIndentPoints > 0)
                pPr.Add(new XElement(W + "ind", new XAttribute(W + "firstLine", PointsToTwips(_template.Layout.FirstLineIndentPoints))));
            return pPr;
        }

        private XElement CreateSectionProperties()
        {
            int width = _paper == MarkdownPdfOptions.PaperLetter ? 12240 : 11906;
            int height = _paper == MarkdownPdfOptions.PaperLetter ? 15840 : 16838;
            int margin = PointsToTwips(_template.Layout.MarginPoints);
            return new XElement(W + "sectPr",
                new XElement(W + "pgSz", new XAttribute(W + "w", width), new XAttribute(W + "h", height)),
                new XElement(W + "pgMar",
                    new XAttribute(W + "top", margin), new XAttribute(W + "right", margin),
                    new XAttribute(W + "bottom", margin), new XAttribute(W + "left", margin),
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
            foreach (ImagePart image in _images.GroupBy(i => Path.GetExtension(i.FileName), StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                types.Add(new XElement(ct + "Default", new XAttribute("Extension", Path.GetExtension(image.FileName).TrimStart('.')), new XAttribute("ContentType", image.ContentType)));
            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), types);
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

        private static string NormalizeDisplayGlyphs(string text) =>
            text.Replace("：", ": ", StringComparison.Ordinal).Replace('\u2794', '\u2192');

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
        private int LineSpacing(double size) => Math.Max(240, (int)Math.Round((_template.Typography.LineHeightPoints * _scale / Math.Max(size, 1)) * 240));

        private sealed record Relationship(string Id, string Type, string Target, bool External);
        private sealed record ImagePart(string SourcePath, string FileName, string ContentType);
    }
}

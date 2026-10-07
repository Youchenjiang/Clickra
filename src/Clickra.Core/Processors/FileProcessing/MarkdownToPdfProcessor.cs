using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Clickra.Core.Processors;

/// <summary>
/// Converts Markdown documents to PDF using Markdig for parsing and PDFsharp for rendering.
/// The pipeline is fully local and keeps the conversion usable from NativeAOT surfaces.
/// </summary>
public sealed class MarkdownToPdfProcessor : MultiFileProcessorBase
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
            throw new ArgumentException("Markdown to PDF converts one input file per output.", nameof(files));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is required for Markdown to PDF conversion.", nameof(outputPath));

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

        onProgress?.Invoke(15, 100, Localization.T("md_pdf_progress_parsing", Path.GetFileName(filePath)));
        string markdown = File.ReadAllText(filePath);
        MarkdownDocument document = Markdown.Parse(markdown, Pipeline);

        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke(40, 100, Localization.T("md_pdf_progress_rendering", Path.GetFileName(filePath)));

        try
        {
            if (PdfSharp.Fonts.GlobalFontSettings.FontResolver is null)
                PdfSharp.Fonts.GlobalFontSettings.FontResolver = new ClickraFontResolver();
        }
        catch (InvalidOperationException)
        {
            // PDFsharp permits setting the resolver only before its first font is created.
        }

        using var pdf = new PdfDocument();
        using var renderer = new Renderer(
            pdf,
            Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? Directory.GetCurrentDirectory(),
            options,
            cancellationToken);
        renderer.Render(document);

        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke(90, 100, Localization.T("md_pdf_progress_saving", Path.GetFileName(_outputPath!)));
        pdf.Save(_outputPath!);
    }

    protected override void OnAllFilesProcessed(
        string? outputPath,
        int totalFiles,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke(100, 100, Localization.T("md_pdf_progress_done", Path.GetFileName(outputPath ?? "")));
    }

    private sealed class Renderer : IDisposable
    {
        private const double ListIndent = 20;

        private readonly PdfDocument _document;
        private readonly string _baseDirectory;
        private readonly CancellationToken _token;
        private readonly PdfSharp.PageSize _pageSize;
        private readonly MarkdownDocumentTemplate _template;
        private readonly double _scale;
        private readonly double _marginTop;
        private readonly double _marginRight;
        private readonly double _marginBottom;
        private readonly double _marginLeft;
        private readonly double _bodySize;
        private readonly double _bodyLineHeight;
        private readonly double _blockGap;
        private readonly XColor _textColor;
        private readonly XColor _strongTextColor;
        private readonly XColor _accentColor;
        private readonly XColor _accentSoftColor;
        private readonly XColor _borderColor;
        private readonly XColor _codeBackgroundColor;
        private readonly XColor _codeTextColor;
        private readonly XColor _inlineCodeBackgroundColor;
        private readonly List<QuoteState> _activeQuotes = new();
        private PdfPage? _page;
        private XGraphics? _graphics;
        private double _y;

        public Renderer(PdfDocument document, string baseDirectory, IReadOnlyDictionary<string, object>? options, CancellationToken token)
        {
            _document = document;
            _baseDirectory = baseDirectory;
            _token = token;
            _template = MarkdownTemplateCatalog.Resolve(
                MarkdownPdfOptions.GetTheme(options),
                MarkdownPdfOptions.GetTemplatePath(options));
            _pageSize = MarkdownPdfOptions.GetPaper(options) == MarkdownPdfOptions.PaperLetter
                ? PdfSharp.PageSize.Letter
                : PdfSharp.PageSize.A4;

            _scale = MarkdownPdfOptions.GetTextSize(options) switch
            {
                MarkdownPdfOptions.TextSmall => 0.9,
                MarkdownPdfOptions.TextLarge => 1.12,
                _ => 1.0
            };
            _marginTop = _template.Layout.EffectiveMarginTopPoints;
            _marginRight = _template.Layout.EffectiveMarginRightPoints;
            _marginBottom = _template.Layout.EffectiveMarginBottomPoints;
            _marginLeft = _template.Layout.EffectiveMarginLeftPoints;
            _bodySize = _template.Typography.BodySizePoints * _scale;
            _bodyLineHeight = _template.Typography.LineHeightPoints * _scale;
            _blockGap = _template.Layout.BlockGapPoints * _scale;
            _textColor = ToXColor(_template.Palette.Body);
            _strongTextColor = ToXColor(_template.Palette.Strong);
            _accentColor = ToXColor(_template.Palette.Accent);
            _accentSoftColor = ToXColor(_template.Palette.SoftAccent);
            _borderColor = ToXColor(_template.Palette.Border);

            bool lightCode = MarkdownPdfOptions.GetCodeTheme(options) == MarkdownPdfOptions.CodeLight;
            _codeBackgroundColor = lightCode ? XColor.FromArgb(241, 245, 249) : XColor.FromArgb(15, 23, 42);
            _codeTextColor = lightCode ? XColor.FromArgb(30, 41, 59) : XColor.FromArgb(241, 245, 249);
            _inlineCodeBackgroundColor = XColor.FromArgb(241, 245, 249);
            NewPage();
        }

        public void Render(MarkdownDocument document)
        {
            List<Block> blocks = document.ToList();
            for (int i = 0; i < blocks.Count; i++)
            {
                _token.ThrowIfCancellationRequested();
                Block block = blocks[i];
                if (block is HeadingBlock heading && i + 1 < blocks.Count)
                {
                    // A level-2 section should not begin in the last ~quarter page: keeping a
                    // meaningful opening chunk together reads better than leaving the heading,
                    // intro, and first list item cramped at the bottom of the previous page.
                    double sectionReserve = heading.Level == 2 ? 180 : 0;
                    double keepWithNext = GetHeadingHeight(heading.Level) + EstimateMinimumBlockHeight(blocks[i + 1]);
                    EnsureSpace(Math.Max(sectionReserve, keepWithNext));
                }
                RenderBlock(block, 0);
            }
        }

        public void Dispose() => _graphics?.Dispose();

        private void RenderBlock(Block block, double indent, bool bodyParagraph = true)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    RenderHeading(heading, indent);
                    break;
                case ParagraphBlock paragraph:
                    RenderParagraph(paragraph, indent, bodyParagraph);
                    break;
                case ListBlock list:
                    RenderList(list, indent);
                    break;
                case QuoteBlock quote:
                    RenderQuote(quote, indent);
                    break;
                case Table table:
                    RenderTable(table, indent);
                    break;
                case FencedCodeBlock fenced:
                    RenderCodeBlock(fenced.Lines.ToString(), indent);
                    break;
                case CodeBlock code:
                    RenderCodeBlock(code.Lines.ToString(), indent);
                    break;
                case ThematicBreakBlock:
                    EnsureSpace(18);
                    _graphics!.DrawLine(XPens.LightGray, _marginLeft + indent, _y + 6, PageRight - indent, _y + 6);
                    _y += 18;
                    break;
                case HtmlBlock html:
                    RenderPlainText(StripHtml(html.Lines.ToString()), _bodySize, XFontStyleEx.Regular, new XSolidBrush(_textColor), indent, _bodyLineHeight);
                    _y += _blockGap;
                    break;
                case ContainerBlock container:
                    foreach (Block child in container) RenderBlock(child, indent, bodyParagraph);
                    break;
                case LeafBlock leaf when leaf.Inline is not null:
                    RenderInline(leaf.Inline, _bodySize, XFontStyleEx.Regular, new XSolidBrush(_textColor), indent, _bodyLineHeight);
                    _y += _blockGap;
                    break;
            }
        }

        private void RenderHeading(HeadingBlock heading, double indent)
        {
            double size = HeadingSize(heading.Level);
            double lineHeight = size * 1.35;
            EnsureSpace(lineHeight + 12);

            XBrush headingBrush = new XSolidBrush(heading.Level == 2 && _template.Layout.AccentH2 ? _accentColor : _strongTextColor);
            if (heading.Level == 2 && _template.Layout.DrawH2Bar)
            {
                _graphics!.DrawRectangle(new XSolidBrush(_accentColor), _marginLeft + indent, _y + 2, 3, lineHeight - 3);
                indent += 10;
            }
            RenderInline(heading.Inline, size, XFontStyleEx.Bold, headingBrush, indent, lineHeight,
                centerSingleLine: heading.Level == 1 && _template.Layout.CenterH1);
            _y += heading.Level <= 2 ? 12 : 7;
        }

        private void RenderParagraph(ParagraphBlock paragraph, double indent, bool bodyParagraph)
        {
            RenderInline(paragraph.Inline, _bodySize, XFontStyleEx.Regular, new XSolidBrush(_textColor), indent, _bodyLineHeight,
                bodyParagraph ? _template.Layout.FirstLineIndentPoints * _scale : 0,
                justify: bodyParagraph && _template.Layout.JustifyBody);
            _y += _blockGap;
        }

        private void RenderList(ListBlock list, double indent)
        {
            int number = int.TryParse(list.OrderedStart, out int parsed) ? parsed : 1;
            foreach (Block child in list)
            {
                if (child is not ListItemBlock item) continue;
                _token.ThrowIfCancellationRequested();
                EnsureSpace(EstimateListItemHeight(item, indent));
                string marker = list.IsOrdered ? $"{number++}." : "•";
                bool first = true;
                foreach (Block itemBlock in item)
                {
                    if (first && itemBlock is ParagraphBlock paragraph)
                    {
                        RenderListParagraph(marker, paragraph, indent);
                        first = false;
                    }
                    else
                    {
                        RenderBlock(itemBlock, indent + ListIndent, bodyParagraph: false);
                        first = false;
                    }
                }
                if (first) RenderPlainText(marker, _bodySize, XFontStyleEx.Regular, new XSolidBrush(_textColor), indent, _bodyLineHeight);
            }
            _y += 2;
        }

        private void RenderListParagraph(string marker, ParagraphBlock paragraph, double indent)
        {
            EnsureSpace(_bodyLineHeight);
            var markerFont = CreateFont(marker, _bodySize, XFontStyleEx.Regular);
            _graphics!.DrawString(marker, markerFont, new XSolidBrush(_textColor), _marginLeft + indent, _y + markerFont.Size);
            RenderInline(paragraph.Inline, _bodySize, XFontStyleEx.Regular, new XSolidBrush(_textColor), indent + ListIndent, _bodyLineHeight);
        }

        private void RenderQuote(QuoteBlock quote, double indent)
        {
            var state = new QuoteState(_marginLeft + indent, _y);
            _activeQuotes.Add(state);
            try
            {
                foreach (Block child in quote) RenderBlock(child, indent + 16, bodyParagraph: false);
                // Paragraph rendering advances by the inter-block gap after its last line.
                // Keep the quote rule aligned to the actual text line box instead of
                // extending it through that trailing whitespace.
                double contentBottom = Math.Max(_y - _blockGap, state.StartY + _bodyLineHeight);
                DrawQuoteBar(state, contentBottom);
            }
            finally
            {
                _activeQuotes.Remove(state);
            }
            _y += 2;
        }

        private void DrawQuoteBar(QuoteState state, double endY)
        {
            if (_graphics is null || endY <= state.StartY) return;
            _graphics.DrawRectangle(new XSolidBrush(_borderColor), state.X, state.StartY, _template.Layout.QuoteBarWidthPoints, endY - state.StartY);
        }

        private void CloseQuoteBarsForPage()
        {
            foreach (QuoteState state in _activeQuotes) DrawQuoteBar(state, PageBottom);
        }

        private void RenderCodeBlock(string code, double indent)
        {
            string[] lines = NormalizeNewlines(code).Split('\n');
            double available = ContentWidth - indent;
            var wrapped = new List<StyledLine>();
            foreach (string line in lines) AddCodeLine(line, available - 16, wrapped);

            bool firstLine = true;
            foreach (StyledLine line in wrapped)
            {
                double rowHeight = 23 * _scale;
                double lineAdvance = 18 * _scale;
                EnsureSpace(26 * _scale);
                if (firstLine)
                {
                    _y += 5 * _scale;
                    firstLine = false;
                }
                double drawX = _marginLeft + indent + (10 * _scale);
                double drawY = _y + line.Font.Size;
                _graphics!.DrawRectangle(new XSolidBrush(_codeBackgroundColor), _marginLeft + indent, _y - (5 * _scale), available, rowHeight);
                _graphics.DrawString(line.Text, line.Font, new XSolidBrush(_codeTextColor), drawX, drawY);
                _y += lineAdvance;
            }
            _y += _blockGap + 2;
        }

        private void AddCodeLine(string line, double maxWidth, List<StyledLine> output)
        {
            string text = line.Length == 0
                ? " "
                : NormalizeDisplayGlyphs(line.Replace("\t", "    ", StringComparison.Ordinal));
            XFont font = CreateFont(text, 9.5 * _scale, XFontStyleEx.Regular, monospace: !ContainsCjk(text));
            foreach (string wrappedLine in WrapText(text, font, maxWidth))
                output.Add(new StyledLine(wrappedLine, font));
        }

        private void RenderTable(Table table, double indent)
        {
            var rows = table.OfType<TableRow>().ToList();
            if (rows.Count == 0) return;
            int columnCount = rows.Max(row => row.Count);
            if (columnCount <= 0) return;

            double tableWidth = ContentWidth - indent;
            double columnWidth = tableWidth / columnCount;

            foreach (TableRow row in rows)
            {
                _token.ThrowIfCancellationRequested();
                List<TableCellLayout> cells = BuildTableRow(row, columnCount, columnWidth);
                RenderTableRow(cells, row.IsHeader, indent, columnWidth);
            }
            _y += _blockGap;
        }

        private List<TableCellLayout> BuildTableRow(TableRow row, int columnCount, double columnWidth)
        {
            var result = new List<TableCellLayout>(columnCount);
            for (int i = 0; i < columnCount; i++)
            {
                string text = i < row.Count && row[i] is TableCell cell
                    ? NormalizeDisplayGlyphs(ExtractBlockText(cell))
                    : "";
                XFont font = CreateFont(text, 9.5 * _scale, row.IsHeader ? XFontStyleEx.Bold : XFontStyleEx.Regular);
                List<string> lines = WrapText(text, font, columnWidth - (12 * _scale)).ToList();
                if (lines.Count == 0) lines.Add("");
                result.Add(new TableCellLayout(lines, font));
            }
            return result;
        }

        private void RenderTableRow(List<TableCellLayout> cells, bool isHeader, double indent, double columnWidth)
        {
            int lineOffset = 0;
            int maxLines = Math.Max(1, cells.Max(cell => cell.Lines.Count));
            while (lineOffset < maxLines)
            {
                double tableLineHeight = 13 * _scale;
                double tablePadding = 8 * _scale;
                double minimumRowHeight = 22 * _scale;
                EnsureSpace(minimumRowHeight);
                int availableLines = Math.Max(1, (int)Math.Floor((PageBottom - _y - tablePadding) / tableLineHeight));
                int lineCount = Math.Min(maxLines - lineOffset, availableLines);
                double rowHeight = Math.Max(minimumRowHeight, (lineCount * tableLineHeight) + tablePadding);
                DrawTableRowChunk(cells, isHeader, indent, columnWidth, lineOffset, lineCount, rowHeight);
                _y += rowHeight;
                lineOffset += lineCount;
            }
        }

        private void DrawTableRowChunk(
            List<TableCellLayout> cells,
            bool isHeader,
            double indent,
            double columnWidth,
            int lineOffset,
            int lineCount,
            double rowHeight)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                double x = _marginLeft + indent + (i * columnWidth);
                if (isHeader && _template.Layout.FillTableHeader)
                    _graphics!.DrawRectangle(new XSolidBrush(_accentSoftColor), x, _y, columnWidth, rowHeight);
                _graphics!.DrawRectangle(new XPen(_borderColor, 0.8), x, _y, columnWidth, rowHeight);
                DrawTableCellLines(cells[i], x, lineOffset, lineCount);
            }
        }

        private void DrawTableCellLines(TableCellLayout cell, double x, int lineOffset, int lineCount)
        {
            int end = Math.Min(cell.Lines.Count, lineOffset + lineCount);
            for (int lineIndex = lineOffset; lineIndex < end; lineIndex++)
            {
                int visibleIndex = lineIndex - lineOffset;
                double drawX = x + (8 * _scale);
                double drawY = _y + (6 * _scale) + cell.Font.Size + (visibleIndex * 13 * _scale);
                _graphics!.DrawString(
                    cell.Lines[lineIndex], cell.Font, new XSolidBrush(_textColor),
                    drawX, drawY);
            }
        }

        private void RenderInline(ContainerInline? inline, double size, XFontStyleEx baseStyle, XBrush baseBrush, double indent, double lineHeight, double firstLineIndent = 0, bool centerSingleLine = false, bool justify = false)
        {
            var segments = new List<InlineSegment>();
            CollectInlineSegments(inline, baseStyle.HasFlag(XFontStyleEx.Bold), baseStyle.HasFlag(XFontStyleEx.Italic), null, false, segments);
            RenderSegments(segments, size, baseBrush, indent, lineHeight, firstLineIndent, centerSingleLine, justify);
        }

        private void RenderSegments(List<InlineSegment> segments, double size, XBrush baseBrush, double indent, double lineHeight, double firstLineIndent, bool centerSingleLine = false, bool justify = false)
        {
            if (justify && segments.All(segment => segment.ImageUrl is null))
            {
                RenderJustifiedSegments(segments, size, baseBrush, indent, lineHeight, firstLineIndent);
                return;
            }

            double x = _marginLeft + indent + firstLineIndent;
            double maxX = PageRight - indent;
            if (centerSingleLine && TryMeasureInlineWidth(segments, size, out double measuredWidth) && measuredWidth <= maxX - (_marginLeft + indent))
                x = _marginLeft + indent + ((maxX - (_marginLeft + indent) - measuredWidth) / 2);
            EnsureSpace(lineHeight);

            foreach (InlineSegment segment in segments)
                RenderSegment(segment, size, baseBrush, indent, lineHeight, maxX, ref x);
            _y += lineHeight;
        }

        private void RenderJustifiedSegments(List<InlineSegment> segments, double size, XBrush baseBrush, double indent, double lineHeight, double firstLineIndent)
        {
            double lineStart = _marginLeft + indent + firstLineIndent;
            double continuationStart = _marginLeft + indent;
            double maxX = PageRight - indent;
            double narrowestLineWidth = maxX - lineStart;
            var line = new List<InlineDrawPiece>();
            double lineWidth = 0;

            void FlushLine(bool justifyLine)
            {
                if (line.Count == 0)
                {
                    EnsureSpace(lineHeight);
                    _y += lineHeight;
                    lineStart = continuationStart;
                    return;
                }

                EnsureSpace(lineHeight);
                DrawJustifiedLine(line, lineStart, maxX, lineWidth, lineHeight, justifyLine);
                _y += lineHeight;
                line.Clear();
                lineWidth = 0;
                lineStart = continuationStart;
            }

            foreach (InlineDrawPiece? piece in BuildJustifiedPieces(segments, size, baseBrush, narrowestLineWidth))
            {
                _token.ThrowIfCancellationRequested();
                if (piece is null)
                {
                    FlushLine(justifyLine: false);
                    continue;
                }

                double available = maxX - lineStart;
                if (line.Count > 0 && lineWidth + piece.Width > available)
                    FlushLine(justifyLine: true);

                line.Add(piece);
                lineWidth += piece.Width;
            }

            if (line.Count > 0)
                FlushLine(justifyLine: false);
            else if (segments.Count == 0)
            {
                EnsureSpace(lineHeight);
                _y += lineHeight;
            }
        }

        private List<InlineDrawPiece?> BuildJustifiedPieces(List<InlineSegment> segments, double size, XBrush baseBrush, double maxPieceWidth)
        {
            var pieces = new List<InlineDrawPiece?>();
            foreach (InlineSegment segment in segments)
            {
                if (segment.Text == "\n")
                {
                    pieces.Add(null);
                    continue;
                }

                string text = NormalizeDisplayGlyphs(segment.Text);
                XBrush brush = segment.Url is not null
                    ? new XSolidBrush(_accentColor)
                    : segment.Bold
                        ? new XSolidBrush(_strongTextColor)
                        : baseBrush;

                foreach (string token in TokenizeForWrapping(text))
                {
                    if (token == "\n")
                    {
                        pieces.Add(null);
                        continue;
                    }

                    XFont font = CreateFont(token, segment.Code ? size * 0.92 : size, GetInlineStyle(segment), segment.Code && !ContainsCjk(token));
                    foreach (string pieceText in BreakToken(token, font, maxPieceWidth))
                        pieces.Add(new InlineDrawPiece(pieceText, segment.Url, segment.Code, font, brush, _graphics!.MeasureString(pieceText, font).Width));
                }
            }
            return pieces;
        }

        private void DrawJustifiedLine(List<InlineDrawPiece> pieces, double lineStart, double maxX, double lineWidth, double lineHeight, bool justifyLine)
        {
            int gapCount = 0;
            if (justifyLine)
            {
                for (int i = 0; i + 1 < pieces.Count; i++)
                    if (CanExpandGap(pieces[i].Text, pieces[i + 1].Text)) gapCount++;
            }

            double extraPerGap = gapCount > 0 ? Math.Max(0, maxX - lineStart - lineWidth) / gapCount : 0;
            double x = lineStart;
            for (int i = 0; i < pieces.Count; i++)
            {
                InlineDrawPiece piece = pieces[i];
                double top = _y;
                if (piece.IsCode && !string.IsNullOrWhiteSpace(piece.Text))
                    _graphics!.DrawRectangle(new XSolidBrush(_inlineCodeBackgroundColor), x - 2, _y + 1, piece.Width + 4, lineHeight - 3);
                _graphics!.DrawString(piece.Text, piece.Font, piece.Brush, x, _y + piece.Font.Size);
                if (piece.Url is not null && IsWebUrl(piece.Url))
                {
                    _graphics.DrawLine(new XPen(_accentColor, 0.8), x, _y + piece.Font.Size + 2, x + piece.Width, _y + piece.Font.Size + 2);
                    _page!.AddWebLink(new PdfRectangle(new XRect(x, top, piece.Width, lineHeight)), piece.Url);
                }
                x += piece.Width;
                if (i + 1 < pieces.Count && CanExpandGap(piece.Text, pieces[i + 1].Text))
                    x += extraPerGap;
            }
        }

        private static bool CanExpandGap(string left, string right)
        {
            if (left.Length == 0 || right.Length == 0) return false;
            if (char.IsWhiteSpace(left[^1]) || char.IsWhiteSpace(right[0])) return true;
            return IsCjk(left[^1]) && IsCjk(right[0]);
        }

        private bool TryMeasureInlineWidth(List<InlineSegment> segments, double size, out double width)
        {
            width = 0;
            foreach (InlineSegment segment in segments)
            {
                if (segment.Text == "\n" || segment.ImageUrl is not null) return false;
                string text = NormalizeDisplayGlyphs(segment.Text);
                foreach (string token in TokenizeForWrapping(text))
                {
                    if (token == "\n") return false;
                    XFont font = CreateFont(token, segment.Code ? size * 0.92 : size, GetInlineStyle(segment), segment.Code && !ContainsCjk(token));
                    width += _graphics!.MeasureString(token, font).Width;
                }
            }
            return true;
        }

        private void RenderSegment(InlineSegment segment, double size, XBrush baseBrush, double indent, double lineHeight, double maxX, ref double x)
        {
            _token.ThrowIfCancellationRequested();
            if (segment.Text == "\n")
            {
                NewLine(ref x, indent, lineHeight);
                return;
            }
            if (segment.ImageUrl is not null && TryRenderInlineImage(segment.ImageUrl, indent, ref x, lineHeight)) return;

            string text = NormalizeDisplayGlyphs(segment.Text);
            XBrush brush = segment.Url is not null
                ? new XSolidBrush(_accentColor)
                : segment.Bold
                    ? new XSolidBrush(_strongTextColor)
                    : baseBrush;
            foreach (string token in TokenizeForWrapping(text))
            {
                XFont font = token == "\n"
                    ? CreateFont(string.Empty, segment.Code ? size * 0.92 : size, GetInlineStyle(segment), segment.Code)
                    : CreateFont(token, segment.Code ? size * 0.92 : size, GetInlineStyle(segment), segment.Code && !ContainsCjk(token));
                RenderInlineToken(token, segment.Url, segment.Code, font, brush, indent, lineHeight, maxX, ref x);
            }
        }

        private static string NormalizeDisplayGlyphs(string text) =>
            text.Replace("：", ": ", StringComparison.Ordinal).Replace('\u2794', '\u2192');

        private void RenderInlineToken(string token, string? url, bool isCode, XFont font, XBrush brush, double indent, double lineHeight, double maxX, ref double x)
        {
            if (token == "\n")
            {
                NewLine(ref x, indent, lineHeight);
                return;
            }

            foreach (string piece in BreakToken(token, font, maxX - (_marginLeft + indent)))
                DrawInlinePiece(piece, url, isCode, font, brush, indent, lineHeight, maxX, ref x);
        }

        private void DrawInlinePiece(string piece, string? url, bool isCode, XFont font, XBrush brush, double indent, double lineHeight, double maxX, ref double x)
        {
            double width = _graphics!.MeasureString(piece, font).Width;
            if (x > _marginLeft + indent && x + width > maxX) NewLine(ref x, indent, lineHeight);

            double top = _y;
            if (isCode && !string.IsNullOrWhiteSpace(piece))
                _graphics.DrawRectangle(new XSolidBrush(_inlineCodeBackgroundColor), x - 2, _y + 1, width + 4, lineHeight - 3);
            _graphics.DrawString(piece, font, brush, x, _y + font.Size);
            if (url is not null && IsWebUrl(url))
            {
                _graphics.DrawLine(new XPen(_accentColor, 0.8), x, _y + font.Size + 2, x + width, _y + font.Size + 2);
                _page!.AddWebLink(new PdfRectangle(new XRect(x, top, width, lineHeight)), url);
            }
            x += width;
        }

        private static XFontStyleEx GetInlineStyle(InlineSegment segment) =>
            segment.Bold
                ? (segment.Italic ? XFontStyleEx.BoldItalic : XFontStyleEx.Bold)
                : (segment.Italic ? XFontStyleEx.Italic : XFontStyleEx.Regular);

        private bool TryRenderInlineImage(string url, double indent, ref double x, double lineHeight)
        {
            if (!TryResolveLocalImagePath(url, out string imagePath)) return false;

            XImage image;
            try
            {
                image = XImage.FromFile(imagePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                return false;
            }

            using (image)
            {
                double lineStart = _marginLeft + indent;
                if (x > lineStart) NewLine(ref x, indent, lineHeight);

                double availableWidth = ContentWidth - indent;
                double width = Math.Min(availableWidth, image.PointWidth);
                double scale = width / image.PointWidth;
                double height = image.PointHeight * scale;
                double maxHeight = PageBottom - _marginTop;
                if (height > maxHeight)
                {
                    scale = maxHeight / image.PointHeight;
                    height = image.PointHeight * scale;
                    width = image.PointWidth * scale;
                }

                EnsureSpace(height);
                _graphics!.DrawImage(image, lineStart, _y, width, height);
                _y += height;
                x = lineStart;
                return true;
            }
        }

        private bool TryResolveLocalImagePath(string url, out string imagePath)
        {
            imagePath = "";
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? absolute))
            {
                if (!absolute.IsFile) return false;
                imagePath = absolute.LocalPath;
            }
            else
            {
                imagePath = Path.GetFullPath(Path.Combine(_baseDirectory, Uri.UnescapeDataString(url.Replace('/', Path.DirectorySeparatorChar))));
            }

            return File.Exists(imagePath);
        }

        private void RenderPlainText(string text, double size, XFontStyleEx style, XBrush brush, double indent, double lineHeight)
        {
            var segments = new List<InlineSegment> { new(text, style.HasFlag(XFontStyleEx.Bold), style.HasFlag(XFontStyleEx.Italic), false, null) };
            RenderSegments(segments, size, brush, indent, lineHeight, 0);
        }

        private void NewLine(ref double x, double indent, double lineHeight)
        {
            _y += lineHeight;
            EnsureSpace(lineHeight);
            x = _marginLeft + indent;
        }

        private void EnsureSpace(double requiredHeight)
        {
            if (_page is null || _graphics is null || _y + requiredHeight > PageBottom)
                NewPage();
        }

        private double GetHeadingHeight(int level)
        {
            double size = HeadingSize(level);
            return (size * 1.35) + (level <= 2 ? 12 : 7);
        }

        private double HeadingSize(int level)
        {
            double baseSize = level switch
            {
                1 => _template.Typography.Headings.H1,
                2 => _template.Typography.Headings.H2,
                3 => _template.Typography.Headings.H3,
                4 => _template.Typography.Headings.H4,
                5 => _template.Typography.Headings.H5,
                _ => _template.Typography.Headings.H6
            };
            return baseSize * _scale;
        }

        private double EstimateMinimumBlockHeight(Block block) => block switch
        {
            ParagraphBlock => _bodyLineHeight * 2,
            ListBlock => _bodyLineHeight * 2,
            QuoteBlock => _bodyLineHeight * 2,
            Table => 44,
            FencedCodeBlock => 42,
            CodeBlock => 42,
            HeadingBlock heading => GetHeadingHeight(heading.Level),
            _ => _bodyLineHeight
        };

        private double EstimateListItemHeight(ListItemBlock item, double indent)
        {
            if (item.Count != 1 || item[0] is not ParagraphBlock paragraph || paragraph.Inline is null)
                return _bodyLineHeight * 2;

            string text = ExtractInlineText(paragraph.Inline);
            XFont font = CreateFont(text, _bodySize, XFontStyleEx.Regular);
            int lines = Math.Max(1, WrapText(text, font, ContentWidth - indent - ListIndent).Count());
            return Math.Min(lines * _bodyLineHeight, PageBottom - _marginTop);
        }

        private void NewPage()
        {
            if (_page is not null && _graphics is not null) CloseQuoteBarsForPage();
            _graphics?.Dispose();
            _page = _document.AddPage();
            _page.Size = _pageSize;
            _graphics = XGraphics.FromPdfPage(_page);
            _y = _marginTop;
            foreach (QuoteState state in _activeQuotes) state.StartY = _y;
        }

        private double ContentWidth => _page!.Width.Point - _marginLeft - _marginRight;
        private double PageRight => _page!.Width.Point - _marginRight;
        private double PageBottom => _page!.Height.Point - _marginBottom;

        private XFont CreateFont(string text, double size, XFontStyleEx style, bool monospace = false)
        {
            string family = monospace ? _template.Typography.MonospaceFont : SelectFontFamily(text);
            return new XFont(family, size, style);
        }

        private string SelectFontFamily(string text)
        {
            foreach (char ch in text)
            {
                if (ch is >= '\u3040' and <= '\u30ff') return "MS Gothic";
                if (ch is >= '\uac00' and <= '\ud7af') return "Malgun Gothic";
                if (IsCjkTypographyChar(ch)) return _template.Typography.CjkFont;
            }
            return _template.Typography.LatinFont;
        }

        private static XColor ToXColor(MarkdownThemeColor color) =>
            XColor.FromArgb(color.R, color.G, color.B);

        private IEnumerable<string> WrapText(string text, XFont font, double maxWidth)
        {
            var line = new StringBuilder();
            foreach (string token in TokenizeForWrapping(text))
            {
                if (token == "\n")
                {
                    yield return line.ToString();
                    line.Clear();
                    continue;
                }

                foreach (string piece in BreakToken(token, font, maxWidth))
                {
                    string candidate = line + piece;
                    if (line.Length > 0 && _graphics!.MeasureString(candidate, font).Width > maxWidth)
                    {
                        yield return line.ToString();
                        line.Clear();
                    }
                    line.Append(piece);
                }
            }
            if (line.Length > 0) yield return line.ToString();
        }

        private IEnumerable<string> BreakToken(string token, XFont font, double maxWidth)
        {
            if (_graphics!.MeasureString(token, font).Width <= maxWidth)
            {
                yield return token;
                yield break;
            }

            var part = new StringBuilder();
            foreach (char ch in token)
            {
                string candidate = part.ToString() + ch;
                if (part.Length > 0 && _graphics.MeasureString(candidate, font).Width > maxWidth)
                {
                    yield return part.ToString();
                    part.Clear();
                }
                part.Append(ch);
            }
            if (part.Length > 0) yield return part.ToString();
        }

        private static IEnumerable<string> TokenizeForWrapping(string text)
        {
            text = NormalizeNewlines(text);
            var current = new StringBuilder();
            foreach (char ch in text)
            {
                if (ch == '\n')
                {
                    if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                    yield return "\n";
                }
                else if (char.IsWhiteSpace(ch))
                {
                    current.Append(ch);
                    yield return current.ToString();
                    current.Clear();
                }
                else if (IsCjkTypographyChar(ch))
                {
                    if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                    yield return ch.ToString();
                }
                else
                {
                    current.Append(ch);
                }
            }
            if (current.Length > 0) yield return current.ToString();
        }

        private static bool IsCjk(char ch) =>
            ch is >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u9fff' or >= '\uac00' and <= '\ud7af';

        private static bool IsFullWidth(char ch) => ch is >= '\uff00' and <= '\uffef';

        private static bool IsCjkTypographyChar(char ch) =>
            IsCjk(ch) ||
            ch is >= '\u2e80' and <= '\u303f' or
                >= '\u31c0' and <= '\u31ef' or
                >= '\ufe10' and <= '\ufe1f' or
                >= '\ufe30' and <= '\ufe4f' ||
            IsFullWidth(ch);

        private static bool ContainsCjk(string text) => text.Any(IsCjkTypographyChar);

        private static bool IsWebUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" or "mailto";

        private static void CollectInlineSegments(
            ContainerInline? container,
            bool bold,
            bool italic,
            string? url,
            bool code,
            List<InlineSegment> output)
        {
            if (container is null) return;
            for (Inline? inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        output.Add(new InlineSegment(literal.Content.ToString(), bold, italic, code, url));
                        break;
                    case CodeInline codeInline:
                        output.Add(new InlineSegment(codeInline.Content, bold, italic, true, url));
                        break;
                    case LineBreakInline:
                        output.Add(new InlineSegment("\n", bold, italic, code, url));
                        break;
                    case EmphasisInline emphasis:
                        CollectInlineSegments(emphasis, bold || emphasis.DelimiterCount >= 2, italic || emphasis.DelimiterCount == 1, url, code, output);
                        break;
                    case LinkInline link when link.IsImage:
                        output.Add(new InlineSegment(ExtractInlineText(link), bold, italic, code, null, link.Url));
                        break;
                    case LinkInline link:
                        CollectInlineSegments(link, bold, italic, link.Url, code, output);
                        break;
                    case HtmlInline html:
                        string stripped = StripHtml(html.Tag);
                        if (!string.IsNullOrWhiteSpace(stripped)) output.Add(new InlineSegment(stripped, bold, italic, code, url));
                        break;
                    case ContainerInline nested:
                        CollectInlineSegments(nested, bold, italic, url, code, output);
                        break;
                }
            }
        }

        private static string ExtractBlockText(ContainerBlock block)
        {
            var builder = new StringBuilder();
            foreach (Block child in block)
            {
                if (child is LeafBlock leaf && leaf.Inline is not null)
                {
                    if (builder.Length > 0) builder.Append(' ');
                    builder.Append(ExtractInlineText(leaf.Inline));
                }
                else if (child is ContainerBlock nested)
                {
                    string nestedText = ExtractBlockText(nested);
                    if (!string.IsNullOrWhiteSpace(nestedText))
                    {
                        if (builder.Length > 0) builder.Append(' ');
                        builder.Append(nestedText);
                    }
                }
            }
            return builder.ToString();
        }

        private static string ExtractInlineText(ContainerInline container)
        {
            var segments = new List<InlineSegment>();
            CollectInlineSegments(container, false, false, null, false, segments);
            return string.Concat(segments.Select(segment => segment.Text));
        }

        private static string StripHtml(string value) =>
            Regex.Replace(value, "<[^>]+>", "", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Trim();

        private static string NormalizeNewlines(string value) =>
            value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        private sealed record InlineSegment(string Text, bool Bold, bool Italic, bool Code, string? Url, string? ImageUrl = null);
        private sealed record InlineDrawPiece(string Text, string? Url, bool IsCode, XFont Font, XBrush Brush, double Width);
        private sealed record StyledLine(string Text, XFont Font);
        private sealed record TableCellLayout(List<string> Lines, XFont Font);
        private sealed class QuoteState(double x, double startY)
        {
            public double X { get; } = x;
            public double StartY { get; set; } = startY;
        }
    }
}

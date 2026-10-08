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

    /// <summary>Processes the supplied Markdown files with the configured conversion options.</summary>
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

    /// <summary>Renders one Markdown input file into the target PDF document.</summary>
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

    /// <summary>Finalizes the completed Markdown-to-PDF conversion batch.</summary>
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
        private readonly PdfDocument _document;
        private readonly string _baseDirectory;
        private readonly CancellationToken _token;
        private readonly PdfSharp.PageSize _pageSize;
        private readonly MarkdownDocumentTemplate _template;
        private readonly MarkdownResolvedLayout _layout;
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
        private readonly XColor _surfaceColor;
        private readonly XColor _quoteBarColor;
        private readonly XColor _codeBackgroundColor;
        private readonly XColor _codeTextColor;
        private readonly XColor _inlineCodeBackgroundColor;
        private readonly List<QuoteState> _activeQuotes = new();
        private readonly List<QuoteSurfaceFragment> _quoteSurfaceFragments = new();
        private PdfPage? _page;
        private XGraphics? _graphics;
        private double _y;

        /// <summary>Initializes a PDF renderer with resolved Markdown presentation options and cancellation state.</summary>
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
            _layout = MarkdownResolvedLayout.Create(_template, _scale);
            _marginTop = _template.Layout.EffectiveMarginTopPoints;
            _marginRight = _template.Layout.EffectiveMarginRightPoints;
            _marginBottom = _template.Layout.EffectiveMarginBottomPoints;
            _marginLeft = _template.Layout.EffectiveMarginLeftPoints;
            _bodySize = _layout.BodySizePoints;
            _bodyLineHeight = _layout.BodyLineHeightPoints;
            _blockGap = _layout.BlockGapPoints;
            _textColor = ToXColor(_template.Palette.Body);
            _strongTextColor = ToXColor(_template.Palette.Strong);
            _accentColor = ToXColor(_template.Palette.Accent);
            _accentSoftColor = ToXColor(_template.Palette.SoftAccent);
            _borderColor = ToXColor(_template.Palette.Border);
            MarkdownResolvedPalette resolvedPalette = MarkdownResolvedPalette.Create(_template);
            _surfaceColor = ToXColor(resolvedPalette.Surface);
            _quoteBarColor = ToXColor(resolvedPalette.QuoteBar);

            bool lightCode = MarkdownPdfOptions.GetCodeTheme(options) == MarkdownPdfOptions.CodeLight;
            _codeBackgroundColor = lightCode ? XColor.FromArgb(241, 245, 249) : XColor.FromArgb(15, 23, 42);
            _codeTextColor = lightCode ? XColor.FromArgb(30, 41, 59) : XColor.FromArgb(241, 245, 249);
            _inlineCodeBackgroundColor = XColor.FromArgb(241, 245, 249);
            NewPage();
        }

        /// <summary>Renders every block in a parsed Markdown document into the target PDF.</summary>
        public void Render(MarkdownDocument document)
        {
            List<Block> blocks = document.ToList();
            for (int i = 0; i < blocks.Count; i++)
            {
                _token.ThrowIfCancellationRequested();
                Block block = blocks[i];
                if (block is HeadingBlock heading && i + 1 < blocks.Count)
                {
                    double keepWithNext = GetHeadingHeight(heading.Level) + EstimateMinimumBlockHeight(blocks[i + 1]);
                    EnsureSpace(keepWithNext);
                }
                RenderBlock(block, 0);
            }
            DrawPendingQuoteSurfaces();
        }

        /// <summary>Releases the active PDF graphics context.</summary>
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
                    double ruleHeight = _layout.RuleBlockHeightPoints(_template);
                    EnsureSpace(ruleHeight);
                    _graphics!.DrawLine(XPens.LightGray, _marginLeft + indent, _y + 6, PageRight - indent, _y + 6);
                    _y += ruleHeight;
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
                default:
                    // Unknown Markdig blocks intentionally render no output.
                    break;
            }
        }

        private void RenderHeading(HeadingBlock heading, double indent)
        {
            double size = _layout.HeadingSizePoints(_template, heading.Level);
            double lineHeight = _layout.HeadingLineHeightPoints(_template, heading.Level);
            double before = _layout.HeadingBeforePoints(_template, heading.Level);
            EnsureSpace(before + lineHeight + 12);
            _y += before;

            XBrush headingBrush = new XSolidBrush(heading.Level == 2 && _template.Layout.AccentH2 ? _accentColor : _strongTextColor);
            if (heading.Level == 2 && _template.Layout.DrawH2Bar)
            {
                double barY = _template.Id == MarkdownPdfOptions.ThemeDefault ? _y - 6 : _y + 2;
                double barHeight = _template.Id == MarkdownPdfOptions.ThemeDefault ? 24 * _scale : lineHeight - 3;
                _graphics!.DrawRectangle(new XSolidBrush(_accentColor), _marginLeft + indent, barY, 3, barHeight);
                indent += 10;
            }
            RenderInline(heading.Inline, size, XFontStyleEx.Bold, headingBrush, indent, lineHeight,
                centerSingleLine: heading.Level == 1 && _template.Layout.CenterH1);
            _y += _layout.HeadingAfterPoints(_template, heading.Level);
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
                        RenderBlock(itemBlock, indent + _layout.ListIndentPoints, bodyParagraph: false);
                        first = false;
                    }
                }
                if (first) RenderPlainText(marker, _bodySize, XFontStyleEx.Regular, new XSolidBrush(_textColor), indent, _bodyLineHeight);
            }
            _y += _layout.ListAfterPoints;
        }

        private void RenderListParagraph(string marker, ParagraphBlock paragraph, double indent)
        {
            EnsureSpace(_bodyLineHeight);
            var markerFont = CreateFont(marker, _bodySize, XFontStyleEx.Regular);
            _graphics!.DrawString(marker, markerFont, new XSolidBrush(_textColor), _marginLeft + indent, _y + markerFont.Size);
            RenderInline(paragraph.Inline, _bodySize, XFontStyleEx.Regular, new XSolidBrush(_textColor), indent + _layout.ListIndentPoints, _bodyLineHeight);
        }

        private void RenderQuote(QuoteBlock quote, double indent)
        {
            double estimatedBackgroundHeight = EstimateQuoteBackgroundHeight(quote, indent);
            EnsureSpace(estimatedBackgroundHeight + _blockGap);
            double quoteStart = _y;
            _y += _layout.QuoteVerticalPaddingPoints;
            var state = new QuoteState(_marginLeft + indent, quoteStart);
            _activeQuotes.Add(state);
            try
            {
                foreach (Block child in quote) RenderBlock(child, indent + _layout.QuoteIndentPoints, bodyParagraph: false);
                // Paragraph rendering advances by the inter-block gap after its last line.
                // Keep the quote rule aligned to the actual text line box instead of
                // extending it through that trailing whitespace.
                double contentBottom = Math.Max(_y - _blockGap, state.StartY + _bodyLineHeight);
                _y += _layout.QuoteVerticalPaddingPoints;
                double endY = Math.Max(contentBottom + _layout.QuoteVerticalPaddingPoints, state.StartY + _bodyLineHeight);
                DrawQuoteBar(state, endY);
                RecordQuoteSurface(state, endY);
            }
            finally
            {
                _activeQuotes.Remove(state);
            }
        }

        private void DrawQuoteBar(QuoteState state, double endY)
        {
            if (_graphics is null || endY <= state.StartY) return;
            _graphics.DrawRectangle(new XSolidBrush(_quoteBarColor), state.X, state.StartY, _template.Layout.QuoteBarWidthPoints, endY - state.StartY);
        }

        private double EstimateQuoteBackgroundHeight(QuoteBlock quote, double indent)
        {
            double height = _layout.QuoteVerticalPaddingPoints * 2;
            foreach (Block child in quote)
                height += EstimateBlockAdvance(child, indent + _layout.QuoteIndentPoints);
            return Math.Max(height, _bodyLineHeight + (_layout.QuoteVerticalPaddingPoints * 2));
        }

        private double EstimateBlockAdvance(Block block, double indent) =>
            EstimateBlockVisualHeight(block, indent) + EstimateTrailingSpacing(block);

        private double EstimateTrailingSpacing(Block block) => block switch
        {
            ParagraphBlock => _blockGap,
            HtmlBlock => _blockGap,
            Table => _blockGap,
            FencedCodeBlock => _layout.CodeAfterPoints(_template),
            CodeBlock => _layout.CodeAfterPoints(_template),
            ListBlock => _layout.ListAfterPoints,
            HeadingBlock heading => _layout.HeadingAfterPoints(_template, heading.Level),
            _ => 0
        };

        private double EstimateBlockVisualHeight(Block block, double indent) => block switch
        {
            ParagraphBlock paragraph when paragraph.Inline is not null =>
                EstimateInlineHeight(paragraph.Inline, _bodySize, indent, _bodyLineHeight),
            FencedCodeBlock fenced => EstimateCodeVisualHeight(fenced.Lines.ToString(), indent),
            CodeBlock code => EstimateCodeVisualHeight(code.Lines.ToString(), indent),
            Table table => EstimateTableVisualHeight(table, indent),
            ListBlock list => EstimateListVisualHeight(list, indent),
            QuoteBlock nested => EstimateQuoteBackgroundHeight(nested, indent),
            HeadingBlock heading => _layout.HeadingBeforePoints(_template, heading.Level)
                + _layout.HeadingLineHeightPoints(_template, heading.Level),
            ThematicBreakBlock => _layout.RuleBlockHeightPoints(_template),
            HtmlBlock html => EstimatePlainTextVisualHeight(StripHtml(html.Lines.ToString()), indent),
            ContainerBlock container => container.Sum(child => EstimateBlockAdvance(child, indent)),
            LeafBlock leaf when leaf.Inline is not null => EstimateInlineHeight(leaf.Inline, _bodySize, indent, _bodyLineHeight),
            _ => _bodyLineHeight
        };

        private double EstimatePlainTextVisualHeight(string text, double indent)
        {
            var inline = new List<InlineSegment> { new(text, false, false, false, null) };
            return EstimateSegmentsHeight(inline, _bodySize, indent, _bodyLineHeight);
        }

        private double EstimateCodeVisualHeight(string code, double indent)
        {
            string[] lines = NormalizeNewlines(code).Split('\n');
            double maxWidth = ContentWidth - indent - (_layout.CodeHorizontalPaddingPoints * 2);
            var wrapped = new List<CodeLineLayout>();
            foreach (string line in lines) AddCodeLine(line, maxWidth, wrapped);
            int lineCount = Math.Max(1, wrapped.Count);
            return (_layout.CodeVerticalPaddingPoints * 2) + (lineCount * _layout.CodeLineHeightPoints);
        }

        private double EstimateTableVisualHeight(Table table, double indent)
        {
            List<TableRow> rows = table.OfType<TableRow>().ToList();
            if (rows.Count == 0) return 0;
            int columnCount = rows.Max(row => row.Count);
            if (columnCount <= 0) return 0;

            double tableWidth = ContentWidth - indent;
            IReadOnlyList<double> fractions = MarkdownTableColumnSizer.ResolveFractions(table, columnCount);
            double[] columnWidths = fractions.Select(fraction => tableWidth * fraction).ToArray();
            double total = 0;
            foreach (TableRow row in rows)
            {
                List<TableCellLayout> cells = BuildTableRow(row, columnCount, columnWidths);
                int maxLines = Math.Max(1, cells.Max(cell => cell.Lines.Count));
                total += (maxLines * _layout.TableLineHeightPoints) + (_layout.TableVerticalPaddingPoints * 2);
            }
            return total;
        }

        private double EstimateListVisualHeight(ListBlock list, double indent)
        {
            double total = 0;
            foreach (Block child in list)
            {
                if (child is not ListItemBlock item) continue;
                bool first = true;
                foreach (Block itemBlock in item)
                {
                    if (first && itemBlock is ParagraphBlock paragraph && paragraph.Inline is not null)
                    {
                        total += EstimateInlineHeight(
                            paragraph.Inline,
                            _bodySize,
                            indent + _layout.ListIndentPoints,
                            _bodyLineHeight);
                    }
                    else
                    {
                        total += EstimateBlockAdvance(itemBlock, indent + _layout.ListIndentPoints);
                    }
                    first = false;
                }
                if (first) total += _bodyLineHeight;
            }
            return total;
        }

        private double EstimateInlineHeight(ContainerInline? inline, double size, double indent, double lineHeight)
        {
            var segments = new List<InlineSegment>();
            CollectInlineSegments(inline, false, false, null, false, segments);
            return EstimateSegmentsHeight(segments, size, indent, lineHeight);
        }

        private double EstimateSegmentsHeight(List<InlineSegment> segments, double size, double indent, double lineHeight)
        {
            double maxWidth = ContentWidth - indent;
            double x = 0;
            double height = 0;

            foreach (InlineSegment segment in segments)
            {
                if (segment.Text == "\n")
                {
                    height += lineHeight;
                    x = 0;
                    continue;
                }

                if (segment.ImageUrl is not null && TryMeasureInlineImage(segment.ImageUrl, maxWidth, out double imageHeight))
                {
                    if (x > 0) height += lineHeight;
                    height += imageHeight;
                    x = 0;
                    continue;
                }

                MeasureWrappedSegment(segment, size, maxWidth, lineHeight, ref x, ref height);
            }

            return height + lineHeight;
        }

        private void MeasureWrappedSegment(
            InlineSegment segment,
            double size,
            double maxWidth,
            double lineHeight,
            ref double x,
            ref double height)
        {
            string text = NormalizeDisplayGlyphs(segment.Text);
            foreach (string token in TokenizeForWrapping(text))
            {
                if (token == "\n")
                {
                    height += lineHeight;
                    x = 0;
                    continue;
                }

                XFont font = CreateFont(
                    token,
                    segment.Code ? _layout.CodeFontSizePoints : size,
                    GetInlineStyle(segment),
                    segment.Code && !ContainsCjk(token));
                foreach (string piece in BreakToken(token, font, maxWidth))
                {
                    double width = _graphics!.MeasureString(piece, font).Width;
                    if (x > 0 && x + width > maxWidth)
                    {
                        height += lineHeight;
                        x = 0;
                    }
                    x += width;
                }
            }
        }

        private bool TryMeasureInlineImage(string url, double availableWidth, out double height)
        {
            height = 0;
            if (!TryResolveLocalImagePath(url, out string imagePath)) return false;
            try
            {
                using XImage image = XImage.FromFile(imagePath);
                double width = Math.Min(availableWidth, image.PointWidth);
                double scale = width / image.PointWidth;
                height = image.PointHeight * scale;
                double maxHeight = PageBottom - _marginTop;
                if (height > maxHeight) height = maxHeight;
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        private void CloseQuoteBarsForPage()
        {
            foreach (QuoteState state in _activeQuotes)
            {
                DrawQuoteBar(state, PageBottom);
                RecordQuoteSurface(state, PageBottom);
            }
        }

        private void RecordQuoteSurface(QuoteState state, double endY)
        {
            if (_template.Id != MarkdownPdfOptions.ThemeDefault || _page is null || endY <= state.StartY) return;
            _quoteSurfaceFragments.Add(new QuoteSurfaceFragment(
                _page,
                state.X,
                state.StartY,
                PageRight - state.X,
                endY - state.StartY));
        }

        private void DrawPendingQuoteSurfaces()
        {
            if (_quoteSurfaceFragments.Count == 0) return;

            _graphics?.Dispose();
            _graphics = null;
            foreach (IGrouping<PdfPage, QuoteSurfaceFragment> pageFragments in _quoteSurfaceFragments.GroupBy(fragment => fragment.Page))
            {
                using XGraphics background = XGraphics.FromPdfPage(pageFragments.Key, XGraphicsPdfPageOptions.Prepend);
                foreach (QuoteSurfaceFragment fragment in pageFragments
                    .OrderBy(fragment => fragment.X)
                    .ThenByDescending(fragment => fragment.Width)
                    .ThenBy(fragment => fragment.Y))
                {
                    background.DrawRectangle(
                        new XPen(_borderColor, 0.8),
                        new XSolidBrush(_surfaceColor),
                        fragment.X,
                        fragment.Y,
                        fragment.Width,
                        fragment.Height);
                }
            }
            _quoteSurfaceFragments.Clear();
        }

        private void RenderCodeBlock(string code, double indent)
        {
            string[] lines = NormalizeNewlines(code).Split('\n');
            double available = ContentWidth - indent;
            var wrapped = new List<CodeLineLayout>();
            foreach (string line in lines) AddCodeLine(line, available - (_layout.CodeHorizontalPaddingPoints * 2), wrapped);

            int lineIndex = 0;
            while (lineIndex < wrapped.Count)
            {
                double availableHeight = PageBottom - _y - (_layout.CodeVerticalPaddingPoints * 2);
                int fragmentLineCount = (int)Math.Floor(availableHeight / _layout.CodeLineHeightPoints);
                if (fragmentLineCount <= 0)
                {
                    NewPage();
                    continue;
                }

                fragmentLineCount = Math.Min(fragmentLineCount, wrapped.Count - lineIndex);
                double fragmentHeight = (_layout.CodeVerticalPaddingPoints * 2)
                    + (fragmentLineCount * _layout.CodeLineHeightPoints);
                _graphics!.DrawRectangle(
                    new XSolidBrush(_codeBackgroundColor),
                    _marginLeft + indent,
                    _y,
                    available,
                    fragmentHeight);

                _y += _layout.CodeVerticalPaddingPoints;
                double drawX = _marginLeft + indent + _layout.CodeHorizontalPaddingPoints;
                for (int i = 0; i < fragmentLineCount; i++)
                {
                    CodeLineLayout line = wrapped[lineIndex++];
                    double pieceX = drawX;
                    foreach (CodeDrawPiece piece in line.Pieces)
                    {
                        double drawY = _y + piece.Font.Size;
                        _graphics.DrawString(piece.Text, piece.Font, new XSolidBrush(_codeTextColor), pieceX, drawY);
                        pieceX += piece.Width;
                    }
                    _y += _layout.CodeLineHeightPoints;
                }
                _y += _layout.CodeVerticalPaddingPoints;

                if (lineIndex < wrapped.Count) NewPage();
            }
            _y += _layout.CodeAfterPoints(_template);
        }

        private void AddCodeLine(string line, double maxWidth, List<CodeLineLayout> output)
        {
            string text = line.Length == 0
                ? " "
                : NormalizeDisplayGlyphs(line.Replace("\t", "    ", StringComparison.Ordinal));
            var current = new List<CodeDrawPiece>();
            double width = 0;
            foreach (string token in TokenizeForWrapping(text))
            {
                XFont font = CreateFont(token, _layout.CodeFontSizePoints, XFontStyleEx.Regular, monospace: !ContainsCjk(token));
                foreach (string pieceText in BreakToken(token, font, maxWidth))
                {
                    double pieceWidth = _graphics!.MeasureString(pieceText, font).Width;
                    if (current.Count > 0 && width + pieceWidth > maxWidth)
                    {
                        output.Add(new CodeLineLayout(current));
                        current = new List<CodeDrawPiece>();
                        width = 0;
                    }
                    current.Add(new CodeDrawPiece(pieceText, font, pieceWidth));
                    width += pieceWidth;
                }
            }
            if (current.Count > 0) output.Add(new CodeLineLayout(current));
        }

        private void RenderTable(Table table, double indent)
        {
            var rows = table.OfType<TableRow>().ToList();
            if (rows.Count == 0) return;
            int columnCount = rows.Max(row => row.Count);
            if (columnCount <= 0) return;

            double tableWidth = ContentWidth - indent;
            IReadOnlyList<double> fractions = MarkdownTableColumnSizer.ResolveFractions(table, columnCount);
            double[] columnWidths = fractions.Select(fraction => tableWidth * fraction).ToArray();

            foreach (TableRow row in rows)
            {
                _token.ThrowIfCancellationRequested();
                List<TableCellLayout> cells = BuildTableRow(row, columnCount, columnWidths);
                RenderTableRow(cells, row.IsHeader, indent, columnWidths);
            }
            _y += _blockGap;
        }

        private List<TableCellLayout> BuildTableRow(TableRow row, int columnCount, IReadOnlyList<double> columnWidths)
        {
            var result = new List<TableCellLayout>(columnCount);
            for (int i = 0; i < columnCount; i++)
            {
                TableCell? cell = i < row.Count ? row[i] as TableCell : null;
                result.Add(BuildTableCell(cell, row.IsHeader, columnWidths[i] - (_layout.TableHorizontalPaddingPoints * 2)));
            }
            return result;
        }

        private TableCellLayout BuildTableCell(TableCell? cell, bool isHeader, double maxWidth)
        {
            List<InlineSegment> segments = CollectTableCellSegments(cell, isHeader);

            List<InlineDrawPiece?> pieces = BuildTablePieces(segments, maxWidth);
            var lines = new List<List<InlineDrawPiece>> { new() };
            double lineWidth = 0;
            foreach (InlineDrawPiece? piece in pieces)
            {
                if (piece is null)
                {
                    lines.Add(new List<InlineDrawPiece>());
                    lineWidth = 0;
                    continue;
                }

                if (lines[^1].Count > 0 && lineWidth + piece.Width > maxWidth)
                {
                    lines.Add(new List<InlineDrawPiece>());
                    lineWidth = 0;
                }

                lines[^1].Add(piece);
                lineWidth += piece.Width;
            }

            return new TableCellLayout(lines);
        }

        private static List<InlineSegment> CollectTableCellSegments(TableCell? cell, bool isHeader)
        {
            var segments = new List<InlineSegment>();
            if (cell is not null)
            {
                foreach (Block child in cell)
                {
                    if (child is LeafBlock leaf && leaf.Inline is not null)
                    {
                        if (segments.Count > 0)
                            segments.Add(new InlineSegment(" ", isHeader, false, false, null));
                        CollectInlineSegments(leaf.Inline, isHeader, false, null, false, segments);
                    }
                    else
                    {
                        string text = child is ContainerBlock nested
                            ? ExtractBlockText(nested)
                            : "";
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            if (segments.Count > 0)
                                segments.Add(new InlineSegment(" ", isHeader, false, false, null));
                            segments.Add(new InlineSegment(text, isHeader, false, false, null));
                        }
                    }
                }
            }

            if (segments.Count == 0)
                segments.Add(new InlineSegment("", isHeader, false, false, null));
            return segments;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S3776", Justification = "Table token wrapping preserves explicit newline, whitespace, and font fallback behavior in one pass.")]
        private List<InlineDrawPiece?> BuildTablePieces(List<InlineSegment> segments, double maxPieceWidth)
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
                    : new XSolidBrush(_textColor);
                foreach (string token in TokenizeForWrapping(text))
                {
                    if (token == "\n")
                    {
                        pieces.Add(null);
                        continue;
                    }

                    XFont font = CreateFont(
                        token,
                        segment.Code ? _layout.CodeFontSizePoints : _layout.TableFontSizePoints,
                        GetInlineStyle(segment),
                        segment.Code && !ContainsCjk(token));
                    foreach (string pieceText in BreakToken(token, font, maxPieceWidth))
                        pieces.Add(new InlineDrawPiece(pieceText, segment.Url, segment.Code, font, brush, _graphics!.MeasureString(pieceText, font).Width));
                }
            }
            return pieces;
        }

        private void RenderTableRow(List<TableCellLayout> cells, bool isHeader, double indent, IReadOnlyList<double> columnWidths)
        {
            int lineOffset = 0;
            int maxLines = Math.Max(1, cells.Max(cell => cell.Lines.Count));
            while (lineOffset < maxLines)
            {
                double tableLineHeight = _layout.TableLineHeightPoints;
                double tablePadding = _layout.TableVerticalPaddingPoints * 2;
                double minimumRowHeight = tableLineHeight + tablePadding;
                EnsureSpace(minimumRowHeight);
                int availableLines = Math.Max(1, (int)Math.Floor((PageBottom - _y - tablePadding) / tableLineHeight));
                int lineCount = Math.Min(maxLines - lineOffset, availableLines);
                double rowHeight = Math.Max(minimumRowHeight, (lineCount * tableLineHeight) + tablePadding);
                DrawTableRowChunk(cells, isHeader, indent, columnWidths, lineOffset, lineCount, rowHeight);
                _y += rowHeight;
                lineOffset += lineCount;
            }
        }

        private void DrawTableRowChunk(
            List<TableCellLayout> cells,
            bool isHeader,
            double indent,
            IReadOnlyList<double> columnWidths,
            int lineOffset,
            int lineCount,
            double rowHeight)
        {
            double offset = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                double columnWidth = columnWidths[i];
                double x = _marginLeft + indent + offset;
                if (isHeader && _template.Layout.FillTableHeader)
                    _graphics!.DrawRectangle(new XSolidBrush(_accentSoftColor), x, _y, columnWidth, rowHeight);
                else if (_template.Id == MarkdownPdfOptions.ThemeDefault)
                    _graphics!.DrawRectangle(new XSolidBrush(_surfaceColor), x, _y, columnWidth, rowHeight);
                _graphics!.DrawRectangle(new XPen(_borderColor, 0.8), x, _y, columnWidth, rowHeight);
                DrawTableCellLines(cells[i], x, lineOffset, lineCount);
                offset += columnWidth;
            }
        }

        private void DrawTableCellLines(TableCellLayout cell, double x, int lineOffset, int lineCount)
        {
            int end = Math.Min(cell.Lines.Count, lineOffset + lineCount);
            for (int lineIndex = lineOffset; lineIndex < end; lineIndex++)
            {
                int visibleIndex = lineIndex - lineOffset;
                double drawX = x + _layout.TableHorizontalPaddingPoints;
                double lineTop = _y + _layout.TableVerticalPaddingPoints + (visibleIndex * _layout.TableLineHeightPoints);
                foreach (InlineDrawPiece piece in cell.Lines[lineIndex])
                {
                    if (piece.IsCode && !string.IsNullOrWhiteSpace(piece.Text))
                        _graphics!.DrawRectangle(new XSolidBrush(_inlineCodeBackgroundColor), drawX - 1, lineTop + 1, piece.Width + 2, _layout.TableLineHeightPoints - 2);
                    _graphics!.DrawString(piece.Text, piece.Font, piece.Brush, drawX, lineTop + piece.Font.Size);
                    if (piece.Url is not null && IsWebUrl(piece.Url))
                    {
                        _graphics.DrawLine(new XPen(_accentColor, 0.8), drawX, lineTop + piece.Font.Size + 2, drawX + piece.Width, lineTop + piece.Font.Size + 2);
                        _page!.AddWebLink(CreateWebLinkRectangle(drawX, lineTop, piece.Width, _layout.TableLineHeightPoints), piece.Url);
                    }
                    drawX += piece.Width;
                }
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
                XBrush brush = ResolveInlineBrush(segment, baseBrush);

                foreach (string token in TokenizeForWrapping(text))
                {
                    if (token == "\n")
                    {
                        pieces.Add(null);
                        continue;
                    }

                    XFont font = CreateFont(token, segment.Code ? _layout.CodeFontSizePoints : size, GetInlineStyle(segment), segment.Code && !ContainsCjk(token));
                    foreach (string pieceText in BreakToken(token, font, maxPieceWidth))
                        pieces.Add(new InlineDrawPiece(pieceText, segment.Url, segment.Code, font, brush, _graphics!.MeasureString(pieceText, font).Width));
                }
            }
            return pieces;
        }

        private void DrawJustifiedLine(List<InlineDrawPiece> pieces, double lineStart, double maxX, double lineWidth, double lineHeight, bool justifyLine)
        {
            int gapCount = justifyLine ? CountExpandableGaps(pieces) : 0;

            double extraPerGap = gapCount > 0 ? Math.Max(0, maxX - lineStart - lineWidth) / gapCount : 0;
            double x = lineStart;
            for (int i = 0; i < pieces.Count; i++)
            {
                InlineDrawPiece piece = pieces[i];
                DrawJustifiedPiece(piece, x, lineHeight);
                x += piece.Width;
                if (i + 1 < pieces.Count && CanExpandGap(piece.Text, pieces[i + 1].Text))
                    x += extraPerGap;
            }
        }

        private static int CountExpandableGaps(IReadOnlyList<InlineDrawPiece> pieces)
        {
            int gapCount = 0;
            for (int i = 0; i + 1 < pieces.Count; i++)
                if (CanExpandGap(pieces[i].Text, pieces[i + 1].Text)) gapCount++;
            return gapCount;
        }

        private void DrawJustifiedPiece(InlineDrawPiece piece, double x, double lineHeight)
        {
            double top = _y;
            if (piece.IsCode && !string.IsNullOrWhiteSpace(piece.Text))
                _graphics!.DrawRectangle(new XSolidBrush(_inlineCodeBackgroundColor), x - 2, _y + 1, piece.Width + 4, lineHeight - 3);
            _graphics!.DrawString(piece.Text, piece.Font, piece.Brush, x, _y + piece.Font.Size);
            if (piece.Url is not null && IsWebUrl(piece.Url))
            {
                _graphics.DrawLine(new XPen(_accentColor, 0.8), x, _y + piece.Font.Size + 2, x + piece.Width, _y + piece.Font.Size + 2);
                _page!.AddWebLink(CreateWebLinkRectangle(x, top, piece.Width, lineHeight), piece.Url);
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
                    XFont font = CreateFont(token, segment.Code ? _layout.CodeFontSizePoints : size, GetInlineStyle(segment), segment.Code && !ContainsCjk(token));
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
                NewLine(indent, lineHeight, ref x);
                return;
            }
            if (segment.ImageUrl is not null && TryRenderInlineImage(segment.ImageUrl, indent, lineHeight, ref x)) return;

            string text = NormalizeDisplayGlyphs(segment.Text);
            XBrush brush = baseBrush;
            if (segment.Url is not null)
                brush = new XSolidBrush(_accentColor);
            else if (segment.Bold)
                brush = new XSolidBrush(_strongTextColor);
            foreach (string token in TokenizeForWrapping(text))
            {
                double fontSize = segment.Code ? _layout.CodeFontSizePoints : size;
                XFont font = token == "\n"
                    ? CreateFont(string.Empty, fontSize, GetInlineStyle(segment), segment.Code)
                    : CreateFont(token, fontSize, GetInlineStyle(segment), segment.Code && !ContainsCjk(token));
                RenderInlineToken(token, segment.Url, segment.Code, font, brush, indent, lineHeight, maxX, ref x);
            }
        }

        private XBrush ResolveInlineBrush(InlineSegment segment, XBrush baseBrush)
        {
            if (segment.Url is not null) return new XSolidBrush(_accentColor);
            if (segment.Bold) return new XSolidBrush(_strongTextColor);
            return baseBrush;
        }

        private static string NormalizeDisplayGlyphs(string text) => text;

        private void RenderInlineToken(string token, string? url, bool isCode, XFont font, XBrush brush, double indent, double lineHeight, double maxX, ref double x)
        {
            if (token == "\n")
            {
                NewLine(indent, lineHeight, ref x);
                return;
            }

            foreach (string piece in BreakToken(token, font, maxX - (_marginLeft + indent)))
                DrawInlinePiece(piece, url, isCode, font, brush, indent, lineHeight, maxX, ref x);
        }

        private void DrawInlinePiece(string piece, string? url, bool isCode, XFont font, XBrush brush, double indent, double lineHeight, double maxX, ref double x)
        {
            double width = _graphics!.MeasureString(piece, font).Width;
            if (x > _marginLeft + indent && x + width > maxX) NewLine(indent, lineHeight, ref x);

            double top = _y;
            if (isCode && !string.IsNullOrWhiteSpace(piece))
                _graphics.DrawRectangle(new XSolidBrush(_inlineCodeBackgroundColor), x - 2, _y + 1, width + 4, lineHeight - 3);
            _graphics.DrawString(piece, font, brush, x, _y + font.Size);
            if (url is not null && IsWebUrl(url))
            {
                _graphics.DrawLine(new XPen(_accentColor, 0.8), x, _y + font.Size + 2, x + width, _y + font.Size + 2);
                _page!.AddWebLink(CreateWebLinkRectangle(x, top, width, lineHeight), url);
            }
            x += width;
        }

        private PdfRectangle CreateWebLinkRectangle(double x, double top, double width, double height)
        {
            double pageHeight = _page!.Height.Point;
            return new PdfRectangle(
                new XPoint(x, pageHeight - (top + height)),
                new XPoint(x + width, pageHeight - top));
        }

        private static XFontStyleEx GetInlineStyle(InlineSegment segment)
        {
            if (segment.Bold && segment.Italic) return XFontStyleEx.BoldItalic;
            if (segment.Bold) return XFontStyleEx.Bold;
            return segment.Italic ? XFontStyleEx.Italic : XFontStyleEx.Regular;
        }

        private bool TryRenderInlineImage(string url, double indent, double lineHeight, ref double x)
        {
            if (!TryResolveLocalImagePath(url, out string imagePath)) return false;

            XImage image;
            try
            {
                image = XImage.FromFile(imagePath);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (NotSupportedException) { return false; }

            using (image)
            {
                double lineStart = _marginLeft + indent;
                if (x > lineStart) NewLine(indent, lineHeight, ref x);

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

        private void NewLine(double indent, double lineHeight, ref double x)
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
            => _layout.HeadingLineHeightPoints(_template, level) + _layout.HeadingAfterPoints(_template, level);

        private double EstimateMinimumBlockHeight(Block block) => block switch
        {
            ParagraphBlock => _bodyLineHeight * 2,
            ListBlock => _bodyLineHeight * 2,
            QuoteBlock => _bodyLineHeight * 2,
            Table => _layout.TableLineHeightPoints + (_layout.TableVerticalPaddingPoints * 2),
            FencedCodeBlock => _layout.CodeLineHeightPoints + (_layout.CodeVerticalPaddingPoints * 2),
            CodeBlock => _layout.CodeLineHeightPoints + (_layout.CodeVerticalPaddingPoints * 2),
            HeadingBlock heading => GetHeadingHeight(heading.Level),
            _ => _bodyLineHeight
        };

        private double EstimateListItemHeight(ListItemBlock item, double indent)
        {
            if (item.Count != 1 || item[0] is not ParagraphBlock paragraph || paragraph.Inline is null)
                return _bodyLineHeight * 2;

            string text = ExtractInlineText(paragraph.Inline);
            XFont font = CreateFont(text, _bodySize, XFontStyleEx.Regular);
            int lines = Math.Max(1, WrapText(text, font, ContentWidth - indent - _layout.ListIndentPoints).Count());
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
                if (ch == '\u2794') return "Segoe UI Symbol";
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
                    default:
                        // Ignore unsupported inline nodes while preserving surrounding content.
                        break;
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S3776", Justification = "Recursive Markdig block traversal is intentionally explicit for supported leaf and nested container nodes.")]
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
        private sealed record CodeDrawPiece(string Text, XFont Font, double Width);
        private sealed record CodeLineLayout(List<CodeDrawPiece> Pieces);
        private sealed record TableCellLayout(List<List<InlineDrawPiece>> Lines);
        private sealed record QuoteSurfaceFragment(PdfPage Page, double X, double Y, double Width, double Height);
        private sealed class QuoteState(double x, double startY)
        {
            /// <summary>Left edge of the active quote surface.</summary>
            public double X { get; } = x;
            /// <summary>Current vertical start of the active quote surface.</summary>
            public double StartY { get; set; } = startY;
        }
    }
}

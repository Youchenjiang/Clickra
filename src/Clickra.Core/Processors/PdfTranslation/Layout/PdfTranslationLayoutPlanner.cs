using Clickra.Core.Models;
using PdfSharp.Drawing;

namespace Clickra.Core.Processors;

/// <summary>Source geometry and measured output geometry for one paragraph.</summary>
internal sealed class PdfParagraphLayoutSnapshot
{
    public required PdfParagraph Paragraph { get; init; }
    public required PdfParagraphSemanticRole Role { get; init; }
    public required double SourceFontSize { get; set; }
    public required double SourceLineHeight { get; set; }
    public required double SourceCenterX { get; init; }
    public required double SourceLeftAnchor { get; init; }
    public required double SourceRightAnchor { get; init; }
    public required int Column { get; init; }
    public double MeasuredHeight { get; set; }
    public double OutputFontSize { get; set; }
    public double ShiftY { get; set; }
    public double FontRatio => SourceFontSize <= 0 ? 1 : OutputFontSize / SourceFontSize;
}

internal sealed class PdfTranslationLayoutPlan
{
    public IReadOnlyList<PdfParagraphLayoutSnapshot> Snapshots { get; init; } = Array.Empty<PdfParagraphLayoutSnapshot>();
    public int HeadingCount { get; init; }
    public int ShiftedParagraphCount { get; init; }
    public int FixedCollisionCount { get; init; }
    public int BottomOverflowCount { get; init; }
    public double MaximumAlignmentAnchorShift { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public bool IsSuccessful => string.IsNullOrEmpty(FailureReason);
}

internal sealed class PdfTranslationLayoutSummary
{
    public int HeadingCount { get; set; }
    public int ShiftedParagraphCount { get; set; }
    public int FixedCollisionCount { get; set; }
    public int BottomOverflowCount { get; set; }
    public double MinimumHeadingFontRatio { get; set; } = 1.0;
    public double MaximumAlignmentAnchorShift { get; set; }
}

internal sealed class PdfLayoutPlanningException : InvalidOperationException
{
    public int FixedCollisionCount { get; }
    public int BottomOverflowCount { get; }

    public PdfLayoutPlanningException(
        string message,
        int fixedCollisionCount = 0,
        int bottomOverflowCount = 0) : base(message)
    {
        FixedCollisionCount = fixedCollisionCount;
        BottomOverflowCount = bottomOverflowCount;
    }
}

/// <summary>
/// Captures source typography before rendering and performs the only permitted
/// reflow: a heading may push translatable prose below it in the same column.
/// Protected regions are fixed obstacles and are never shifted.
/// </summary>
internal static class PdfTranslationLayoutPlanner
{
    private const double Gap = 2.0;
    private const double PageBottomMargin = 14.0;

    public static PdfTranslationLayoutPlan BuildAndApply(
        XGraphics gfx,
        IReadOnlyList<PdfParagraph> paragraphs,
        string targetFontName,
        double pageWidth,
        double pageHeight)
    {
        var snapshots = paragraphs.Select(p => new PdfParagraphLayoutSnapshot
        {
            Paragraph = p,
            Role = AssignRole(p),
            SourceFontSize = p.SourceVisualFontSize > 0
                ? p.SourceVisualFontSize
                : (p.AllLetters.Count == 0 ? p.AverageFontSize : p.AllLetters.Max(l => l.FontSize)),
            SourceLineHeight = p.SourceLineHeight > 0 ? p.SourceLineHeight : p.Height,
            SourceCenterX = (p.OriginalX0 + p.OriginalX1) / 2.0,
            SourceLeftAnchor = p.OriginalX0,
            SourceRightAnchor = p.OriginalX1,
            Column = ColumnOf(p, pageWidth)
        }).ToList();

        NormalizeContinuationTypography(snapshots, pageWidth);

        foreach (var snapshot in snapshots)
        {
            snapshot.Paragraph.SemanticRole = snapshot.Role;
            snapshot.Paragraph.SourceVisualFontSize = snapshot.SourceFontSize;
            snapshot.Paragraph.TranslationGroupId = snapshot.Role == PdfParagraphSemanticRole.PageTitle
                ? "page-title"
                : string.Empty;
            snapshot.OutputFontSize = snapshot.SourceFontSize;
            snapshot.MeasuredHeight = string.IsNullOrWhiteSpace(snapshot.Paragraph.TranslatedText)
                ? snapshot.Paragraph.Height
                : PdfTranslatedParagraphRenderer.RenderParagraph(
                    gfx,
                    snapshot.Paragraph,
                    targetFontName,
                    measureOnly: true,
                    metricsSink: metrics => snapshot.OutputFontSize = metrics.EffectiveFontSize);

        }

        int propagatedContinuations = PropagateContinuationFontSize(snapshots, pageWidth);

        int shifted = propagatedContinuations;
        foreach (var heading in snapshots
                     .Where(s => IsHeading(s.Role) && !string.IsNullOrWhiteSpace(s.Paragraph.TranslatedText))
                     .OrderByDescending(s => s.Paragraph.OriginalY1))
        {
            double extra = Math.Max(0, heading.MeasuredHeight - heading.Paragraph.Height);
            if (extra <= 1.0) continue;

            var sameColumn = heading.Column < 0
                ? snapshots.ToList()
                : snapshots.Where(s => s.Column == heading.Column).ToList();
            var fixedObstacles = sameColumn
                .Where(s => IsFixedObstacle(s.Paragraph) && s.Paragraph.OriginalY1 < heading.Paragraph.OriginalY0)
                .OrderByDescending(s => s.Paragraph.OriginalY1)
                .ToList();
            double obstacleTop = fixedObstacles.Count > 0
                ? fixedObstacles[0].Paragraph.OriginalY1
                : PageBottomMargin;
            double available = heading.Paragraph.OriginalY0 - obstacleTop - Gap;
            if (extra > available + 0.5)
            {
                string reason = $"Heading '{Preview(heading.Paragraph.TextWithPlaceholders)}' needs {extra:F1}pt but only {Math.Max(0, available):F1}pt is available before a fixed region/page bottom.";
                throw new PdfLayoutPlanningException(reason, fixedCollisionCount: 1);
            }

            foreach (var candidate in sameColumn
                         .Where(s => s.Paragraph != heading.Paragraph &&
                                     s.Paragraph.OriginalY1 < heading.Paragraph.OriginalY0 &&
                                     IsShiftable(s.Paragraph, pageHeight)))
            {
                candidate.Paragraph.Y0 -= extra;
                candidate.Paragraph.Y1 -= extra;
                candidate.ShiftY -= extra;
                shifted++;
            }
        }

        // A translated single-line paragraph can legitimately grow to two
        // lines when its source glyph box omitted leading (the acknowledgement
        // names on ASTER p.11 are the concrete case). Resolve the actual
        // rendered-height overlap against the next same-column fragment; using
        // only source-height delta is insufficient when the source box is much
        // shorter than its visual line spacing.
        foreach (var expanding in snapshots
                     .Where(s => !IsHeading(s.Role) &&
                                 !string.IsNullOrWhiteSpace(s.Paragraph.TranslatedText))
                     .OrderByDescending(s => s.Paragraph.OriginalY1))
        {
            double extra = Math.Max(0, expanding.MeasuredHeight - expanding.Paragraph.Height);
            double sourceLineBox = Math.Max(expanding.Paragraph.SourceLineHeight, expanding.SourceFontSize);
            if (extra <= 1.0 || expanding.Paragraph.Height > Math.Max(sourceLineBox * 1.5, 8.0) ||
                expanding.MeasuredHeight > sourceLineBox * 2.5) continue;

            var candidate = snapshots
                .Where(s => s.Column == expanding.Column &&
                            s.Paragraph != expanding.Paragraph &&
                            s.Paragraph.OriginalY1 < expanding.Paragraph.OriginalY0 &&
                            IsReflowShiftable(s.Paragraph, pageHeight))
                .OrderByDescending(s => s.Paragraph.OriginalY1)
                .FirstOrDefault();
            if (candidate == null) continue;

            double targetY1 = expanding.Paragraph.Y0 - expanding.MeasuredHeight - Gap;
            double delta = targetY1 - candidate.Paragraph.Y1;
            if (delta >= -0.5) continue;

            candidate.Paragraph.Y0 += delta;
            candidate.Paragraph.Y1 += delta;
            candidate.ShiftY += delta;
            shifted++;
        }

        // Final geometric guard: compare the actual rendered bottom of each
        // translated fragment with the next fragment in the same column. This
        // catches split paragraphs whose source boxes overlap after reflow;
        // it is deliberately limited to movable translated text.
        foreach (var columnGroup in snapshots.GroupBy(s => s.Column))
        {
            PdfParagraphLayoutSnapshot? previous = null;
            foreach (var current in columnGroup.OrderByDescending(s => s.Paragraph.OriginalY0))
            {
                if (!IsReflowShiftable(current.Paragraph, pageHeight))
                {
                    // Headings and protected regions are hard layout
                    // boundaries. Never carry a paragraph's extra height
                    // across a references/section heading.
                    previous = null;
                    continue;
                }
                if (!IsShortNaturalExpansion(current))
                    continue;

                if (previous != null)
                {
                    double previousBottom = previous.Paragraph.Y0 -
                        Math.Max(previous.MeasuredHeight, previous.Paragraph.Height);
                    double targetY1 = previousBottom - Gap;
                    double delta = targetY1 - current.Paragraph.Y1;
                    if (delta < -0.5)
                    {
                        current.Paragraph.Y0 += delta;
                        current.Paragraph.Y1 += delta;
                        current.ShiftY += delta;
                        shifted++;
                    }
                }
                previous = current;
            }
        }

        var bottomOverflowParagraphs = snapshots
            .Where(s => IsReflowShiftable(s.Paragraph, pageHeight) &&
                        s.Paragraph.Y0 < PageBottomMargin - 0.5 &&
                        s.Paragraph.OriginalY0 >= PageBottomMargin - 0.5)
            .ToList();
        int bottomOverflow = bottomOverflowParagraphs.Count;
        if (bottomOverflow > 0)
        {
            string details = string.Join(", ", bottomOverflowParagraphs
                .Take(3).Select(s => $"'{Preview(s.Paragraph.TextWithPlaceholders)}' y0={s.Paragraph.Y0:F1}"));
            throw new PdfLayoutPlanningException(
                $"{bottomOverflow} paragraph(s) moved below the page bottom: {details}",
                bottomOverflowCount: bottomOverflow);
        }

        double maximumAlignmentAnchorShift = snapshots
            .Where(s => IsHeading(s.Role))
            .Select(s => Math.Max(
                Math.Abs(((s.Paragraph.X0 + s.Paragraph.X1) / 2.0) - s.SourceCenterX),
                Math.Max(
                    Math.Abs(s.Paragraph.X0 - s.SourceLeftAnchor),
                    Math.Abs(s.Paragraph.X1 - s.SourceRightAnchor))))
            .DefaultIfEmpty(0.0)
            .Max();

        return new PdfTranslationLayoutPlan
        {
            Snapshots = snapshots,
            HeadingCount = snapshots.Count(s => IsHeading(s.Role)),
            ShiftedParagraphCount = shifted,
            FixedCollisionCount = 0,
            BottomOverflowCount = bottomOverflow,
            MaximumAlignmentAnchorShift = maximumAlignmentAnchorShift
        };
    }

    private static PdfParagraphSemanticRole AssignRole(PdfParagraph para)
    {
        if (para.IsPageTitle) return PdfParagraphSemanticRole.PageTitle;
        // Bypassed paragraphs are fixed source regions even when their text
        // happens to look like a Roman-numbered heading (bibliography author
        // continuations such as "I. Harper, ..." are a common example).
        // Classifying them as headings would trigger forbidden reflow and can
        // fail the page-bottom planner.
        if (para.IsBypassed || para.IsTable || para.IsDiagram || para.IsCode || para.IsGrayPromptContent)
            return PdfParagraphSemanticRole.Protected;
        if (PdfParagraphSemanticClassifier.IsHeadingParagraph(para))
        {
            string text = para.TextWithPlaceholders.Trim();
            return text.StartsWith("ABSTRACT", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("摘要", StringComparison.Ordinal)
                ? PdfParagraphSemanticRole.AbstractHeading
                : PdfParagraphSemanticRole.SectionHeading;
        }
        if (PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para)) return PdfParagraphSemanticRole.FigureCaption;
        return PdfParagraphSemanticRole.Body;
    }

    private static bool IsHeading(PdfParagraphSemanticRole role) =>
        role is PdfParagraphSemanticRole.PageTitle or PdfParagraphSemanticRole.AbstractHeading or
        PdfParagraphSemanticRole.SectionHeading or PdfParagraphSemanticRole.SubsectionHeading;

    private static bool IsFixedObstacle(PdfParagraph para) =>
        para.IsBypassed || para.IsTable || para.IsDiagram || para.IsCode || para.IsGrayPromptContent ||
        PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(para);

    private static bool IsShiftable(PdfParagraph para, double pageHeight) =>
        !IsFixedObstacle(para) &&
        !PdfParagraphRoleClassifier.IsRunningHeaderOrFooter(para, pageHeight) &&
        // Lower-column continuation lines are still valid movable body prose.
        // The previous 14% page-height cutoff left ASTER p.417's final line
        // fixed while the paragraph above it moved, causing an overlap. The
        // A small bottom-band guard also keeps footer text fixed even when its
        // wording is not recognized by the running-header classifier.
        para.OriginalY0 >= Math.Max(PageBottomMargin, pageHeight * 0.06) &&
        PdfParagraphRoleClassifier.IsTranslatableBodyProse(para);

    private static bool IsReflowShiftable(PdfParagraph para, double pageHeight) =>
        !IsFixedObstacle(para) &&
        !para.IsPageTitle &&
        !PdfParagraphSemanticClassifier.IsHeadingParagraph(para) &&
        !PdfParagraphRoleClassifier.IsRunningHeaderOrFooter(para, pageHeight) &&
        para.OriginalY0 >= Math.Max(PageBottomMargin, pageHeight * 0.06) &&
        !string.IsNullOrWhiteSpace(para.TranslatedText) &&
        !PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para);

    private static bool IsShortNaturalExpansion(PdfParagraphLayoutSnapshot snapshot)
    {
        double sourceLineBox = Math.Max(snapshot.Paragraph.SourceLineHeight, snapshot.SourceFontSize);
        return snapshot.MeasuredHeight > snapshot.Paragraph.Height + 1.0 &&
               snapshot.Paragraph.Height <= Math.Max(sourceLineBox * 1.5, 8.0) &&
               snapshot.MeasuredHeight <= sourceLineBox * 4.0;
    }

    private static void NormalizeContinuationTypography(List<PdfParagraphLayoutSnapshot> snapshots, double pageWidth)
    {
        // PdfPig occasionally assigns a continuation line the font size of a
        // neighboring footer/glyph run (ASTER p.417 reports 4.7pt although the
        // body line is 9.17pt).  Treat a close, same-column body predecessor as
        // the authoritative paragraph style; the renderer may still scale down
        // from that source size if the translated line genuinely needs it.
        foreach (var current in snapshots
                     .Where(s => IsTypographyContinuationCandidate(s.Paragraph))
                     .OrderByDescending(s => s.Paragraph.OriginalY1))
        {
            var predecessor = snapshots
                .Where(s => s != current &&
                            IsTypographyContinuationCandidate(s.Paragraph) &&
                            ColumnOf(s.Paragraph, pageWidth) == ColumnOf(current.Paragraph, pageWidth) &&
                            s.SourceFontSize > current.SourceFontSize * 1.2 &&
                            Math.Abs(((s.Paragraph.OriginalY0 + s.Paragraph.OriginalY1) / 2.0) -
                                     ((current.Paragraph.OriginalY0 + current.Paragraph.OriginalY1) / 2.0)) <= 32.0)
                .OrderBy(s => Math.Abs(((s.Paragraph.OriginalY0 + s.Paragraph.OriginalY1) / 2.0) -
                                       ((current.Paragraph.OriginalY0 + current.Paragraph.OriginalY1) / 2.0)))
                .FirstOrDefault();

            if (predecessor == null) continue;

            double predecessorSize = predecessor.SourceFontSize;
            if (predecessorSize <= 0 || current.SourceFontSize >= predecessorSize * 0.8) continue;

            current.SourceFontSize = predecessorSize;
            current.SourceLineHeight = Math.Max(current.SourceLineHeight, predecessor.SourceLineHeight);
            current.Paragraph.SourceVisualFontSize = predecessorSize;
            current.Paragraph.SourceLineHeight = current.SourceLineHeight;
        }
    }

    private static bool IsTypographyContinuationCandidate(PdfParagraph para)
    {
        // A PDF extractor can split one visual paragraph into several tiny
        // paragraphs. The first fragment may not satisfy the normal prose
        // word-count heuristic (for example a page-header continuation or an
        // author-name line), but it must still inherit the source typography
        // from its adjacent same-column fragment. Keep this guard limited to
        // ordinary translatable text so headings, references, tables and
        // fixed artwork do not inherit unrelated sizes.
        if (para.IsBypassed || para.IsTable || para.IsDiagram || para.IsCode ||
            para.IsGrayPromptContent || para.IsPageTitle ||
            PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para) ||
            PdfParagraphSemanticClassifier.IsHeadingParagraph(para))
            return false;

        if (para.SemanticRole is PdfParagraphSemanticRole.AbstractHeading or
            PdfParagraphSemanticRole.SectionHeading or
            PdfParagraphSemanticRole.SubsectionHeading or
            PdfParagraphSemanticRole.Protected)
            return false;

        return !string.IsNullOrWhiteSpace(para.TextWithPlaceholders);
    }

    private static int PropagateContinuationFontSize(List<PdfParagraphLayoutSnapshot> snapshots, double pageWidth)
    {
        int moved = 0;
        foreach (var current in snapshots.Where(s => IsTypographyContinuationCandidate(s.Paragraph)))
        {
            if (current.Paragraph.Height > Math.Max(current.Paragraph.SourceLineHeight * 1.5, 8.0))
                continue;

            var predecessor = snapshots
                .Where(s => s != current &&
                            IsTypographyContinuationCandidate(s.Paragraph) &&
                            ColumnOf(s.Paragraph, pageWidth) == ColumnOf(current.Paragraph, pageWidth) &&
                            s.OutputFontSize > current.OutputFontSize * 1.2 &&
                            Math.Abs(((s.Paragraph.OriginalY0 + s.Paragraph.OriginalY1) / 2.0) -
                                     ((current.Paragraph.OriginalY0 + current.Paragraph.OriginalY1) / 2.0)) <= 32.0)
                .OrderBy(s => Math.Abs(((s.Paragraph.OriginalY0 + s.Paragraph.OriginalY1) / 2.0) -
                                       ((current.Paragraph.OriginalY0 + current.Paragraph.OriginalY1) / 2.0)))
                .FirstOrDefault();
            if (predecessor == null) continue;

            double gap = Math.Abs(((predecessor.Paragraph.OriginalY0 + predecessor.Paragraph.OriginalY1) / 2.0) -
                                 ((current.Paragraph.OriginalY0 + current.Paragraph.OriginalY1) / 2.0));
            if (gap > 32.0 || predecessor.OutputFontSize <= 0) continue;
            current.Paragraph.LayoutFontSizeOverride = predecessor.OutputFontSize;
            current.OutputFontSize = predecessor.OutputFontSize;

            // PdfPig can expose the last source line as a separate, very short
            // paragraph.  Keeping its original Y1 leaves a large blank gap
            // after the preceding paragraph once that paragraph reflows into
            // fewer translated lines.  Attach the continuation to the actual
            // rendered bottom of its same-column predecessor, preserving the
            // source column and the normal inter-line gap.
            double targetY1 = predecessor.Paragraph.Y1 - predecessor.MeasuredHeight - Gap;
            double delta = targetY1 - current.Paragraph.Y1;
            if (delta > 0.5)
            {
                current.Paragraph.Y1 += delta;
                current.Paragraph.Y0 += delta;
                current.ShiftY += delta;
                moved++;
            }
        }

        return moved;
    }

    private static int ColumnOf(PdfParagraph para, double pageWidth)
    {
        double center = (para.OriginalX0 + para.OriginalX1) / 2.0;
        if (para.OriginalX0 < pageWidth * 0.18 && para.OriginalX1 > pageWidth * 0.82) return -1;
        return center < pageWidth / 2.0 ? 0 : 1;
    }

    private static string Preview(string value)
    {
        string text = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return text.Length <= 48 ? text : text[..48] + "…";
    }
}

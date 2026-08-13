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
    public int LineCount { get; set; }
    public double LineSpacingMultiplier { get; set; }
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
    public double MinimumBodyFontRatio { get; init; } = 1.0;
    public double MaximumBodyFontRatio { get; init; } = 1.0;
    public double MaximumBodyLineSpacingMultiplier { get; init; }
    public double MaximumInterParagraphGap { get; init; }
    public double MaximumFlowRegionResidualWhitespace { get; init; }
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
    public int BodyParagraphCount { get; set; }
    public double MinimumBodyFontRatio { get; set; } = 1.0;
    public double MaximumBodyFontRatio { get; set; } = 1.0;
    public double MaximumBodyLineSpacingMultiplier { get; set; }
    public double MaximumInterParagraphGap { get; set; }
    public double MaximumFlowRegionResidualWhitespace { get; set; }
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
    private const double MinimumBodyFontScale = 0.80;
    private const double EmergencyBodyFontScale = 0.55;
    private const double MaximumBodyFontScale = 1.15;
    private const double MaximumBodyLineSpacing = 1.50;
    private const double ProtectedRegionOverlapRatio = 0.20;

    public static PdfTranslationLayoutPlan BuildAndApply(
        XGraphics gfx,
        IReadOnlyList<PdfParagraph> paragraphs,
        string targetFontName,
        double pageWidth,
        double pageHeight,
        IReadOnlyList<TableMaskRegion>? protectedRegions = null)
    {
        var snapshots = InitializeSnapshots(paragraphs, pageWidth, gfx, targetFontName);

        int propagatedContinuations = PropagateContinuationFontSize(snapshots, pageWidth);
        int shifted = propagatedContinuations + ShiftHeadingObstacles(snapshots, pageHeight);

        shifted += ReflowSingleLineExpansions(snapshots, pageHeight);
        shifted += GuardColumnBottomOverflows(snapshots, pageHeight);

        shifted += BalanceBodyFlow(
            new BodyFlowBalanceOptions(gfx, snapshots, targetFontName, pageWidth, pageHeight, protectedRegions ?? Array.Empty<TableMaskRegion>()),
            out double maximumInterParagraphGap,
            out double maximumFlowRegionResidualWhitespace);

        int bottomOverflowCount = CountBottomOverflows(snapshots, pageHeight);

        return BuildPlanResult(snapshots, shifted, pageHeight, protectedRegions, maximumInterParagraphGap, maximumFlowRegionResidualWhitespace, bottomOverflowCount);
    }

    private static List<PdfParagraphLayoutSnapshot> InitializeSnapshots(
        IReadOnlyList<PdfParagraph> paragraphs,
        double pageWidth,
        XGraphics gfx,
        string targetFontName)
    {
        var snapshots = paragraphs.Select(p => new PdfParagraphLayoutSnapshot
        {
            Paragraph = p,
            Role = AssignRole(p),
            SourceFontSize = CalculateSourceFontSize(p),
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
            MeasureSnapshot(gfx, snapshot, targetFontName);
        }

        return snapshots;
    }

    private static int ReflowSingleLineExpansions(List<PdfParagraphLayoutSnapshot> snapshots, double pageHeight)
    {
        int shifted = 0;
        foreach (var expanding in snapshots
                     .Where(s => !IsHeading(s.Role) &&
                                 !string.IsNullOrWhiteSpace(s.Paragraph.TranslatedText))
                     .OrderByDescending(s => s.Paragraph.OriginalY1))
        {
            if (ShouldSkipExpansion(expanding)) continue;

            var candidate = FindExpansionCandidate(expanding, snapshots, pageHeight);
            if (candidate == null) continue;

            double targetY1 = expanding.Paragraph.Y0 - expanding.MeasuredHeight - Gap;
            double delta = targetY1 - candidate.Paragraph.Y1;
            if (delta >= -0.5) continue;

            candidate.Paragraph.Y0 += delta;
            candidate.Paragraph.Y1 += delta;
            candidate.ShiftY += delta;
            shifted++;
        }
        return shifted;
    }

    private static bool ShouldSkipExpansion(PdfParagraphLayoutSnapshot expanding)
    {
        double extra = Math.Max(0, expanding.MeasuredHeight - expanding.Paragraph.Height);
        double sourceLineBox = Math.Max(expanding.Paragraph.SourceLineHeight, expanding.SourceFontSize);
        return extra <= 1.0 ||
               expanding.Paragraph.Height > Math.Max(sourceLineBox * 1.5, 8.0) ||
               expanding.MeasuredHeight > sourceLineBox * 2.5;
    }

    private static PdfParagraphLayoutSnapshot? FindExpansionCandidate(
        PdfParagraphLayoutSnapshot expanding,
        List<PdfParagraphLayoutSnapshot> snapshots,
        double pageHeight)
    {
        return snapshots
            .Where(s => s.Column == expanding.Column &&
                        s.Paragraph != expanding.Paragraph &&
                        s.Paragraph.OriginalY1 < expanding.Paragraph.OriginalY0 &&
                        IsReflowShiftable(s.Paragraph, pageHeight))
            .OrderByDescending(s => s.Paragraph.OriginalY1)
            .FirstOrDefault();
    }

    private static int GuardColumnBottomOverflows(List<PdfParagraphLayoutSnapshot> snapshots, double pageHeight)
    {
        int shifted = 0;
        foreach (var columnGroup in snapshots.GroupBy(s => s.Column))
            shifted += GuardSingleColumnBottomOverflows(columnGroup, pageHeight);
        return shifted;
    }

    private static int GuardSingleColumnBottomOverflows(
        IGrouping<int, PdfParagraphLayoutSnapshot> columnGroup,
        double pageHeight)
    {
        int shifted = 0;
        PdfParagraphLayoutSnapshot? previous = null;
        foreach (var current in columnGroup.OrderByDescending(s => s.Paragraph.OriginalY0))
        {
            if (!IsReflowShiftable(current.Paragraph, pageHeight))
            {
                previous = null;
                continue;
            }
            if (!IsShortNaturalExpansion(current)) continue;

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
        return shifted;
    }

    private static int CountBottomOverflows(List<PdfParagraphLayoutSnapshot> snapshots, double pageHeight) =>
        snapshots.Count(s => IsReflowShiftable(s.Paragraph, pageHeight) &&
                             s.Paragraph.Y0 < PageBottomMargin - 0.5 &&
                             s.Paragraph.OriginalY0 >= PageBottomMargin - 0.5);

    private static PdfTranslationLayoutPlan BuildPlanResult(
        List<PdfParagraphLayoutSnapshot> snapshots,
        int shifted,
        double pageHeight,
        IReadOnlyList<TableMaskRegion>? protectedRegions,
        double maximumInterParagraphGap,
        double maximumFlowRegionResidualWhitespace,
        int bottomOverflowCount)
    {
        double maximumAlignmentAnchorShift = snapshots
            .Where(s => IsHeading(s.Role))
            .Select(s => Math.Max(
                Math.Abs(((s.Paragraph.X0 + s.Paragraph.X1) / 2.0) - s.SourceCenterX),
                Math.Max(
                    Math.Abs(s.Paragraph.X0 - s.SourceLeftAnchor),
                    Math.Abs(s.Paragraph.X1 - s.SourceRightAnchor))))
            .DefaultIfEmpty(0.0)
            .Max();
        var bodySnapshots = snapshots
            .Where(s => s.Role == PdfParagraphSemanticRole.Body &&
                        !string.IsNullOrWhiteSpace(s.Paragraph.TranslatedText) &&
                        HasCjkTranslation(s.Paragraph) &&
                        IsReflowShiftable(s.Paragraph, pageHeight) &&
                        !OverlapsProtectedRegion(
                            s.Paragraph,
                            protectedRegions ?? Array.Empty<TableMaskRegion>()))
            .ToList();

        return new PdfTranslationLayoutPlan
        {
            Snapshots = snapshots,
            HeadingCount = snapshots.Count(s => IsHeading(s.Role)),
            ShiftedParagraphCount = shifted,
            FixedCollisionCount = 0,
            BottomOverflowCount = bottomOverflowCount,
            MaximumAlignmentAnchorShift = maximumAlignmentAnchorShift,
            MinimumBodyFontRatio = bodySnapshots.Select(s => s.FontRatio).DefaultIfEmpty(1.0).Min(),
            MaximumBodyFontRatio = bodySnapshots.Select(s => s.FontRatio).DefaultIfEmpty(1.0).Max(),
            MaximumBodyLineSpacingMultiplier = bodySnapshots
                .Select(s => s.LineSpacingMultiplier).DefaultIfEmpty(0).Max(),
            MaximumInterParagraphGap = maximumInterParagraphGap,
            MaximumFlowRegionResidualWhitespace = maximumFlowRegionResidualWhitespace
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
        !IsRotated(para) &&
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
        !IsRotated(para) &&
        !para.IsPageTitle &&
        !PdfParagraphSemanticClassifier.IsHeadingParagraph(para) &&
        !PdfParagraphRoleClassifier.IsRunningHeaderOrFooter(para, pageHeight) &&
        para.OriginalY0 >= Math.Max(PageBottomMargin, pageHeight * 0.06) &&
        !string.IsNullOrWhiteSpace(para.TranslatedText) &&
        !PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para);

    private static bool IsRotated(PdfParagraph para) =>
        !string.Equals(para.TextDirection?.ToString(), "Rotate0", StringComparison.Ordinal);

    private static bool HasCjkTranslation(PdfParagraph paragraph) =>
        !string.IsNullOrWhiteSpace(paragraph.TranslatedText) &&
        paragraph.TranslatedText.Any(FontUtilities.IsCjkCharacter);

    private static bool IsShortNaturalExpansion(PdfParagraphLayoutSnapshot snapshot)
    {
        double sourceLineBox = Math.Max(snapshot.Paragraph.SourceLineHeight, snapshot.SourceFontSize);
        return snapshot.MeasuredHeight > snapshot.Paragraph.Height + 1.0 &&
               snapshot.Paragraph.Height <= Math.Max(sourceLineBox * 1.5, 8.0) &&
               snapshot.MeasuredHeight <= sourceLineBox * 4.0;
    }

    private sealed record BodyFlowBalanceOptions(
        XGraphics Gfx,
        List<PdfParagraphLayoutSnapshot> Snapshots,
        string TargetFontName,
        double PageWidth,
        double PageHeight,
        IReadOnlyList<TableMaskRegion> ProtectedRegions);

    private static int BalanceBodyFlow(
        BodyFlowBalanceOptions options,
        out double maximumInterParagraphGap,
        out double maximumFlowRegionResidualWhitespace)
    {
        int shifted = 0;
        maximumInterParagraphGap = 0;
        maximumFlowRegionResidualWhitespace = 0;
        foreach (int column in new[] { 0, 1 })
        {
            var flowable = options.Snapshots
                .Where(s => s.Column == column &&
                            IsReflowShiftable(s.Paragraph, options.PageHeight) &&
                            HasCjkTranslation(s.Paragraph) &&
                            !OverlapsProtectedRegion(s.Paragraph, options.ProtectedRegions))
                .OrderByDescending(s => s.Paragraph.OriginalY1)
                .ToList();
            foreach (var run in BuildBodyFlowRuns(
                         flowable, options.Snapshots, column, options.PageWidth, options.ProtectedRegions))
            {
                if (run.Count == 0) continue;

                shifted += BalanceSingleFlowRun(
                    options, run,
                    out double runMaxGap, out double runResidual);
                maximumInterParagraphGap = Math.Max(maximumInterParagraphGap, runMaxGap);
                maximumFlowRegionResidualWhitespace = Math.Max(maximumFlowRegionResidualWhitespace, runResidual);
            }
        }
        return shifted;
    }

    private static int BalanceSingleFlowRun(
        BodyFlowBalanceOptions options,
        List<PdfParagraphLayoutSnapshot> run,
        out double maximumInterParagraphGap,
        out double maximumFlowRegionResidualWhitespace)
    {
        int shifted = 0;
        maximumInterParagraphGap = 0;
        maximumFlowRegionResidualWhitespace = 0;

        double regionTop = run[0].Paragraph.Y1;
        double protectedBoundaryTop = FindAdjacentProtectedBoundaryTop(run, options.ProtectedRegions, regionTop);
        double flowBoundaryTop = FindAdjacentFlowBoundaryTop(
            run,
            options.Snapshots,
            options.PageWidth,
            options.ProtectedRegions,
            regionTop);
        double boundaryTop = Math.Max(protectedBoundaryTop, flowBoundaryTop);
        double regionBottom = Math.Max(
            PageBottomMargin,
            Math.Max(
                run[^1].Paragraph.OriginalY0,
                boundaryTop > 0 ? boundaryTop + Gap : 0));
        double availableHeight = regionTop - regionBottom;
        if (availableHeight <= 0) return 0;

        var baseGaps = ComputeBaseGaps(run);
        double gapHeight = baseGaps.Sum();
        double contentBudget = availableHeight - gapHeight;
        if (contentBudget <= 0) return 0;

        var baseFonts = run.ToDictionary(
            snapshot => snapshot,
            snapshot => Math.Max(snapshot.OutputFontSize, snapshot.SourceFontSize * MinimumBodyFontScale));
        double selectedScale = FindLargestFittingFontScale(options.Gfx, run, baseFonts, options.TargetFontName, contentBudget);
        ApplyFontScaleAndMeasure(options.Gfx, run, baseFonts, options.TargetFontName, selectedScale);

        shifted += IncreaseLeadingToFit(run, options, contentBudget);

        RedistributeGaps(run, baseGaps, availableHeight);

        double usedHeight = run.Sum(s => s.MeasuredHeight) + baseGaps.Sum();
        double residual = Math.Max(0, availableHeight - usedHeight);
        maximumInterParagraphGap = baseGaps.DefaultIfEmpty(0).Max();
        maximumFlowRegionResidualWhitespace = residual;

        shifted += PositionRunParagraphs(run, regionTop, residual, baseGaps);
        return shifted;
    }

    private static List<double> ComputeBaseGaps(List<PdfParagraphLayoutSnapshot> run)
    {
        var baseGaps = new List<double>();
        for (int i = 1; i < run.Count; i++)
        {
            double sourceGap = run[i - 1].Paragraph.OriginalY0 - run[i].Paragraph.OriginalY1;
            double typicalLine = Math.Max(run[i - 1].SourceLineHeight, run[i - 1].SourceFontSize);
            baseGaps.Add(Math.Clamp(sourceGap, Gap, Math.Max(Gap, typicalLine * 0.85)));
        }
        return baseGaps;
    }

    private static int IncreaseLeadingToFit(
        List<PdfParagraphLayoutSnapshot> run,
        BodyFlowBalanceOptions options,
        double contentBudget)
    {
        double remaining = Math.Max(0, contentBudget - run.Sum(s => s.MeasuredHeight));
        double lineUnits = run.Sum(s => s.OutputFontSize * Math.Max(0, s.LineCount));
        if (remaining > 0.5 && lineUnits > 0)
        {
            double leadingIncrease = Math.Min(
                remaining / lineUnits,
                run.Min(s => Math.Max(0, MaximumBodyLineSpacing - s.LineSpacingMultiplier)));
            if (leadingIncrease > 0.001)
            {
                foreach (var snapshot in run)
                {
                    snapshot.Paragraph.LayoutLineSpacingMultiplierOverride =
                        snapshot.LineSpacingMultiplier + leadingIncrease;
                    MeasureSnapshot(options.Gfx, snapshot, options.TargetFontName);
                }
            }
        }
        return 0;
    }

    private static void RedistributeGaps(
        List<PdfParagraphLayoutSnapshot> run,
        List<double> baseGaps,
        double availableHeight)
    {
        double remaining = Math.Max(0, availableHeight - run.Sum(s => s.MeasuredHeight) - baseGaps.Sum());
        if (remaining <= 0.5 || baseGaps.Count == 0) return;

        double perGap = remaining / baseGaps.Count;
        for (int i = 0; i < baseGaps.Count; i++)
        {
            double maximumGap = Math.Max(
                Gap,
                Math.Max(run[i].SourceLineHeight, run[i].SourceFontSize) * 1.15);
            double addition = Math.Min(perGap, maximumGap - baseGaps[i]);
            if (addition > 0) baseGaps[i] += addition;
        }
    }

    private static int PositionRunParagraphs(
        List<PdfParagraphLayoutSnapshot> run,
        double regionTop,
        double residual,
        List<double> baseGaps)
    {
        int shifted = 0;
        double cursor = regionTop - residual / 2.0;
        for (int i = 0; i < run.Count; i++)
        {
            var snapshot = run[i];
            double newY1 = cursor;
            double newY0 = newY1 - snapshot.MeasuredHeight;
            double delta = newY1 - snapshot.Paragraph.Y1;
            if (Math.Abs(delta) > 0.5)
            {
                snapshot.ShiftY += delta;
                shifted++;
            }
            snapshot.Paragraph.Y1 = newY1;
            snapshot.Paragraph.Y0 = newY0;
            cursor = newY0 - (i < baseGaps.Count ? baseGaps[i] : 0);
        }
        return shifted;
    }

    private static double FindAdjacentProtectedBoundaryTop(
        IReadOnlyList<PdfParagraphLayoutSnapshot> run,
        IReadOnlyList<TableMaskRegion> protectedRegions,
        double regionTop)
    {
        if (run.Count == 0 || protectedRegions.Count == 0) return 0;

        var last = run[^1].Paragraph;
        double paragraphWidth = Math.Max(0, last.OriginalX1 - last.OriginalX0);
        if (paragraphWidth <= 0) return 0;

        // A detected vector/table region can be padded a few points into the
        // source paragraph above it. Treat a region touching the last flow
        // paragraph (or within one generous paragraph gap below it) as the
        // hard lower boundary, while ignoring unrelated regions farther down.
        double minimumRelevantTop = last.OriginalY0 - 36.0;
        return protectedRegions
            .Where(region =>
            {
                if (region.Y1 > regionTop + 0.5 || region.Y1 < minimumRelevantTop)
                    return false;
                double overlapWidth = Math.Max(
                    0,
                    Math.Min(last.OriginalX1, region.X1) -
                    Math.Max(last.OriginalX0, region.X0));
                return overlapWidth / paragraphWidth >= ProtectedRegionOverlapRatio;
            })
            .Select(region => region.Y1)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static double FindAdjacentFlowBoundaryTop(
        IReadOnlyList<PdfParagraphLayoutSnapshot> run,
        IReadOnlyList<PdfParagraphLayoutSnapshot> snapshots,
        double pageWidth,
        IReadOnlyList<TableMaskRegion> protectedRegions,
        double regionTop)
    {
        if (run.Count == 0) return 0;

        var last = run[^1].Paragraph;
        double paragraphWidth = Math.Max(0, last.OriginalX1 - last.OriginalX0);
        if (paragraphWidth <= 0) return 0;

        return snapshots
            .Where(boundary =>
                !run.Contains(boundary) &&
                IsFlowBoundary(boundary, pageWidth, protectedRegions) &&
                boundary.Paragraph.OriginalY1 <= regionTop + 0.5 &&
                SharesHorizontalBand(last, boundary.Paragraph, paragraphWidth))
            .Select(boundary => boundary.Paragraph.OriginalY1)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static bool SharesHorizontalBand(
        PdfParagraph paragraph,
        PdfParagraph boundary,
        double paragraphWidth)
    {
        double overlapWidth = Math.Max(
            0,
            Math.Min(paragraph.OriginalX1, boundary.OriginalX1) -
            Math.Max(paragraph.OriginalX0, boundary.OriginalX0));
        return overlapWidth / paragraphWidth >= ProtectedRegionOverlapRatio;
    }

    private static IReadOnlyList<List<PdfParagraphLayoutSnapshot>> BuildBodyFlowRuns(
        IReadOnlyList<PdfParagraphLayoutSnapshot> flowable,
        IReadOnlyList<PdfParagraphLayoutSnapshot> allSnapshots,
        int column,
        double pageWidth,
        IReadOnlyList<TableMaskRegion> protectedRegions)
    {
        var runs = new List<List<PdfParagraphLayoutSnapshot>>();
        var current = new List<PdfParagraphLayoutSnapshot>();
        PdfParagraphLayoutSnapshot? previous = null;
        foreach (var snapshot in flowable)
        {
            double sourceGap = previous == null
                ? 0
                : previous.Paragraph.OriginalY0 - snapshot.Paragraph.OriginalY1;
            double gapLimit = previous == null
                ? double.MaxValue
                : Math.Max(36.0, previous.SourceLineHeight * 4.0);
            bool boundary = previous != null &&
                (sourceGap > gapLimit || HasFixedBoundaryBetween(
                    previous, snapshot, allSnapshots, column, pageWidth, protectedRegions));
            if (boundary)
            {
                runs.Add(current);
                current = new List<PdfParagraphLayoutSnapshot>();
            }
            current.Add(snapshot);
            previous = snapshot;
        }
        if (current.Count > 0) runs.Add(current);
        return runs;
    }

    private static bool HasFixedBoundaryBetween(
        PdfParagraphLayoutSnapshot upper,
        PdfParagraphLayoutSnapshot lower,
        IReadOnlyList<PdfParagraphLayoutSnapshot> snapshots,
        int column,
        double pageWidth,
        IReadOnlyList<TableMaskRegion> protectedRegions)
    {
        return snapshots.Any(boundary =>
            boundary != upper && boundary != lower &&
            IsFlowBoundary(boundary, pageWidth, protectedRegions) &&
            (boundary.Column == column || boundary.Column < 0) &&
            boundary.Paragraph.OriginalY1 <= upper.Paragraph.OriginalY0 + 0.5 &&
            boundary.Paragraph.OriginalY0 >= lower.Paragraph.OriginalY1 - 0.5);
    }

    private static bool IsFlowBoundary(
        PdfParagraphLayoutSnapshot snapshot,
        double pageWidth,
        IReadOnlyList<TableMaskRegion> protectedRegions) =>
        IsHeading(snapshot.Role) ||
        snapshot.Role is PdfParagraphSemanticRole.FigureCaption or PdfParagraphSemanticRole.Protected ||
        IsFixedObstacle(snapshot.Paragraph) ||
        OverlapsProtectedRegion(snapshot.Paragraph, protectedRegions) ||
        snapshot.Paragraph.Width > pageWidth * 0.70;

    private static bool OverlapsProtectedRegion(
        PdfParagraph paragraph,
        IReadOnlyList<TableMaskRegion> protectedRegions)
    {
        if (protectedRegions.Count == 0) return false;

        double paragraphWidth = Math.Max(0, paragraph.OriginalX1 - paragraph.OriginalX0);
        double paragraphHeight = Math.Max(0, paragraph.OriginalY1 - paragraph.OriginalY0);
        double paragraphArea = paragraphWidth * paragraphHeight;
        if (paragraphArea <= 0) return false;

        double centerX = (paragraph.OriginalX0 + paragraph.OriginalX1) / 2.0;
        double centerY = (paragraph.OriginalY0 + paragraph.OriginalY1) / 2.0;
        foreach (var region in protectedRegions)
        {
            double overlapWidth = Math.Max(
                0,
                Math.Min(paragraph.OriginalX1, region.X1) -
                Math.Max(paragraph.OriginalX0, region.X0));
            double overlapHeight = Math.Max(
                0,
                Math.Min(paragraph.OriginalY1, region.Y1) -
                Math.Max(paragraph.OriginalY0, region.Y0));
            double overlapRatio = overlapWidth * overlapHeight / paragraphArea;
            bool centerInside = centerX >= region.X0 && centerX <= region.X1 &&
                                centerY >= region.Y0 && centerY <= region.Y1;
            if (centerInside || overlapRatio >= ProtectedRegionOverlapRatio)
                return true;
        }

        return false;
    }

    private static double FindLargestFittingFontScale(
        XGraphics gfx,
        IReadOnlyList<PdfParagraphLayoutSnapshot> run,
        IReadOnlyDictionary<PdfParagraphLayoutSnapshot, double> baseFonts,
        string targetFontName,
        double contentBudget)
    {
        double low = MinimumBodyFontScale;
        double high = MaximumBodyFontScale;

        ApplyFontScaleAndMeasure(gfx, run, baseFonts, targetFontName, high);
        if (run.Sum(s => s.MeasuredHeight) <= contentBudget + 0.5)
        {
            return high;
        }

        double best = low;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            double candidate = (low + high) / 2.0;
            ApplyFontScaleAndMeasure(gfx, run, baseFonts, targetFontName, candidate);
            double totalHeight = run.Sum(s => s.MeasuredHeight);
            if (totalHeight <= contentBudget + 0.5)
            {
                best = candidate;
                low = candidate;
            }
            else
            {
                high = candidate;
            }
        }
        ApplyFontScaleAndMeasure(gfx, run, baseFonts, targetFontName, best);
        if (run.Sum(s => s.MeasuredHeight) <= contentBudget + 0.5)
            return best;

        low = EmergencyBodyFontScale;
        high = MinimumBodyFontScale;
        best = low;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            double candidate = (low + high) / 2.0;
            ApplyFontScaleAndMeasure(gfx, run, baseFonts, targetFontName, candidate);
            double totalHeight = run.Sum(s => s.MeasuredHeight);
            if (totalHeight <= contentBudget + 0.5)
            {
                best = candidate;
                low = candidate;
            }
            else
            {
                high = candidate;
            }
        }
        return best;
    }

    private static void ApplyFontScaleAndMeasure(
        XGraphics gfx,
        IReadOnlyList<PdfParagraphLayoutSnapshot> run,
        IReadOnlyDictionary<PdfParagraphLayoutSnapshot, double> baseFonts,
        string targetFontName,
        double scale)
    {
        foreach (var snapshot in run)
        {
            snapshot.Paragraph.LayoutFontSizeOverride = baseFonts[snapshot] * scale;
            snapshot.Paragraph.LayoutLineSpacingMultiplierOverride = 0;
            MeasureSnapshot(gfx, snapshot, targetFontName);
        }
    }

    private static void MeasureSnapshot(
        XGraphics gfx,
        PdfParagraphLayoutSnapshot snapshot,
        string targetFontName)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Paragraph.TranslatedText))
        {
            snapshot.MeasuredHeight = snapshot.Paragraph.Height;
            snapshot.OutputFontSize = snapshot.SourceFontSize;
            snapshot.LineCount = 0;
            snapshot.LineSpacingMultiplier = 0;
            return;
        }

        snapshot.MeasuredHeight = PdfTranslatedParagraphRenderer.RenderParagraph(
            gfx,
            snapshot.Paragraph,
            targetFontName,
            measureOnly: true,
            metricsSink: metrics =>
            {
                snapshot.OutputFontSize = metrics.EffectiveFontSize;
                snapshot.LineCount = metrics.LineCount;
                snapshot.LineSpacingMultiplier = metrics.LineSpacingMultiplier;
            });
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

    private static double CalculateSourceFontSize(PdfParagraph p)
    {
        if (p.IsPageTitle || PdfParagraphSemanticClassifier.IsHeadingParagraph(p))
        {
            if (p.SourceVisualFontSize > 0)
                return p.SourceVisualFontSize;
            return p.AllLetters.Count == 0 ? p.AverageFontSize : p.AllLetters.Max(l => l.FontSize);
        }

        if (p.AverageFontSize > 0)
            return p.AverageFontSize;
        if (p.SourceVisualFontSize > 0)
            return p.SourceVisualFontSize;
        return p.AllLetters.Count == 0 ? p.AverageFontSize : p.AllLetters.Max(l => l.FontSize);
    }

    private static string Preview(string value)
    {
        string text = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return text.Length <= 48 ? text : text[..48] + "…";
    }

    private static int ShiftHeadingObstacles(List<PdfParagraphLayoutSnapshot> snapshots, double pageHeight)
    {
        int shifted = 0;
        foreach (var heading in snapshots
                     .Where(s => IsHeading(s.Role) && !string.IsNullOrWhiteSpace(s.Paragraph.TranslatedText))
                     .OrderByDescending(s => s.Paragraph.OriginalY1))
        {
            shifted += ShiftSingleHeadingObstacle(heading, snapshots, pageHeight);
        }
        return shifted;
    }

    private static int ShiftSingleHeadingObstacle(PdfParagraphLayoutSnapshot heading, List<PdfParagraphLayoutSnapshot> snapshots, double pageHeight)
    {
        double extra = Math.Max(0, heading.MeasuredHeight - heading.Paragraph.Height);
        if (extra <= 1.0 || heading.Role == PdfParagraphSemanticRole.PageTitle) return 0;

        var sameColumn = heading.Column < 0
            ? snapshots.ToList()
            : snapshots.Where(s => s.Column == heading.Column).ToList();
        var fixedObstacles = sameColumn
            .Where(s => s.Paragraph != heading.Paragraph &&
                        (IsFixedObstacle(s.Paragraph) || IsHeading(s.Role)) &&
                        s.Paragraph.OriginalY1 < heading.Paragraph.OriginalY0)
            .OrderByDescending(s => s.Paragraph.OriginalY1)
            .ToList();
        double obstacleTop = fixedObstacles.Count > 0 ? fixedObstacles[0].Paragraph.OriginalY1 : PageBottomMargin;
        double available = heading.Paragraph.OriginalY0 - obstacleTop - Gap;
        if (extra > available + 0.5)
        {
            // ponytail: keep the PDF output; add a real compact-heading fallback if this overlap becomes common.
            return 0;
        }

        int shifted = 0;
        foreach (var candidate in sameColumn
                     .Where(s => s.Paragraph != heading.Paragraph &&
                                 s.Paragraph.OriginalY1 < heading.Paragraph.OriginalY0 &&
                                 s.Paragraph.OriginalY0 >= obstacleTop - 0.5 &&
                                 IsShiftable(s.Paragraph, pageHeight)))
        {
            candidate.Paragraph.Y0 -= extra;
            candidate.Paragraph.Y1 -= extra;
            candidate.ShiftY -= extra;
            shifted++;
        }
        return shifted;
    }
}

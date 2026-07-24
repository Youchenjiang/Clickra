using Clickra.Core.Models;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace Clickra.Core.Processors;

internal static class PdfPageParagraphBuilder
{
    private static readonly char[] WhitespaceSeparators = [' ', '\t', '\n', '\r'];

    public static List<PdfParagraph> BuildPageParagraphs(UglyToad.PdfPig.Content.Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var pageList = new List<PdfParagraph>();

        var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters).ToList();
        if (words.Count == 0)
        {
            return pageList;
        }

        var segmenter = new DocstrumBoundingBoxes();
        bool isTablePage = words.Any(w => PdfTableParagraphClassifier.IsTableCaptionWord(w, words));
        var blocks = PdfParagraphBlockMerger.GetMergedBlocks(segmenter.GetBlocks(words), page.Width, isTablePage);
        foreach (var block in blocks)
        {
            ProcessBlockLines(block, page, isTablePage, pageList);
        }

        SanitizeTextPlaceholders(pageList);

        if (page.Number == 1)
            PageOneLayoutClassifier.MergeTitleWithSubtitle(pageList, page.Height);

        // Pass 0.5: Mark table paragraphs geometrically
        PdfTableClassifier.MarkTableParagraphs(pageList, page.Width, page.Height, isTablePage);

        // Pass 0.55: Clear false-positive table marks on body paragraphs
        CleanupTableProseClassifications(pageList, page.Width);

        // Pass 1: Mark initial bypassed paragraphs (short figure labels only)
        MarkInitialDiagramParagraphs(pageList, page);

        var effectiveDiagramRegions = ClassifyDiagramAndGrayRegions(pageList, page);

        // Pass 2: Propagate bypass to nearby small/label paragraphs (e.g. annotations inside drawings)
        PropagateBypassToNearbyLabels(pageList, page);

        PostProcessDiagramAndGrayFlags(pageList, page, effectiveDiagramRegions);
        FinalizeParagraphBypassFlags(pageList, page);

        // This is the final invariant pass: no later classifier may turn a
        // short selectable workflow label back into a translatable paragraph.
        // Doing it immediately before returning the page list prevents white
        // masks from erasing labels in vector figures such as ASTER Figure 3.
        PdfDiagramLabelMarker.FinalizeShortFigureLabels(pageList, effectiveDiagramRegions);

        return pageList;
    }

    private static void MarkInitialDiagramParagraphs(List<PdfParagraph> pageList, UglyToad.PdfPig.Content.Page page)
    {
        foreach (var para in pageList)
            ClassifySingleParagraph(para, page);
    }

    private static void ClassifySingleParagraph(PdfParagraph para, UglyToad.PdfPig.Content.Page page)
    {
        if (PdfGrayPromptClassifier.IsGrayPromptBoxParagraph(para) || PdfGrayPromptClassifier.IsGrayPromptSubheading(para))
        {
            PdfGrayPromptMarker.MarkAsGrayPromptContent(para);
            return;
        }
        if (para.IsCode) return;
        if (ShouldMarkInitialDiagram(para, page))
            para.IsDiagram = true;
    }

    private static bool ShouldMarkInitialDiagram(PdfParagraph para, UglyToad.PdfPig.Content.Page page)
    {
        if (para.IsTable || !PdfDiagramMaskBuilder.OverlapsWithLargeImage(para, page)) return false;
        if (IsProtectedProseParagraph(para)) return false;
        return PdfChartLabelClassifier.IsLikelyChartLabel(para) || para.TextWithPlaceholders.Trim().Length <= 80;
    }

    private static bool IsProtectedProseParagraph(PdfParagraph para)
    {
        return PdfParagraphSemanticClassifier.IsHeadingParagraph(para) ||
               PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) ||
               PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para) ||
               PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para);
    }

    private static List<TableMaskRegion> ClassifyDiagramAndGrayRegions(List<PdfParagraph> pageList, UglyToad.PdfPig.Content.Page page)
    {
        var diagramRegions = PdfDiagramMaskBuilder.BuildProcessedDiagramMaskRegions(page, pageList);
        PdfDiagramFlagCleaner.ClearDiagramFlagOnRunningHeaders(pageList, page.Height);

        // Table grid strokes overlap cell text and falsely mark it as diagram; keep as table for redraw.
        PdfTableClassifier.ReclassifyWorkDivisionTableText(pageList, page.Width);
        PdfTableClassifier.ReclassifyAppendixFeatureTableText(pageList, page.Width);
        PdfTableClassifier.ReclassifyTableMisclassifiedProse(pageList, page.Width);
        PdfTableClassifier.MarkCompactAcademicTableBodies(
            pageList,
            page.Width,
            PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph,
            PdfParagraphSemanticClassifier.IsHeadingParagraph,
            PdfParagraphSemanticClassifier.IsAppendixSectionHeading);
        PdfTableClassifier.MarkSplitPromptPerformanceTable(pageList, PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph);
        var tableMaskForDiagram = PdfTableMaskPlanner.BuildTableMaskRegions(
            pageList.Where(p => p.IsTable).ToList(), page.Width);
        var effectiveDiagramRegions = PdfDiagramRegionGeometry.GetEffectiveDiagramMaskRegions(
            diagramRegions, tableMaskForDiagram, pageList);
        PdfDiagramLabelMarker.MarkDiagramFigureLabels(pageList, page, effectiveDiagramRegions);
        PdfDiagramFlagCleaner.ReclassifyChartLabelsMisclassifiedAsTable(pageList, effectiveDiagramRegions);
        PdfDiagramLabelMarker.ReclassifyStandaloneChartLabelsAsDiagram(pageList);
        PdfDiagramLabelMarker.FinalizeDiagramFigureLabels(pageList, effectiveDiagramRegions, page.Height);
        PdfDiagramLabelMarker.MarkWorkflowFigureLabelsAboveCaption(pageList, page.Height);
        PdfDiagramLabelMarker.MarkCodeFigureContentAboveCaption(pageList, page.Width, page.Height);
        PdfDiagramFlagCleaner.ClearDiagramFlagOnFigureCaptions(pageList);
        PdfDiagramFlagCleaner.ClearDiagramFlagOnSectionHeadings(pageList);
        PdfDiagramFlagCleaner.ReclassifyCalloutFindingsText(pageList);
        PdfTableClassifier.ReclassifyWorkDivisionTableText(pageList, page.Width);
        PdfTableClassifier.ReclassifyAppendixFeatureTableText(pageList, page.Width);

        foreach (var para in pageList)
        {
            para.IsBypassed = para.IsBypassed || para.IsCode || para.IsOnlyMath || string.IsNullOrWhiteSpace(para.TextWithPlaceholders) ||
                              PdfParagraphSemanticClassifier.IsEquationParagraph(para) || PdfTableParagraphClassifier.IsTableParagraph(para) || para.IsDiagram || para.IsTable;
        }

        return effectiveDiagramRegions;
    }

    private static void PostProcessDiagramAndGrayFlags(
        List<PdfParagraph> pageList,
        UglyToad.PdfPig.Content.Page page,
        List<TableMaskRegion> effectiveDiagramRegions)
    {
        PdfDiagramFlagCleaner.ClearDiagramFlagOnRunningHeaders(pageList, page.Height);
        PdfDiagramFlagCleaner.ClearDiagramFlagOnTranslatableProse(pageList, effectiveDiagramRegions);
        PdfDiagramLabelMarker.MarkWorkflowFigureLabelsAboveCaption(pageList, page.Height);
        PdfDiagramLabelMarker.MarkCodeFigureContentAboveCaption(pageList, page.Width, page.Height);
        PdfDiagramFlagCleaner.ClearDiagramFlagOnFigureCaptions(pageList);
        PdfDiagramFlagCleaner.ClearDiagramFlagOnSectionHeadings(pageList);
        PdfDiagramLabelMarker.MarkWorkflowBannerTextInDiagramRegions(pageList, effectiveDiagramRegions, page.Height, PdfGrayPromptClassifier.IsGrayPromptCodeParagraph);
        bool workDivisionPage = pageList.Any(p =>
            p.TextWithPlaceholders.Trim().Contains("WORK DIVISION", StringComparison.OrdinalIgnoreCase));
        var grayPromptShadedRegions = workDivisionPage
            ? new List<TableMaskRegion>()
            : PdfGrayPromptRegionBuilder.GetGrayPromptShadedRegions(effectiveDiagramRegions, page.Width, pageList);
        var effectiveGrayRegions = workDivisionPage
            ? new List<TableMaskRegion>()
            : PdfGrayPromptRegionBuilder.BuildEffectiveGrayMaskRegions(
                page, effectiveDiagramRegions, pageList, page.Width, PdfGrayPromptGeometry.ParagraphCenterInsideAnyRegion);
        if (!workDivisionPage)
        {
            // Geometry-first: mark by vector gray boxes before any heuristic clearing.
            PdfGrayPromptMarker.MarkAllParagraphsByGrayGeometry(pageList, effectiveGrayRegions, page.Height);
            PdfGrayPromptMarker.MarkGrayPromptBoxesAsCode(pageList, grayPromptShadedRegions);
            PdfGrayPromptMarker.MarkGrayPromptContentInShadedRegions(pageList, grayPromptShadedRegions);
        }
        PdfGrayPromptMarker.ClearMisclassifiedCodeFlags(pageList);

        PdfParagraphPostProcessor.MergeVerticallyAdjacentParagraphs(pageList, PdfParagraphSemanticClassifier.IsHeadingParagraph);
        // Docstrum may split wrapped ordinary prose at a short right edge.
        // Rejoin only tight, same-column line fragments before translation so
        // one visual paragraph receives one typography/reflow decision.
        PdfParagraphPostProcessor.MergeWrappedLineFragments(
            pageList,
            PdfParagraphSemanticClassifier.IsHeadingParagraph,
            page.Height);
        PdfTableClassifier.ReclassifyWorkDivisionTableText(pageList, page.Width);
        PdfTableClassifier.ReclassifyAppendixFeatureTableText(pageList, page.Width);
        PdfTableClassifier.MarkCompactAcademicTableBodies(
            pageList,
            page.Width,
            PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph,
            PdfParagraphSemanticClassifier.IsHeadingParagraph,
            PdfParagraphSemanticClassifier.IsAppendixSectionHeading);
        PdfTableClassifier.MarkSplitPromptPerformanceTable(pageList, PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph);
        // Re-apply only the strong caption-delimited rule after paragraph
        // merging and prose cleanup. A short final table section can otherwise
        // be demoted as prose even though it remains inside the same table.
        PdfTableClassifier.MarkCaptionDelimitedTableRegions(pageList, page.Width);
        PdfTableMaskPlanner.MarkParagraphsInsideTableMasksUntilStable(
            pageList,
            page.Width,
            PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph);
        if (!workDivisionPage)
        {
            PdfGrayPromptMarker.MarkAllParagraphsByGrayGeometry(pageList, effectiveGrayRegions, page.Height);
            PdfGrayPromptMarker.ClearGrayPromptContentOutsideShadedRegions(pageList, effectiveGrayRegions);
            PdfGrayPromptMarker.ClearTranslatableProseFromGrayPromptFlags(pageList, effectiveGrayRegions);
            PdfGrayPromptMarker.ClearGrayPromptFlagsBelowShadedBottom(pageList, effectiveGrayRegions, page.Width);
            PdfGrayPromptMarker.RestoreGrayPromptContinuations(pageList);
            PdfGrayPromptMarker.MarkAllParagraphsByGrayGeometry(pageList, effectiveGrayRegions, page.Height);
            PdfGrayPromptMarker.FinalizeGrayPromptContentFlags(pageList);
        }
        PdfTableClassifier.ReclassifyAppendixFeatureTableText(pageList, page.Width);
        PdfDiagramLabelMarker.MarkWorkflowFigureLabelsAboveCaption(pageList, page.Height);
        PdfDiagramLabelMarker.MarkCodeFigureContentAboveCaption(pageList, page.Width, page.Height);
        if (!workDivisionPage)
        {
            PdfGrayPromptMarker.MarkAllParagraphsByGrayGeometry(pageList, grayPromptShadedRegions, page.Height);
            PdfGrayPromptMarker.RestoreGrayPromptContinuations(pageList);
            PdfGrayPromptMarker.FinalizeGrayPromptContentFlags(pageList);
        }
    }

    private static bool IsPublicationMetadata(string finalText, int pageNumber)
        => pageNumber == 1 &&
           finalText.Contains("DOI", StringComparison.OrdinalIgnoreCase) &&
           (finalText.Contains("IEEE", StringComparison.OrdinalIgnoreCase) || finalText.Contains('©'));

    private static bool IsTinyFixedLabel(PdfParagraph para, string finalText)
        => para.AverageFontSize > 0 &&
           para.AverageFontSize <= 6.0 &&
           para.Width <= 40.0 &&
           para.Height <= 8.0 &&
           finalText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= 2;

    private static bool IsGrayPromptKeyword(string finalText)
        => finalText.Equals("EX-", StringComparison.OrdinalIgnoreCase) ||
           System.Text.RegularExpressions.Regex.IsMatch(
               finalText, @"^AMPLE\}?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)) ||
           finalText.Contains("OUTPUT FORMAT", StringComparison.OrdinalIgnoreCase);

    private static bool IsLowerCaseProseContinuation(PdfParagraph para, string leadingContinuation)
        => PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) &&
           !ReferenceSectionDetector.IsReferenceParagraph(para) &&
           !para.IsTable && !para.IsDiagram && !para.IsCode && !para.IsGrayPromptContent &&
           leadingContinuation.Length > 0 && char.IsLower(leadingContinuation[0]);

    private static void FinalizeParagraphBypassFlags(List<PdfParagraph> pageList, UglyToad.PdfPig.Content.Page page)
    {
        if (page.Number == 1)
            PageOneLayoutClassifier.ApplyAuthorBlockFlags(pageList, page.Height);

        foreach (var para in pageList)
        {
            string finalText = para.TextWithPlaceholders.Trim();

            if (IsGrayPromptKeyword(finalText))
                PdfGrayPromptMarker.MarkAsGrayPromptContent(para);

            if (page.Number == 1 && PageOneLayoutClassifier.IsAuthorBlockParagraph(para, pageList, page.Height))
            {
                para.IsBypassed = true;
                continue;
            }

            // A lower-case, full-column continuation can be marked bypassed by
            // an earlier diagram/reference heuristic even though it is body
            // prose. Restore translation eligibility before the final bypass
            // calculation; protected table/code/diagram/gray regions remain
            // excluded. This prevents source-only tail lines at page bottoms.
            string leadingContinuation = finalText.TrimStart();
            if (para.IsBypassed && IsLowerCaseProseContinuation(para, leadingContinuation))
                para.IsBypassed = false;

            // Preserve IsBypassed=true set by proximity propagation (Pass 2 above);
            // only recalculate when it is currently false.
            bool preserveProseContinuation = IsLowerCaseProseContinuation(para, leadingContinuation);
            bool pubMeta = IsPublicationMetadata(finalText, page.Number);
            bool tinyLabel = IsTinyFixedLabel(para, finalText);
            para.IsBypassed = para.IsBypassed ||
                              para.IsCode || para.IsOnlyMath || string.IsNullOrWhiteSpace(para.TextWithPlaceholders) ||
                              (!preserveProseContinuation && PdfParagraphSemanticClassifier.IsEquationParagraph(para)) ||
                              PdfTableParagraphClassifier.IsTableParagraph(para) || para.IsDiagram || para.IsTable ||
                              PdfChartLabelClassifier.IsChartTickGlyph(para) ||
                              pubMeta || tinyLabel;
        }
    }

    private static void ProcessBlockLines(
        PdfParagraphBlockMerger.MergedBlock block,
        UglyToad.PdfPig.Content.Page page,
        bool isTablePage,
        List<PdfParagraph> pageList)
    {
        var blockLines = PdfParagraph.MergeHorizontalLines(block.TextLines);
        var currentGroup = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>();
        bool? currentIsMath = null;

        double minX = block.TextLines.Count > 0 ? block.TextLines.Min(l => l.BoundingBox.Left) : 0;
        double maxX = block.TextLines.Count > 0 ? block.TextLines.Max(l => l.BoundingBox.Right) : 0;
        double blockWidth = maxX - minX;

        bool isTableBlock = IsTableBlock(blockLines, isTablePage, blockWidth, page.Width);

        var groupCtx = new LineGroupContext(block, page, isTablePage, isTableBlock);
        foreach (var line in blockLines)
        {
            (currentGroup, currentIsMath) = AddLineToCurrentGroup(line, currentGroup, currentIsMath, groupCtx, pageList);
        }

        if (currentGroup.Count > 0)
        {
            var paragraph = new PdfParagraph(currentGroup);
            pageList.Add(paragraph);
        }
    }

    private readonly record struct LineGroupContext(
        PdfParagraphBlockMerger.MergedBlock Block,
        UglyToad.PdfPig.Content.Page PigPage,
        bool IsTablePage,
        bool IsTableBlock);

    private static bool ShouldSplitBlockLine(
        UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine line,
        PdfParagraphBlockMerger.MergedBlock block,
        UglyToad.PdfPig.Content.Page page,
        bool isTablePage,
        bool isTableBlock,
        List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> currentGroup)
    {
        bool startsNew = PdfParagraphBlockMerger.StartsNewParagraphOrSection(line.Text);

        bool prevLineEndedEarly = false;
        bool prevLineWasHeading = false;
        bool isVerticalGapLarge = false;
        if (currentGroup.Count > 0)
        {
            var prevLine = currentGroup[currentGroup.Count - 1];
            if (prevLine.BoundingBox.Right < block.Right - 20.0)
            {
                prevLineEndedEarly = true;
            }
            if (PdfParagraphBlockMerger.IsHeadingLine(prevLine))
            {
                prevLineWasHeading = true;
            }

            double gapY = prevLine.BoundingBox.Bottom - line.BoundingBox.Top;
            if (gapY > 15.0)
            {
                isVerticalGapLarge = true;
            }
        }

        bool prevLineHasGap = isTablePage && currentGroup.Count > 0 && PdfTextLineGeometry.HasColumnGap(currentGroup[currentGroup.Count - 1]);
        bool currLineHasGap = isTablePage && PdfTextLineGeometry.HasColumnGap(line);
        bool crossColumnSplit = currentGroup.Count > 0 &&
            PdfPageReadingOrder.IsLineInLeftColumn(currentGroup[currentGroup.Count - 1], page.Width) !=
            PdfPageReadingOrder.IsLineInLeftColumn(line, page.Width);
        bool forceSplit = isTableBlock && currentGroup.Count > 0;

        return startsNew || isVerticalGapLarge || crossColumnSplit ||
            (prevLineEndedEarly && !prevLineWasHeading) || (prevLineWasHeading && !FontUtilities.IsLineBold(line)) ||
            prevLineHasGap || currLineHasGap || forceSplit;
    }

    private static bool IsTableBlock(
        IReadOnlyList<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> blockLines,
        bool isTablePage,
        double blockWidth,
        double pageWidth)
    {
        return blockLines.Count >= 2 &&
               (isTablePage || blockWidth < 150.0) &&
               (blockLines.Average(l => l.Words.Count) <= 3.5 ||
                (isTablePage && blockWidth < pageWidth * 0.45 &&
                 blockLines.Max(l => l.Words.Count) <= 8));
    }

    private static (List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> Group, bool? IsMath) AddLineToCurrentGroup(
        UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine line,
        List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> currentGroup,
        bool? currentIsMath,
        LineGroupContext ctx,
        List<PdfParagraph> pageList)
    {
        bool isMath = PdfParagraph.IsMathLine(line);
        bool shouldSplit = ShouldSplitBlockLine(line, ctx.Block, ctx.PigPage, ctx.IsTablePage, ctx.IsTableBlock, currentGroup);

        if (currentGroup.Count == 0)
            return (new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> { line }, isMath);

        if (isMath == currentIsMath && !shouldSplit)
        {
            currentGroup.Add(line);
            return (currentGroup, currentIsMath);
        }

        pageList.Add(new PdfParagraph(currentGroup));
        return (new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> { line }, isMath);
    }

    private static void SanitizeTextPlaceholders(List<PdfParagraph> pageList)
    {
        foreach (var para in pageList)
        {
            if (string.IsNullOrWhiteSpace(para.TextWithPlaceholders)) continue;
            string twp = para.TextWithPlaceholders.Trim();
            int artifactIdx = twp.IndexOf("):(", System.StringComparison.Ordinal);
            if (artifactIdx > 0)
            {
                para.TextWithPlaceholders = twp.Substring(0, artifactIdx).TrimEnd();
            }
        }
    }

    private static void CleanupTableProseClassifications(List<PdfParagraph> pageList, double pageWidth)
    {
        foreach (var para in pageList)
        {
            if (!para.IsTable) continue;
            string txt = para.TextWithPlaceholders.Trim();
            if (ShouldUnmarkTableProse(para, txt, pageWidth))
            {
                para.IsTable = false;
            }
        }
    }

    private static bool ShouldUnmarkTableProse(PdfParagraph para, string txt, double pageWidth)
    {
        if (string.IsNullOrWhiteSpace(txt)) return false;
        if (PdfTableMisclassifiedProseCleanup.IsLikelyTableHeader(para, txt)) return false;
        int wordCount = txt.Split(WhitespaceSeparators, StringSplitOptions.RemoveEmptyEntries).Length;
        if (PdfTableMisclassifiedProseCleanup.IsTallFullColumnProse(para, wordCount, pageWidth)) return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[A-Z]\.\s", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1))) return true;
        if (para.Width > pageWidth * 0.38 && wordCount > 10) return true;
        if (txt.StartsWith('•') || txt.StartsWith('·') || txt.StartsWith("To sum up", StringComparison.OrdinalIgnoreCase)) return true;
        if (txt.StartsWith("and ", StringComparison.OrdinalIgnoreCase) && wordCount > 3 && para.Height <= 20) return true;
        return System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d+\s+[A-Za-z]", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)) && para.Height <= 25 && para.Width > 120;
    }

    private static void PropagateBypassToNearbyLabels(List<PdfParagraph> pageList, UglyToad.PdfPig.Content.Page page)
    {
        bool pageHasDiagramLabels = pageList.Any(p => p.IsDiagram);
        int diagramLabelMaxLen = pageHasDiagramLabels ? 80 : 20;
        bool changed = true;
        while (changed)
        {
            changed = pageList
                .Where(para => !ShouldSkipBypassPropagation(para, pageList, page, diagramLabelMaxLen))
                .Any(para => TryPropagateBypassForLabel(para, pageList, page.Height));
        }
    }

    private static bool ShouldSkipBypassPropagation(
        PdfParagraph para,
        List<PdfParagraph> pageList,
        UglyToad.PdfPig.Content.Page page,
        int diagramLabelMaxLen)
    {
        if (para.IsBypassed || para.IsTable || para.IsCode) return true;
        if (page.Number == 1 && PageOneLayoutClassifier.IsAuthorBlockParagraph(para, pageList, page.Height)) return true;
        if (PdfParagraphRoleClassifier.IsRunningHeaderOrFooter(para, page.Height)) return true;
        if (PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para)) return true;
        if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) || PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para)) return true;

        bool isSmallLabel = para.TextWithPlaceholders.Length <= diagramLabelMaxLen &&
                            !PdfParagraphSemanticClassifier.IsHeadingParagraph(para) &&
                            PdfChartLabelClassifier.IsLikelyChartLabel(para);
        return !isSmallLabel;
    }

    private static bool TryPropagateBypassForLabel(PdfParagraph para, List<PdfParagraph> pageList, double pageHeight)
    {
        var anchor = pageList.FirstOrDefault(other => IsEligibleBypassAnchor(para, other, pageHeight));
        if (anchor == null) return false;

        para.IsBypassed = true;
        if (anchor.IsDiagram)
        {
            para.IsDiagram = true;
            para.IsTable = false;
        }
        return true;
    }

    private static bool IsEligibleBypassAnchor(PdfParagraph para, PdfParagraph other, double pageHeight)
    {
        if (other == para || !other.IsBypassed) return false;
        if (other.IsTable && !other.IsDiagram) return false;
        if (PdfParagraphRoleClassifier.IsRunningHeaderOrFooter(other, pageHeight)) return false;

        bool closeX = (para.X0 <= other.X1 + 30) && (para.X1 >= other.X0 - 30);
        bool closeY = (para.Y0 <= other.Y1 + 30) && (para.Y1 >= other.Y0 - 30);
        return closeX && closeY;
    }
}

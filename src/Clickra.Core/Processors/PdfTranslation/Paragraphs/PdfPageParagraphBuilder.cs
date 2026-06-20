using Clickra.Core.Models;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace Clickra.Core.Processors;

internal static class PdfPageParagraphBuilder
{
    public static List<PdfParagraph> BuildPageParagraphs(UglyToad.PdfPig.Content.Page page)
    {
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
            var blockLines = PdfParagraph.MergeHorizontalLines(block.TextLines);
            var currentGroup = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine>();
            bool? currentIsMath = null;

            double minX = block.TextLines.Count > 0 ? block.TextLines.Min(l => l.BoundingBox.Left) : 0;
            double maxX = block.TextLines.Count > 0 ? block.TextLines.Max(l => l.BoundingBox.Right) : 0;
            double blockWidth = maxX - minX;

            bool isTableBlock = blockLines.Count >= 2 &&
                                (isTablePage || blockWidth < 150.0) &&
                                (blockLines.Average(l => l.Words.Count) <= 3.5 ||
                                 (isTablePage && blockWidth < page.Width * 0.45 &&
                                  blockLines.Max(l => l.Words.Count) <= 8));

            foreach (var line in blockLines)
            {
                bool isMath = PdfParagraph.IsMathLine(line);
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

                    // Prevent DocStrum from mistakenly merging paragraphs across a large vertical gap (e.g. over 15 pt)
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

                // When the previous line is a heading, don't split on prevLineEndedEarly
                // (headings naturally end early; e.g., '2.1 Text Representation and Modality' + 'Alignment')
                bool shouldSplit = startsNew || isVerticalGapLarge || crossColumnSplit ||
                    (prevLineEndedEarly && !prevLineWasHeading) || (prevLineWasHeading && !FontUtilities.IsLineBold(line)) ||
                    prevLineHasGap || currLineHasGap || forceSplit;

                if (currentGroup.Count == 0)
                {
                    currentGroup.Add(line);
                    currentIsMath = isMath;
                }
                else if (isMath == currentIsMath && !shouldSplit)
                {
                    currentGroup.Add(line);
                }
                else
                {
                    var paragraph = new PdfParagraph(currentGroup);
                    pageList.Add(paragraph);

                    currentGroup = new List<UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine> { line };
                    currentIsMath = isMath;
                }
            }

            if (currentGroup.Count > 0)
            {
                var paragraph = new PdfParagraph(currentGroup);
                pageList.Add(paragraph);
            }
        }

        // Pass 0: Sanitize TextWithPlaceholders — remove stray '):(...)' bracket artifacts
        // These appear when AnalyzeLines absorbs the opening '(' of a parenthetical phrase
        // into a formula token, leaving a dangling '):(label)' in the text.
        // e.g. "{v0}):(Equation (1))" -> "{v0}"   or   "InfoNCE):(Equation (1))" -> "InfoNCE"
        foreach (var para in pageList)
        {
            if (string.IsNullOrWhiteSpace(para.TextWithPlaceholders)) continue;
            string twp = para.TextWithPlaceholders.Trim();
            // Find first occurrence of "):(" — a stray closing paren followed by colon+open
            int artifactIdx = twp.IndexOf("):(", System.StringComparison.Ordinal);
            if (artifactIdx > 0)
            {
                para.TextWithPlaceholders = twp.Substring(0, artifactIdx).TrimEnd();
            }
        }

        if (page.Number == 1)
            PageOneLayoutClassifier.MergeTitleWithSubtitle(pageList, page.Height);

        // Pass 0.5: Mark table paragraphs geometrically
        PdfTableClassifier.MarkTableParagraphs(pageList, page.Width, page.Height, isTablePage);

        // Pass 0.55: Clear false-positive table marks on body paragraphs
        foreach (var para in pageList)
        {
            if (!para.IsTable) continue;
            string txt = para.TextWithPlaceholders.Trim();
            int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (para.Height > 35 && wordCount > 20)
            {
                para.IsTable = false;
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[A-Z]\.\s"))
            {
                para.IsTable = false;
            }
            else if (para.Width > page.Width * 0.38 && wordCount > 10)
            {
                // Full-column prose on table pages (e.g. RQ4 intro) is not a table cell.
                para.IsTable = false;
            }
            else if (txt.StartsWith("•") || txt.StartsWith("·") ||
                     txt.StartsWith("To sum up", StringComparison.OrdinalIgnoreCase))
            {
                // Contribution bullets/headings near comparison tables (e.g. PentestAgent p2).
                para.IsTable = false;
            }
            else if (txt.StartsWith("and ", StringComparison.OrdinalIgnoreCase) && wordCount > 3 && para.Height <= 20)
            {
                // Table footnote continuation lines.
                para.IsTable = false;
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d+\s+[A-Za-z]") &&
                     para.Height <= 25 && para.Width > 120)
            {
                // Numbered footnote body (e.g. "1 AutoAttacker and PentestGPT solely rely...").
                para.IsTable = false;
            }
        }

        // Pass 1: Mark initial bypassed paragraphs (short figure labels only)
        foreach (var para in pageList)
        {
            if (PdfGrayPromptClassifier.IsGrayPromptBoxParagraph(para) || PdfGrayPromptClassifier.IsGrayPromptSubheading(para))
            {
                PdfGrayPromptMarker.MarkAsGrayPromptContent(para);
                continue;
            }
            if (para.IsCode) continue;
            if (!para.IsTable && PdfDiagramMaskBuilder.OverlapsWithLargeImage(para, page))
            {
                if (PdfParagraphSemanticClassifier.IsHeadingParagraph(para) || PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) ||
                    PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para) || PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para))
                {
                    continue;
                }
                if (PdfChartLabelClassifier.IsLikelyChartLabel(para) || para.TextWithPlaceholders.Trim().Length <= 80)
                {
                    para.IsDiagram = true;
                }
            }
        }

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

        // Pass 2: Propagate bypass to nearby small/label paragraphs (e.g. annotations inside drawings)
        bool pageHasDiagramLabels = pageList.Any(p => p.IsDiagram);
        int diagramLabelMaxLen = pageHasDiagramLabels ? 80 : 20;
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var para in pageList)
            {
                if (para.IsBypassed) continue;
                if (para.IsTable) continue;
                if (page.Number == 1 && PageOneLayoutClassifier.IsAuthorBlockParagraph(para, pageList, page.Height)) continue;
                if (para.IsCode) continue;
                if (PdfParagraphRoleClassifier.IsRunningHeaderOrFooter(para, page.Height)) continue;
                if (PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para)) continue;
                if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para)) continue;
                if (PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para)) continue;

                bool isSmallLabel = para.TextWithPlaceholders.Length <= diagramLabelMaxLen &&
                                    !PdfParagraphSemanticClassifier.IsHeadingParagraph(para) && PdfChartLabelClassifier.IsLikelyChartLabel(para);
                if (isSmallLabel)
                {
                    foreach (var other in pageList)
                    {
                        if (other == para || !other.IsBypassed) continue;
                        if (other.IsTable && !other.IsDiagram) continue;
                        if (PdfParagraphRoleClassifier.IsRunningHeaderOrFooter(other, page.Height)) continue;

                        bool closeX = (para.X0 <= other.X1 + 30) && (para.X1 >= other.X0 - 30);
                        bool closeY = (para.Y0 <= other.Y1 + 30) && (para.Y1 >= other.Y0 - 30);

                        if (closeX && closeY)
                        {
                            para.IsBypassed = true;
                            if (other.IsDiagram)
                            {
                                para.IsDiagram = true;
                                para.IsTable = false;
                            }
                            changed = true;
                            break;
                        }
                    }
                }
            }
        }

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
        PdfTableClassifier.ReclassifyWorkDivisionTableText(pageList, page.Width);
        PdfTableClassifier.ReclassifyAppendixFeatureTableText(pageList, page.Width);
        PdfTableClassifier.MarkCompactAcademicTableBodies(
            pageList,
            page.Width,
            PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph,
            PdfParagraphSemanticClassifier.IsHeadingParagraph,
            PdfParagraphSemanticClassifier.IsAppendixSectionHeading);
        PdfTableClassifier.MarkSplitPromptPerformanceTable(pageList, PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph);
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

        if (page.Number == 1)
            PageOneLayoutClassifier.ApplyAuthorBlockFlags(pageList, page.Height);

        foreach (var para in pageList)
        {
            string finalText = para.TextWithPlaceholders.Trim();
            if (finalText.Equals("EX-", StringComparison.OrdinalIgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(
                    finalText, @"^AMPLE\}?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                finalText.Contains("OUTPUT FORMAT", StringComparison.OrdinalIgnoreCase))
            {
                PdfGrayPromptMarker.MarkAsGrayPromptContent(para);
            }

            if (page.Number == 1 && PageOneLayoutClassifier.IsAuthorBlockParagraph(para, pageList, page.Height))
            {
                para.IsBypassed = true;
                continue;
            }
            // Preserve IsBypassed=true set by proximity propagation (Pass 2 above);
            // only recalculate when it is currently false.
            para.IsBypassed = para.IsBypassed ||
                              para.IsCode || para.IsOnlyMath || string.IsNullOrWhiteSpace(para.TextWithPlaceholders) ||
                              PdfParagraphSemanticClassifier.IsEquationParagraph(para) || PdfTableParagraphClassifier.IsTableParagraph(para) || para.IsDiagram || para.IsTable ||
                              PdfChartLabelClassifier.IsChartTickGlyph(para);
        }

        return pageList;
    }
}

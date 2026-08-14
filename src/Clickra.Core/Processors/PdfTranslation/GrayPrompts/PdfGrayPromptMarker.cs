using Clickra.Core.Models;

namespace Clickra.Core.Processors;

internal static class PdfGrayPromptMarker
{
    /// <summary>
    /// Gray prompt / system-message boxes are treated like code: bypass, keep English, no strip.
    /// </summary>
    public static void MarkGrayPromptBoxesAsCode(
        List<PdfParagraph> pageList, IReadOnlyList<TableMaskRegion> grayShadedRegions)
    {
        bool inGrayPromptBlock = false;
        PdfParagraph? anchor = null;
        foreach (var para in pageList.OrderByDescending(p => p.Y1))
        {
            if (PdfGrayPromptClassifier.IsGrayPromptBoxParagraph(para))
            {
                if (inGrayPromptBlock && anchor != null && PdfGrayPromptClassifier.SharesGrayPromptColumn(para, anchor))
                {
                    MarkAsGrayPromptContent(para);
                    anchor = para;
                    continue;
                }

                inGrayPromptBlock = true;
                anchor = para;
                MarkAsGrayPromptContent(para);
                continue;
            }

            if (!inGrayPromptBlock || anchor == null) continue;

            if (!PdfGrayPromptClassifier.SharesGrayPromptColumn(para, anchor)) continue;

            // Descending-Y iteration: only extend block to paragraphs below the anchor.
            if (para.Y1 >= anchor.Y1 - 1) continue;

            double gapBelow = anchor.Y0 - para.Y1;
            if (gapBelow > 45)
            {
                inGrayPromptBlock = false;
                anchor = null;
                continue;
            }

            if (PdfGrayPromptClassifier.IsGrayPromptSubheading(para))
            {
                MarkAsGrayPromptContent(para);
                anchor = para;
                continue;
            }

            if (PdfGrayPromptClassifier.IsGrayPromptBoxContinuationParagraph(para, anchor))
            {
                MarkAsGrayPromptContent(para);
                anchor = para;
                continue;
            }

            if (grayShadedRegions.Count > 0 &&
                (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) || PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para)) &&
                !PdfGrayPromptGeometry.IsParagraphInsideGrayShadedRegion(para, grayShadedRegions))
            {
                inGrayPromptBlock = false;
                anchor = null;
                continue;
            }

            string txt = para.TextWithPlaceholders.Trim();
            if (PdfParagraphSemanticClassifier.IsHeadingParagraph(para) ||
                (PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para) && PdfGrayPromptClassifier.SharesGrayPromptColumn(para, anchor)) ||
                System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d+\.\d+\s") ||
                System.Text.RegularExpressions.Regex.IsMatch(txt, @"^[A-Z]\.\d+\s"))
            {
                inGrayPromptBlock = false;
                anchor = null;
                continue;
            }

            if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) || PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para))
            {
                if (PdfGrayPromptClassifier.IsSectionIntroProse(para))
                {
                    inGrayPromptBlock = false;
                    anchor = null;
                    continue;
                }
                if (grayShadedRegions.Count > 0 &&
                    PdfGrayPromptGeometry.IsParagraphInsideGrayShadedRegion(para, grayShadedRegions))
                {
                    MarkAsGrayPromptContent(para);
                    anchor = para;
                    continue;
                }
                inGrayPromptBlock = false;
                anchor = null;
                continue;
            }

            MarkAsGrayPromptContent(para);
            anchor = para;
        }
    }

    public static void MarkAsGrayPromptContent(PdfParagraph para)
    {
        para.IsCode = true;
        para.IsGrayPromptContent = true;
        para.IsDiagram = false;
        para.IsTable = false;
    }

    /// <summary>Force gray-prompt bypass for any paragraph whose center lies inside gray geometry.</summary>
    public static void MarkAllParagraphsByGrayGeometry(
        List<PdfParagraph> pageList, IReadOnlyList<TableMaskRegion> grayRegions, double pageHeight)
    {
        if (grayRegions.Count == 0) return;
        var expanded = PdfGrayPromptRegionBuilder.ExpandGrayShadedRegions(grayRegions, 2.0);
        foreach (var para in pageList)
        {
            if (PdfParagraphRoleClassifier.IsRunningHeaderOrFooter(para, pageHeight)) continue;
            if (PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para)) continue;
            if (PdfParagraphSemanticClassifier.IsHeadingParagraph(para) || PdfParagraphSemanticClassifier.IsAppendixSectionHeading(para)) continue;
            bool insideGray = PdfGrayPromptGeometry.ParagraphCenterInsideAnyRegion(para, expanded) ||
                PdfGrayPromptGeometry.IsParagraphInsideGrayShadedRegion(para, grayRegions);
            if (PdfGrayPromptGeometry.IsParagraphInsideAnchoredGrayPromptRegion(para, grayRegions, pageList))
            {
                MarkAsGrayPromptContent(para);
                continue;
            }
            if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) || PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para)) continue;
            if (insideGray &&
                (PdfGrayPromptClassifier.IsGrayPromptBoxParagraph(para) ||
                 PdfGrayPromptClassifier.IsGrayPromptSubheading(para) ||
                 PdfGrayPromptClassifier.IsGrayPromptBoxContinuationParagraph(para, null)))
            {
                MarkAsGrayPromptContent(para);
            }
        }
    }

    public static void MarkGrayPromptContentInShadedRegions(
        List<PdfParagraph> pageList, IReadOnlyList<TableMaskRegion> grayRegions)
    {
        if (grayRegions.Count == 0) return;
        foreach (var para in pageList)
        {
            if (para.IsTable) continue;
            if (PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para)) continue;
            if (PdfParagraphSemanticClassifier.IsHeadingParagraph(para) || PdfParagraphSemanticClassifier.IsAppendixSectionHeading(para)) continue;
            if (PdfGrayPromptClassifier.IsGrayPromptSubheading(para)) continue;
            string txt = para.TextWithPlaceholders.Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d+\.\d+\s")) continue;

            if (PdfGrayPromptGeometry.IsParagraphInsideGrayShadedRegion(para, grayRegions))
            {
                if (PdfGrayPromptGeometry.IsParagraphInsideAnchoredGrayPromptRegion(para, grayRegions, pageList))
                {
                    MarkAsGrayPromptContent(para);
                    continue;
                }
                if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) || PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para)) continue;
                if (System.Text.RegularExpressions.Regex.IsMatch(txt, @"^\d+\)\s+[A-Za-z]") &&
                    !PdfGrayPromptClassifier.IsGrayPromptBoxContinuationParagraph(para, null))
                {
                    continue;
                }
                MarkAsGrayPromptContent(para);
                continue;
            }

            bool overlapsGray = PdfTableMaskPlanner.ParagraphOverlapsAnyTableMask(
                para.X0, para.Y0, para.X1, para.Y1, PdfGrayPromptRegionBuilder.ExpandGrayShadedRegions(grayRegions), 15.0, 3.0);
            if (overlapsGray && PdfGrayPromptClassifier.IsGrayPromptBoxContinuationParagraph(para, null))
            {
                MarkAsGrayPromptContent(para);
                continue;
            }

            if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) || PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para)) continue;

            if (PdfGrayPromptClassifier.IsGrayPromptSubheading(para) &&
                PdfGrayPromptGeometry.IsParagraphInsideGrayShadedRegion(para, grayRegions))
            {
                MarkAsGrayPromptContent(para);
                continue;
            }

            if (PdfGrayPromptClassifier.IsGrayPromptBoxContinuationParagraph(para, null) &&
                PdfGrayPromptGeometry.IsParagraphInsideGrayShadedRegion(para, grayRegions))
            {
                MarkAsGrayPromptContent(para);
            }
        }
    }

    /// <summary>Strip gray/code flags from column body clearly below the shaded vector box bottom.</summary>
    public static void ClearGrayPromptFlagsBelowShadedBottom(
        List<PdfParagraph> pageList, IReadOnlyList<TableMaskRegion> grayRegions, double pageWidth)
    {
        if (grayRegions.Count == 0) return;
        double center = pageWidth / 2.0;
        bool leftColumnGrayBox = grayRegions.Any(r => (r.X0 + r.X1) / 2.0 < center - 8);
        if (!leftColumnGrayBox) return;
        double shadedBottom = grayRegions.Where(r => (r.X0 + r.X1) / 2.0 < center - 8).Min(r => r.Y0);
        foreach (var para in pageList)
        {
            if (para.Y1 >= shadedBottom - 6) continue;
            if ((para.X0 + para.X1) / 2.0 >= center - 8) continue;
            if (PdfGrayPromptClassifier.IsGrayPromptSubheading(para)) continue;
            string txt = para.TextWithPlaceholders.Trim();
            if (txt.StartsWith("You should", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("Use Nmap", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("Use your ", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("LLM:", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("User:", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("{Information", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("You're", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("You\u2019re", StringComparison.OrdinalIgnoreCase) ||
                txt.Equals("EX-", StringComparison.OrdinalIgnoreCase) ||
                txt.EndsWith("AMPLE}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            para.IsGrayPromptContent = false;
            para.IsCode = false;
        }
    }

    public static void ClearGrayPromptContentOutsideShadedRegions(
        List<PdfParagraph> pageList, IReadOnlyList<TableMaskRegion> grayRegions)
    {
        foreach (var para in pageList)
        {
            if (!para.IsGrayPromptContent) continue;
            if (PdfGrayPromptClassifier.IsGrayPromptBoxParagraph(para)) continue;
            if (grayRegions.Count > 0)
            {
                double shadedBottom = grayRegions.Min(r => r.Y0);
                if (para.Y1 < shadedBottom - 8 &&
                    !PdfGrayPromptClassifier.IsGrayPromptBoxContinuationParagraph(para, null) &&
                    !PdfGrayPromptClassifier.IsGrayPromptSubheading(para))
                {
                    para.IsGrayPromptContent = false;
                    para.IsCode = false;
                    continue;
                }
            }
            if (grayRegions.Count > 0 &&
                !PdfGrayPromptGeometry.IsParagraphInsideGrayShadedRegion(para, grayRegions) &&
                !PdfGrayPromptClassifier.IsGrayPromptBoxContinuationParagraph(para, null) &&
                !PdfGrayPromptClassifier.IsGrayPromptSubheading(para))
            {
                string txt = para.TextWithPlaceholders.Trim();
                int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                bool multiSentenceBody = PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) || PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para) ||
                    (wordCount >= 8 && para.Width > 100 && txt.IndexOf('.') >= 0 && txt.Any(char.IsLower));
                if (multiSentenceBody)
                {
                    para.IsGrayPromptContent = false;
                    para.IsCode = false;
                    continue;
                }
            }
            if (grayRegions.Count > 0 && PdfGrayPromptGeometry.IsParagraphInsideGrayShadedRegion(para, grayRegions)) continue;
            if (PdfGrayPromptClassifier.HasNearbyGrayPromptAbove(para, pageList)) continue;
            if (PdfParagraphSemanticClassifier.IsHeadingParagraph(para))
            {
                para.IsGrayPromptContent = false;
                para.IsCode = false;
                continue;
            }
            if (PdfParagraphSemanticClassifier.IsAppendixSectionHeading(para))
            {
                para.IsGrayPromptContent = false;
                para.IsCode = false;
                continue;
            }
        }
    }

    /// <summary>Body prose must never remain flagged as gray prompt content when outside shaded boxes.</summary>
    public static void ClearTranslatableProseFromGrayPromptFlags(
        List<PdfParagraph> pageList, IReadOnlyList<TableMaskRegion> grayRegions)
    {
        foreach (var para in pageList)
        {
            if (!para.IsGrayPromptContent && !para.IsCode) continue;
            if (PdfGrayPromptClassifier.IsGrayPromptBoxParagraph(para)) continue;
            if (!PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) && !PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para)) continue;
            if (PdfGrayPromptClassifier.IsSectionIntroProse(para))
            {
                para.IsGrayPromptContent = false;
                para.IsCode = false;
                continue;
            }
            if (PdfGrayPromptClassifier.IsGrayPromptSubheading(para)) continue;
            if (grayRegions.Count > 0 && PdfGrayPromptGeometry.IsParagraphInsideGrayShadedRegion(para, grayRegions)) continue;
            if (PdfGrayPromptClassifier.HasNearbyGrayPromptAbove(para, pageList)) continue;
            if (PdfGrayPromptClassifier.IsGrayPromptBoxContinuationParagraph(para, null) &&
                grayRegions.Count > 0 &&
                PdfTableMaskPlanner.ParagraphOverlapsAnyTableMask(
                    para.X0, para.Y0, para.X1, para.Y1,
                    PdfGrayPromptRegionBuilder.ExpandGrayShadedRegions(grayRegions), 15.0, 3.0))
            {
                continue;
            }
            if (PdfGrayPromptClassifier.IsGrayPromptBoxContinuationParagraph(para, null) &&
                grayRegions.Count > 0 &&
                PdfGrayPromptGeometry.IsParagraphInsideGrayShadedRegion(para, grayRegions))
            {
                continue;
            }
            para.IsGrayPromptContent = false;
            para.IsCode = false;
        }
    }

    /// <summary>Re-apply gray flags on prompt continuations cleared by translatable-prose heuristics (p4/p7/p14).</summary>
    public static void RestoreGrayPromptContinuations(List<PdfParagraph> pageList)
    {
        foreach (var para in pageList)
        {
            if (para.IsGrayPromptContent) continue;
            string txt = para.TextWithPlaceholders.Trim();
            if (txt.EndsWith("AMPLE}", StringComparison.OrdinalIgnoreCase) ||
                txt.Equals("EX-", StringComparison.OrdinalIgnoreCase))
            {
                MarkAsGrayPromptContent(para);
                continue;
            }
            if ((txt.StartsWith("LLM:", StringComparison.OrdinalIgnoreCase) ||
                 txt.StartsWith("User:", StringComparison.OrdinalIgnoreCase) ||
                 txt.StartsWith("{Information", StringComparison.OrdinalIgnoreCase)) &&
                HasNearbyGrayPromptTitleAbove(para, pageList, 140))
            {
                MarkAsGrayPromptContent(para);
                continue;
            }
            if (!PdfGrayPromptClassifier.IsGrayPromptBoxContinuationParagraph(para, null)) continue;
            if (!PdfGrayPromptClassifier.HasNearbyGrayPromptAbove(para, pageList, 65)) continue;
            if (PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para) || PdfParagraphSemanticClassifier.IsHeadingParagraph(para)) continue;
            MarkAsGrayPromptContent(para);
        }
    }

    private static bool HasNearbyGrayPromptTitleAbove(
        PdfParagraph para, List<PdfParagraph> pageList, double maxGap)
    {
        foreach (var other in pageList)
        {
            if (!PdfGrayPromptClassifier.IsGrayPromptBoxParagraph(other)) continue;
            if (!PdfGrayPromptClassifier.SharesGrayPromptColumn(para, other)) continue;
            double gap = other.Y0 - para.Y1;
            if (gap >= -2 && gap <= maxGap) return true;
        }
        return false;
    }

    /// <summary>Gray prompt boxes bypass as code, never as diagram; no Pass 1 white masks.</summary>
    public static void FinalizeGrayPromptContentFlags(List<PdfParagraph> pageList)
    {
        foreach (var para in pageList)
        {
            if (IsHeadingOrAppendixSection(para))
            {
                // A prompt line ending in a colon (e.g. "Generate a concise
                // summary ... questions:") can be misread as a heading. If the
                // gray-prompt block scan already flagged it as continuation
                // content, that stronger signal wins; only clear headings that
                // were never part of a gray block.
                if (para.IsGrayPromptContent && PdfGrayPromptClassifier.IsGrayPromptBoxContinuationParagraph(para, null))
                {
                    continue;
                }
                ClearHeadingGrayPromptFlags(para);
                continue;
            }

            if (IsGrayPromptBoxOrSubheading(para))
                para.IsDiagram = false;

            if (!para.IsGrayPromptContent) continue;
            para.IsCode = true;
            para.IsDiagram = false;
            para.IsTable = false;
        }
    }

    private static bool IsHeadingOrAppendixSection(PdfParagraph para)
        => PdfParagraphSemanticClassifier.IsHeadingParagraph(para) || PdfParagraphSemanticClassifier.IsAppendixSectionHeading(para);

    private static bool IsGrayPromptBoxOrSubheading(PdfParagraph para)
        => PdfGrayPromptClassifier.IsGrayPromptBoxParagraph(para) || PdfGrayPromptClassifier.IsGrayPromptSubheading(para);

    private static void ClearHeadingGrayPromptFlags(PdfParagraph para)
    {
        para.IsGrayPromptContent = false;
        if (!PdfGrayPromptClassifier.IsGrayPromptBoxParagraph(para) && !PdfGrayPromptClassifier.IsGrayPromptSubheading(para))
        {
            para.IsCode = false;
        }
    }

    /// <summary>
    /// Undo false-positive IsCode from loose prompt heuristics on body prose and figure labels.
    /// </summary>
    public static void ClearMisclassifiedCodeFlags(List<PdfParagraph> pageList)
    {
        foreach (var para in pageList)
        {
            if (!para.IsCode) continue;
            if (PdfGrayPromptClassifier.IsMisclassifiedPromptCode(para))
            {
                if (para.IsGrayPromptContent) continue;
                para.IsCode = false;
                continue;
            }
            if (para.IsDiagram && !PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(para))
            {
                para.IsCode = false;
                continue;
            }
            if (PdfChartLabelClassifier.IsLikelyChartLabel(para) && !PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(para))
            {
                para.IsCode = false;
            }
        }
    }
}

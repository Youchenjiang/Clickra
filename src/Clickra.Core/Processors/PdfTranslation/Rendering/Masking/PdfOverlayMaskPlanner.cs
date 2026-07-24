using Clickra.Core.Models;
using PdfSharp.Drawing;

namespace Clickra.Core.Processors;

internal static class PdfOverlayMaskPlanner
{
    /// <summary>
    /// Cap the top edge of a white mask so upward growth cannot paint over gray prompt shaded boxes.
    /// </summary>
    public static double ClampMaskTopBelowGrayShadedRegions(
        double maskX0, double maskY0, double maskX1, double maskY1,
        IReadOnlyList<TableMaskRegion> grayRegions,
        double pageWidth = 0,
        double minOverlapX = 10.0)
    {
        if (grayRegions.Count == 0) return maskY1;
        const double clearance = 4.0;
        double clampedY1 = maskY1;
        foreach (var region in grayRegions)
        {
            if (pageWidth > 0 &&
                !PdfMaskGeometry.ParagraphSharesColumnWithRegion(maskX0, maskX1, region, pageWidth, minOverlapX))
            {
                continue;
            }

            double overlapX = Math.Min(maskX1, region.X1) - Math.Max(maskX0, region.X0);
            if (overlapX < minOverlapX) continue;

            if (maskY0 < region.Y0 && clampedY1 > region.Y0 - clearance)
            {
                clampedY1 = Math.Min(clampedY1, region.Y0 - clearance);
            }
        }
        return clampedY1;
    }

    /// <summary>Prevent a lower paragraph's white mask from growing into the bbox above (same column).</summary>
    public static double ClampMaskTopBelowNeighboringParagraphs(
        double maskX0, double maskY0, double maskX1, double maskY1,
        PdfParagraph para, IReadOnlyList<PdfParagraph> allParas, double pageWidth)
    {
        const double clearance = 2.0;
        double center = pageWidth / 2.0;
        bool leftCol = (para.X0 + para.X1) / 2.0 < center - 8;
        double clampedY1 = maskY1;
        foreach (var other in allParas)
        {
            if (ReferenceEquals(other, para) || other.IsBypassed) continue;
            bool otherLeft = (other.X0 + other.X1) / 2.0 < center - 8;
            if (otherLeft != leftCol) continue;
            if (other.Y0 <= para.Y1 + 1) continue;
            if (maskY1 > other.Y0 - clearance)
                clampedY1 = Math.Min(clampedY1, other.Y0 - clearance);
        }
        return clampedY1;
    }

    /// <summary>
    /// Gray geometry may suppress Pass 1/2 only when paragraph center/letters are inside the shaded box —
    /// loose bbox overlap must not delete translatable section body (PentestAgent p7 §3.5).
    /// </summary>
    public static bool ShouldSuppressOverlayForGrayGeometry(
        PdfParagraph para, IReadOnlyList<TableMaskRegion> effectiveGrayMaskRegions)
    {
        if (effectiveGrayMaskRegions.Count == 0) return false;
        bool insideGray = PdfGrayPromptGeometry.ParagraphCenterInsideAnyRegion(para, effectiveGrayMaskRegions) ||
                          PdfGrayPromptGeometry.IsParagraphInsideGrayShadedRegion(para, effectiveGrayMaskRegions);
        if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) || PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para) ||
            PdfParagraphSemanticClassifier.IsHeadingParagraph(para) || PdfParagraphSemanticClassifier.IsAppendixSectionHeading(para))
        {
            // Only gray-prompt paragraphs skip overlay; spurious vector gray boxes
            // (e.g. PentestAgent p4 right-column EffectiveGrayMaskRegion) must not
            // strip-and-skip translatable §2.3 body prose.
            if (para.IsGrayPromptContent || PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(para))
                return insideGray;
            return false;
        }
        if (insideGray) return true;
        return PdfTableMaskPlanner.ParagraphOverlapsAnyTableMask(
            para.X0, para.Y0, para.X1, para.Y1,
            PdfGrayPromptRegionBuilder.ExpandGrayShadedRegions(effectiveGrayMaskRegions), 8.0, 2.0);
    }

    /// <summary>
    /// Cap the top edge of a white mask so upward growth cannot paint over diagram/chart vectors.
    /// </summary>
    public static double ClampMaskTopBelowDiagrams(
        double maskX0, double maskY0, double maskX1, double maskY1,
        IReadOnlyList<TableMaskRegion> regions,
        double pageWidth = 0,
        double minOverlapX = 10.0)
    {
        const double clearance = 2.0;
        double clampedY1 = maskY1;
        foreach (var region in regions)
        {
            if (pageWidth > 0 &&
                !PdfMaskGeometry.ParagraphSharesColumnWithRegion(maskX0, maskX1, region, pageWidth, minOverlapX))
            {
                continue;
            }

            double overlapX = Math.Min(maskX1, region.X1) - Math.Max(maskX0, region.X0);
            if (overlapX < minOverlapX) continue;

            // Mask sits below the diagram region; cap top before diagram bottom edge.
            if (maskY0 < region.Y0 && clampedY1 > region.Y0 - clearance)
            {
                clampedY1 = Math.Min(clampedY1, region.Y0 - clearance);
            }
        }
        return clampedY1;
    }

    /// <summary>
    /// Clip translated body text so it cannot extend into a figure region (below or in the adjacent column).
    /// </summary>
    public static XGraphicsState? BeginClipRenderAboveDiagramBelow(
        XGraphics gfx, PdfParagraph para, double pageHeight,
        IReadOnlyList<TableMaskRegion> diagramMaskRegions,
        IReadOnlyList<PdfParagraph> pageParagraphs,
        double renderedHeight,
        double pageWidth)
    {
        // Retained as a compatibility shim for older callers. The old implementation
        // applied a broad IntersectClip and silently removed translated lines. New
        // rendering uses reflow plus mask geometry and must never enter this path.
        return null;
    }

    /// <summary>Tight figure bounds from captions and diagram labels for body-text clip/mask.</summary>
    public static List<TableMaskRegion> GetFigureClipRegions(
        IReadOnlyList<PdfParagraph> pageParagraphs,
        IReadOnlyList<TableMaskRegion> diagramMaskRegions,
        double pageWidth)
    {
        var clips = new List<TableMaskRegion>();
        double center = pageWidth / 2.0;
        foreach (var para in pageParagraphs)
        {
            if (!PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para)) continue;
            string t = para.TextWithPlaceholders.Trim();
            if (!t.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) &&
                !t.StartsWith("Fig.", StringComparison.OrdinalIgnoreCase) &&
                !t.StartsWith("Fig ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool rightCol = para.X0 >= center - 20;
            TableMaskRegion? tightDiagram = null;
            foreach (var region in diagramMaskRegions)
            {
                double regionCenter = (region.X0 + region.X1) / 2.0;
                bool regionRightCol = regionCenter >= center - 20;
                if (regionRightCol != rightCol) continue;
                // PdfPig coordinates grow upward. A conventional figure sits above its
                // caption, so compare the figure's lower edge with the caption's upper edge.
                // Using region.Y1 against caption.Y0 rejects the real figure and creates a
                // fallback clip below the caption, truncating masks for nearby inline math.
                if (region.Y0 < para.Y1 - 8 || region.Y0 > para.Y1 + 200) continue;
                if (!tightDiagram.HasValue || region.Y0 < tightDiagram.Value.Y0)
                {
                    tightDiagram = region;
                }
            }

            if (tightDiagram.HasValue)
            {
                clips.Add(tightDiagram.Value);
                continue;
            }

            double y0 = Math.Max(40, para.Y0 - 100);
            double y1 = para.Y1 + 10;
            double x0 = rightCol ? center + 5 : 40;
            double x1 = rightCol ? pageWidth - 40 : center - 5;
            clips.Add(new TableMaskRegion(x0, y0, x1, y1));
        }

        foreach (var para in pageParagraphs.Where(p => p.IsDiagram))
        {
            if (PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para)) continue;
            bool rightCol = para.X0 >= center - 20;
            double padX = 6;
            double padY = 4;
            double x0 = Math.Max(rightCol ? center + 5 : 40, para.X0 - padX);
            double x1 = Math.Min(rightCol ? pageWidth - 40 : center - 5, para.X1 + padX);
            double y0 = para.Y0 - padY;
            double y1 = para.Y1 + padY;
            clips.Add(new TableMaskRegion(x0, y0, x1, y1));
        }

        if (clips.Count > 0)
        {
            return PdfDiagramMaskBuilder.BuildDiagramMaskRegions(clips);
        }

        return diagramMaskRegions
            .Select(r =>
            {
                double regionCenter = (r.X0 + r.X1) / 2.0;
                bool rightCol = regionCenter >= center - 20;
                double x0 = rightCol ? Math.Max(center + 5, r.X0) : Math.Max(40, r.X0);
                double x1 = rightCol ? Math.Min(pageWidth - 40, r.X1) : Math.Min(center - 5, r.X1);
                return new TableMaskRegion(x0, r.Y0, x1, r.Y1);
            })
            .Where(r => (r.Y1 - r.Y0) <= 360 && r.X1 > r.X0 + 20)
            .ToList();
    }

    /// <summary>
    /// Skip white masks / translated overlay only when a paragraph is a diagram label or
    /// substantially inside a figure region — not when column body text barely touches the gutter.
    /// </summary>
    public static bool ShouldProtectDiagramRegionFromParagraph(
        PdfParagraph para, IReadOnlyList<TableMaskRegion> diagramMaskRegions,
        IReadOnlyList<PdfParagraph>? pageParagraphs = null, double pageWidth = 0)
    {
        var protectRegions = pageParagraphs != null && pageWidth > 0
            ? GetFigureClipRegions(pageParagraphs, diagramMaskRegions, pageWidth)
            : diagramMaskRegions;
        if (protectRegions.Count == 0) return false;
        if (para.IsDiagram) return true;
        if (para.IsCode && PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(para)) return true;
        if (PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para)) return false;
        if (PdfGrayPromptClassifier.IsGrayPromptBoxParagraph(para)) return false;
        // Body prose, callouts, and section headings always receive Pass 1 mask + Pass 2 overlay.
        if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) || PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para) ||
            PdfParagraphSemanticClassifier.IsHeadingParagraph(para) || PdfParagraphSemanticClassifier.IsAppendixSectionHeading(para))
        {
            return false;
        }

        string txt = para.TextWithPlaceholders.Trim();
        int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        if (!PdfChartLabelClassifier.IsLikelyChartLabel(para) && wordCount >= 8 && para.Width >= 100 && txt.Any(char.IsLower))
        {
            return false;
        }
        if (!PdfChartLabelClassifier.IsLikelyChartLabel(para) && para.Width >= 120 && txt.Length >= 25 && txt.Any(char.IsLower))
        {
            return false;
        }

        double letterRatio = PdfDiagramRegionGeometry.ParagraphLetterOverlapRatio(para, protectRegions);
        if (letterRatio >= 0.4) return true;

        if (PdfChartLabelClassifier.IsLikelyChartLabel(para))
        {
            foreach (var region in protectRegions)
            {
                if (pageWidth > 0 &&
                    !PdfMaskGeometry.ParagraphSharesColumnWithRegion(para.X0, para.X1, region, pageWidth, 15.0))
                {
                    continue;
                }
                if (PdfTableMaskPlanner.ParagraphOverlapsTableMask(para.X0, para.Y0, para.X1, para.Y1,
                        region.X0, region.Y0, region.X1, region.Y1, 15.0, 3.0))
                {
                    return true;
                }
            }
        }

        if (txt.Length <= 80)
        {
            double cx = para.X0 + para.Width / 2;
            double cy = para.Y0 + para.Height / 2;
            foreach (var region in protectRegions)
            {
                if (pageWidth > 0 &&
                    !PdfMaskGeometry.ParagraphSharesColumnWithRegion(para.X0, para.X1, region, pageWidth, 15.0))
                {
                    continue;
                }
                if (cx >= region.X0 && cx <= region.X1 && cy >= region.Y0 && cy <= region.Y1)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

using Clickra.Core.Models;

namespace Clickra.Core.Processors;

internal static class PdfGrayPromptGeometry
{
    /// <summary>Strict geometry test: any mask pixel overlap with gray box blocks Pass 1 white paint.</summary>
    public static bool MaskRectIntersectsAnyGrayRegion(
        double maskX0, double maskY0, double maskX1, double maskY1,
        IReadOnlyList<TableMaskRegion> grayRegions)
    {
        if (grayRegions.Count == 0) return false;
        foreach (var region in PdfGrayPromptRegionBuilder.ExpandGrayShadedRegions(grayRegions, 16.0))
        {
            double overlapX = Math.Min(maskX1, region.X1) - Math.Max(maskX0, region.X0);
            double overlapY = Math.Min(maskY1, region.Y1) - Math.Max(maskY0, region.Y0);
            if (overlapX > 0.5 && overlapY > 0.5)
            {
                return true;
            }
        }
        return false;
    }

    public static bool MaskRectOverlapsPageOneAuthorBand(
        double maskX0, double maskY0, double maskX1, double maskY1,
        double titleBottom, double abstractTop)
    {
        if (titleBottom <= abstractTop) return false;
        double overlapY = Math.Min(maskY1, titleBottom) - Math.Max(maskY0, abstractTop);
        return overlapY > 0.5;
    }

    public static bool MaskRectOverlapsGrayRegions(
        double maskX0, double maskY0, double maskX1, double maskY1,
        IReadOnlyList<TableMaskRegion> grayRegions,
        double pageWidth = 0)
    {
        if (grayRegions.Count == 0) return false;
        var expanded = PdfGrayPromptRegionBuilder.ExpandGrayShadedRegions(grayRegions, 2.0);
        foreach (var region in expanded)
        {
            if (pageWidth > 0 &&
                !PdfMaskGeometry.ParagraphSharesColumnWithRegion(maskX0, maskX1, region, pageWidth, 8.0))
            {
                continue;
            }
            double overlapX = Math.Min(maskX1, region.X1) - Math.Max(maskX0, region.X0);
            double overlapY = Math.Min(maskY1, region.Y1) - Math.Max(maskY0, region.Y0);
            if (overlapX > 0.5 && overlapY > 0.5)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Clip or reject a mask rect so it cannot paint over gray prompt shaded areas.</summary>
    public static bool TryClipMaskBelowGrayRegions(
        ref double maskX0, ref double maskY0, ref double maskX1, ref double maskY1,
        IReadOnlyList<TableMaskRegion> grayRegions, double pageWidth)
    {
        if (grayRegions.Count == 0) return maskY1 > maskY0 + 0.5;
        const double clearance = 4.0;
        foreach (var region in grayRegions.OrderBy(r => r.Y0))
        {
            double overlapX = Math.Min(maskX1, region.X1) - Math.Max(maskX0, region.X0);
            if (overlapX < 8.0) continue;

            if (maskY0 >= region.Y0 - 0.5 && maskY1 <= region.Y1 + 0.5)
            {
                return false;
            }

            if (maskY1 > region.Y0 + 0.5 && maskY0 < region.Y1)
            {
                maskY1 = Math.Min(maskY1, region.Y0 - clearance);
            }
        }
        return maskY1 > maskY0 + 0.5;
    }

    public static bool ParagraphCenterInsideAnyRegion(
        PdfParagraph para, IReadOnlyList<TableMaskRegion> regions)
    {
        double cx = para.X0 + para.Width / 2;
        double cy = para.Y0 + para.Height / 2;
        foreach (var region in regions)
        {
            if (cx >= region.X0 && cx <= region.X1 && cy >= region.Y0 && cy <= region.Y1)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Paragraph center or majority of letters inside shaded gray box only.</summary>
    public static bool IsParagraphInsideGrayShadedRegion(
        PdfParagraph para, IReadOnlyList<TableMaskRegion> grayRegions)
    {
        if (grayRegions.Count == 0) return false;
        var expanded = PdfGrayPromptRegionBuilder.ExpandGrayShadedRegions(grayRegions);
        if (ParagraphCenterInsideAnyRegion(para, expanded)) return true;
        return PdfDiagramRegionGeometry.ParagraphLetterOverlapRatio(para, expanded) >= 0.5;
    }

    public static bool IsParagraphInsideAnchoredGrayPromptRegion(
        PdfParagraph para,
        IReadOnlyList<TableMaskRegion> grayRegions,
        IReadOnlyList<PdfParagraph> pageList)
    {
        if (grayRegions.Count == 0) return false;
        foreach (var region in PdfGrayPromptRegionBuilder.ExpandGrayShadedRegions(grayRegions, 8.0))
        {
            if (!ParagraphCenterInsideAnyRegion(para, new[] { region }) &&
                PdfDiagramRegionGeometry.ParagraphLetterOverlapRatio(para, new[] { region }) < 0.5)
            {
                continue;
            }

            bool hasPromptAnchor = pageList.Any(anchor =>
                anchor != para &&
                (PdfGrayPromptClassifier.IsGrayPromptBoxParagraph(anchor) || PdfGrayPromptClassifier.IsGrayPromptSubheading(anchor)) &&
                ParagraphCenterInsideAnyRegion(anchor, new[] { region }) &&
                PdfGrayPromptClassifier.SharesGrayPromptColumn(anchor, para));
            if (hasPromptAnchor) return true;
        }
        return false;
    }
}

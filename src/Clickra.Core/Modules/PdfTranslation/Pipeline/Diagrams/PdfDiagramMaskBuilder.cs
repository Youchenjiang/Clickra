using System;
using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Modules.PdfTranslation.Model;
using UglyToad.PdfPig.Content;

namespace Clickra.Core.Modules.PdfTranslation.Pipeline;

internal static class PdfDiagramMaskBuilder
{
    public static bool OverlapsWithLargeImage(PdfParagraph para, Page pigPage)
    {
        try
        {
            foreach (var region in GetLargeDiagramBounds(pigPage))
            {
                bool intersectX = para.X0 <= region.X1 && para.X1 >= region.X0;
                bool intersectY = para.Y0 <= region.Y1 && para.Y1 >= region.Y0;
                if (intersectX && intersectY)
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    public static List<TableMaskRegion> SplitWideDiagramMaskRegionsByColumn(
        List<TableMaskRegion> regions, double pageWidth)
    {
        if (regions.Count == 0) return regions;
        double center = pageWidth / 2.0;
        double maxColWidth = pageWidth * 0.52;
        var result = new List<TableMaskRegion>();
        foreach (var r in regions)
        {
            double w = r.X1 - r.X0;
            if (w <= maxColWidth)
            {
                result.Add(r);
                continue;
            }

            var left = new TableMaskRegion(r.X0, r.Y0, Math.Min(r.X1, center - 5), r.Y1);
            var right = new TableMaskRegion(Math.Max(r.X0, center + 5), r.Y0, r.X1, r.Y1);
            if (left.X1 - left.X0 >= 80)
                result.Add(left);
            if (right.X1 - right.X0 >= 80)
                result.Add(right);
        }
        return result;
    }

    public static List<TableMaskRegion> BuildProcessedDiagramMaskRegions(
        Page pigPage, IReadOnlyList<PdfParagraph> pageList)
    {
        return CapDiagramMaskBelowFigureCaptions(
            ShrinkDiagramMaskRegionsBottomGutter(
                SplitWideDiagramMaskRegionsByColumn(
                    BuildDiagramMaskRegions(GetLargeDiagramBounds(pigPage)),
                    pigPage.Width)),
            pageList is List<PdfParagraph> list ? list : pageList.ToList(),
            pigPage.Width);
    }

    /// <summary>Collect large image/path bounding boxes that define diagram/chart regions.</summary>
    public static List<TableMaskRegion> GetLargeDiagramBounds(Page pigPage)
    {
        var bounds = new List<TableMaskRegion>();
        try
        {
            foreach (var img in pigPage.GetImages())
            {
                if (img.BoundingBox.Width > 80 && img.BoundingBox.Height > 80)
                {
                    var b = img.BoundingBox;
                    bounds.Add(new TableMaskRegion(b.Left, b.Bottom, b.Right, b.Top));
                }
            }

            foreach (var path in pigPage.Paths)
            {
                var rectOpt = path.GetBoundingRectangle();
                if (!rectOpt.HasValue) continue;
                var b = rectOpt.Value;

                // Skip full-page borders
                if (b.Width > pigPage.Width * 0.9 || b.Height > pigPage.Height * 0.9)
                    continue;

                // Skip thin horizontal rules (e.g. column separators, table borders)
                bool isThinHRule = b.Width > pigPage.Width * 0.35 && b.Height < 3.0;
                bool isThinVRule = b.Height > pigPage.Height * 0.35 && b.Width < 3.0;
                if (isThinHRule || isThinVRule) continue;

                // Collect any path with meaningful area (small paths cluster into diagram bounds below)
                if (b.Width > 4.0 && b.Height > 4.0)
                {
                    bounds.Add(new TableMaskRegion(b.Left, b.Bottom, b.Right, b.Top));
                }
            }
        }
        catch { }
        return bounds;
    }

    /// <summary>Merge nearby diagram path bounds into mask regions for overlay protection.</summary>
    public static List<TableMaskRegion> BuildDiagramMaskRegions(List<TableMaskRegion> rawBounds)
    {
        if (rawBounds.Count == 0) return rawBounds;
        var merged = new List<TableMaskRegion>();
        var used = new bool[rawBounds.Count];
        for (int i = 0; i < rawBounds.Count; i++)
        {
            if (used[i]) continue;
            var (region, count, hasLargeOriginal) = GrowDiagramCluster(rawBounds, used, i);
            double mergedW = region.X1 - region.X0;
            double mergedH = region.Y1 - region.Y0;
            if (hasLargeOriginal || count >= 3 ||
                (mergedW > 80 && mergedH > 40) || (mergedW > 40 && mergedH > 80))
            {
                merged.Add(new TableMaskRegion(region.X0 - 4, region.Y0 - 4, region.X1 + 4, region.Y1 + 4));
            }
        }
        return merged;
    }

    private static (TableMaskRegion Region, int Count, bool HasLargeOriginal) GrowDiagramCluster(
        IReadOnlyList<TableMaskRegion> rawBounds, bool[] used, int start)
    {
        var initial = rawBounds[start];
        double x0 = initial.X0, y0 = initial.Y0, x1 = initial.X1, y1 = initial.Y1;
        used[start] = true;
        int count = 1;
        bool hasLargeOriginal = IsLargeDiagramOriginal(initial);
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int j = 0; j < rawBounds.Count; j++)
            {
                if (used[j]) continue;
                var other = rawBounds[j];
                bool closeX = other.X0 <= x1 + 25 && other.X1 >= x0 - 25;
                bool closeY = other.Y0 <= y1 + 25 && other.Y1 >= y0 - 25;
                if (!closeX || !closeY) continue;
                x0 = Math.Min(x0, other.X0);
                y0 = Math.Min(y0, other.Y0);
                x1 = Math.Max(x1, other.X1);
                y1 = Math.Max(y1, other.Y1);
                used[j] = true;
                changed = true;
                count++;
                if (IsLargeDiagramOriginal(other)) hasLargeOriginal = true;
            }
        }
        return (new TableMaskRegion(x0, y0, x1, y1), count, hasLargeOriginal);
    }

    private static bool IsLargeDiagramOriginal(TableMaskRegion region)
    {
        double width = region.X1 - region.X0;
        double height = region.Y1 - region.Y0;
        return (width > 80 && height > 30) || (width > 30 && height > 60);
    }

    /// <summary>
    /// Trim bloated bottom gutter from tall merged diagram masks so column body text
    /// below workflow figures (PentestAgent p5 §3.1) is not skip-rendered.
    /// </summary>
    public static List<TableMaskRegion> ShrinkDiagramMaskRegionsBottomGutter(List<TableMaskRegion> regions)
    {
        if (regions.Count == 0) return regions;
        var trimmed = new List<TableMaskRegion>(regions.Count);
        foreach (var r in regions)
        {
            double h = r.Y1 - r.Y0;
            if (h > 100)
            {
                double trim = Math.Min(55, h * 0.28);
                trimmed.Add(new TableMaskRegion(r.X0, r.Y0 + trim, r.X1, r.Y1));
            }
            else
            {
                trimmed.Add(r);
            }
        }
        return trimmed;
    }

    /// <summary>
    /// Cap tall merged diagram bounds so translated figure captions below diagrams are not skip-rendered
    /// (PentestAgent p7 Fig. 4–6).
    /// </summary>
    public static List<TableMaskRegion> CapDiagramMaskBelowFigureCaptions(
        List<TableMaskRegion> regions, IReadOnlyList<PdfParagraph> pageList, double pageWidth)
    {
        if (regions.Count == 0) return regions;
        var captions = pageList
            .Where(PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph)
            .Where(p =>
            {
                string t = p.TextWithPlaceholders.Trim();
                return t.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                       t.StartsWith("Fig.", StringComparison.OrdinalIgnoreCase) ||
                       t.StartsWith("Fig ", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        if (captions.Count < 2) return regions;

        double center = pageWidth / 2.0;
        var result = new List<TableMaskRegion>(regions.Count);
        foreach (var r in regions)
        {
            bool rightCol = (r.X0 + r.X1) / 2.0 >= center - 8;
            var colCaptions = captions
                .Where(c => ((c.X0 + c.Width / 2) >= center - 8) == rightCol)
                .ToList();
            if (colCaptions.Count == 0)
            {
                result.Add(r);
                continue;
            }

            double capY0 = colCaptions.Min(c => c.Y0) - 12;
            if (r.Y0 < capY0 && capY0 < r.Y1 - 40)
            {
                result.Add(new TableMaskRegion(r.X0, capY0, r.X1, r.Y1));
            }
            else
            {
                result.Add(r);
            }
        }
        return result;
    }
}

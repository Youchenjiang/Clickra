using System;
using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Models;
using UglyToad.PdfPig.Content;

namespace Clickra.Core.Processors
{
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
                var r = rawBounds[i];
                double x0 = r.X0, y0 = r.Y0, x1 = r.X1, y1 = r.Y1;
                used[i] = true;
                int count = 1;
                // Track whether any original constituent was already large enough to be a diagram on its own
                bool hasLargeOriginal = (r.X1 - r.X0 > 80 && r.Y1 - r.Y0 > 30) || (r.X1 - r.X0 > 30 && r.Y1 - r.Y0 > 60);
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    for (int j = 0; j < rawBounds.Count; j++)
                    {
                        if (used[j]) continue;
                        var o = rawBounds[j];
                        bool closeX = o.X0 <= x1 + 25 && o.X1 >= x0 - 25;
                        bool closeY = o.Y0 <= y1 + 25 && o.Y1 >= y0 - 25;
                        if (closeX && closeY)
                        {
                            x0 = Math.Min(x0, o.X0);
                            y0 = Math.Min(y0, o.Y0);
                            x1 = Math.Max(x1, o.X1);
                            y1 = Math.Max(y1, o.Y1);
                            used[j] = true;
                            changed = true;
                            count++;
                            if ((o.X1 - o.X0 > 80 && o.Y1 - o.Y0 > 30) || (o.X1 - o.X0 > 30 && o.Y1 - o.Y0 > 60))
                                hasLargeOriginal = true;
                        }
                    }
                }
                double mergedW = x1 - x0;
                double mergedH = y1 - y0;
                // Retain: originally large element, cluster of 3+ small paths, or merged area is sizeable
                if (hasLargeOriginal || count >= 3 ||
                    (mergedW > 80 && mergedH > 40) || (mergedW > 40 && mergedH > 80))
                {
                    merged.Add(new TableMaskRegion(x0 - 4, y0 - 4, x1 + 4, y1 + 4));
                }
            }
            return merged;
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
                .Where(p => PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(p))
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
}

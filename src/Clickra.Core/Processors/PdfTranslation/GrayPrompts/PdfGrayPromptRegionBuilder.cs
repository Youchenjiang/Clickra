using System;
using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Models;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;

namespace Clickra.Core.Processors
{
    internal static class PdfGrayPromptRegionBuilder
    {
        /// <summary>Shaded vector rects that wrap gray System Message / Prompt / Example boxes (either column).</summary>
        public static List<TableMaskRegion> GetGrayPromptShadedRegions(
            IReadOnlyList<TableMaskRegion> diagramRegions, double pageWidth,
            IReadOnlyList<PdfParagraph>? pageList = null)
        {
            if (diagramRegions.Count == 0) return new List<TableMaskRegion>();
            if (pageList != null && pageList.Any(p =>
                    p.TextWithPlaceholders.Trim().Contains("WORK DIVISION", StringComparison.OrdinalIgnoreCase)))
            {
                return new List<TableMaskRegion>();
            }
            double center = pageWidth / 2.0;
            double maxColWidth = pageWidth * 0.52;
            var result = new List<TableMaskRegion>();
            foreach (var r in diagramRegions)
            {
                double w = r.X1 - r.X0;
                double h = r.Y1 - r.Y0;
                if (h < 70 || h > 320) continue;

                if (w >= 180 && w <= maxColWidth)
                {
                    double regionCenter = (r.X0 + r.X1) / 2.0;
                    if (regionCenter < center + 8 || regionCenter > center - 8)
                    {
                        result.Add(r);
                    }
                    continue;
                }

                // Merged workflow + gray-box paths (e.g. PentestAgent p6): split by column.
                if (w > maxColWidth)
                {
                    var left = new TableMaskRegion(r.X0, r.Y0, Math.Min(r.X1, center - 5), r.Y1);
                    var right = new TableMaskRegion(Math.Max(r.X0, center + 5), r.Y0, r.X1, r.Y1);
                    foreach (var part in new[] { left, right })
                    {
                        double pw = part.X1 - part.X0;
                        if (pw >= 180 && pw <= maxColWidth && (part.Y1 - part.Y0) >= 70)
                        {
                            result.Add(part);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>Union of vector gray boxes, gray path fills, and clustered gray-prompt paragraph bboxes.</summary>
        public static List<TableMaskRegion> BuildEffectiveGrayMaskRegions(
            Page pigPage,
            IReadOnlyList<TableMaskRegion> diagramMaskRegions,
            IReadOnlyList<PdfParagraph> pageList,
            double pageWidth,
            Func<PdfParagraph, IReadOnlyList<TableMaskRegion>, bool> paragraphCenterInsideAnyRegion)
        {
            if (pageList.Any(p =>
                    p.TextWithPlaceholders.Trim().Contains("WORK DIVISION", StringComparison.OrdinalIgnoreCase)))
            {
                return new List<TableMaskRegion>();
            }

            var combined = new List<TableMaskRegion>();
            combined.AddRange(GetGrayPromptShadedRegions(diagramMaskRegions, pageWidth, pageList));
            combined.AddRange(GetGrayVectorFillRegions(pigPage));
            combined.AddRange(BuildGrayPromptBoxUnionRegions(pageList, pageWidth));
            combined = MergeOverlappingGrayRegions(combined, pageWidth);
            return FilterSpuriousEffectiveGrayRegions(combined, pageList, paragraphCenterInsideAnyRegion);
        }

        /// <summary>Drop vector gray boxes that sit on translatable body prose without any gray-prompt paragraph inside.</summary>
        public static List<TableMaskRegion> FilterSpuriousEffectiveGrayRegions(
            List<TableMaskRegion> regions,
            IReadOnlyList<PdfParagraph> pageList,
            Func<PdfParagraph, IReadOnlyList<TableMaskRegion>, bool> paragraphCenterInsideAnyRegion)
        {
            if (regions.Count == 0) return regions;
            var filtered = new List<TableMaskRegion>();
            foreach (var region in regions)
            {
                bool hasGrayPrompt = pageList.Any(p =>
                {
                    return (p.IsGrayPromptContent || PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(p)) &&
                           paragraphCenterInsideAnyRegion(p, new[] { region });
                });
                if (hasGrayPrompt)
                {
                    filtered.Add(region);
                    continue;
                }
                bool overlapsBodyProse = pageList.Any(p =>
                    !p.IsBypassed &&
                    !p.IsGrayPromptContent &&
                    !PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(p) &&
                    (PdfParagraphRoleClassifier.IsTranslatableBodyProse(p) ||
                     PdfParagraphSemanticClassifier.IsHeadingParagraph(p) ||
                     PdfParagraphRoleClassifier.IsTranslatableCalloutProse(p)) &&
                    paragraphCenterInsideAnyRegion(p, new[] { region }));
                if (!overlapsBodyProse)
                    filtered.Add(region);
            }
            return filtered;
        }

        /// <summary>Detect light-gray filled vector rectangles (prompt box backgrounds).</summary>
        public static List<TableMaskRegion> GetGrayVectorFillRegions(Page pigPage)
        {
            var result = new List<TableMaskRegion>();
            try
            {
                foreach (var path in pigPage.Paths)
                {
                    var rectOpt = path.GetBoundingRectangle();
                    if (!rectOpt.HasValue) continue;
                    var b = rectOpt.Value;
                    if (b.Width < 50 || b.Height < 20) continue;
                    if (b.Width > pigPage.Width * 0.92 || b.Height > pigPage.Height * 0.92) continue;

                    bool grayFill = TryGetPathGrayFill(path, out double r, out double g, out double blue) &&
                                    IsLightGrayRgb(r, g, blue);
                    if (!grayFill) continue;
                    result.Add(new TableMaskRegion(b.Left, b.Bottom, b.Right, b.Top));
                }
            }
            catch { }
            return result;
        }

        public static bool TryGetPathGrayFill(PdfPath path, out double r, out double g, out double b)
        {
            r = g = b = 0;
            return path.IsFilled && path.FillColor != null && TryExtractRgb(path.FillColor, out r, out g, out b);
        }

        public static bool TryExtractRgb(IColor color, out double r, out double g, out double b)
        {
            r = g = b = 0;
            try
            {
                if (color is RGBColor rgb)
                {
                    r = rgb.R; g = rgb.G; b = rgb.B;
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static bool IsLightGrayRgb(double r, double g, double b)
        {
            if (r > 1.5) { r /= 255.0; g /= 255.0; b /= 255.0; }
            return r >= 0.68 && r <= 0.96 && g >= 0.68 && g <= 0.96 && b >= 0.68 && b <= 0.98 &&
                   Math.Abs(r - g) < 0.1 && Math.Abs(g - b) < 0.1;
        }

        /// <summary>Merge flagged gray-prompt paragraphs into contiguous box bboxes per column.</summary>
        public static List<TableMaskRegion> BuildGrayPromptBoxUnionRegions(
            IReadOnlyList<PdfParagraph> paragraphs, double pageWidth, double pad = 6.0)
        {
            double center = pageWidth / 2.0;
            var grayParas = paragraphs
                .Where(p => p.IsGrayPromptContent || PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(p))
                .ToList();
            if (grayParas.Count == 0) return new List<TableMaskRegion>();

            var result = new List<TableMaskRegion>();
            foreach (bool leftCol in new[] { true, false })
            {
                var colParas = grayParas
                    .Where(p => leftCol
                        ? (p.X0 + p.X1) / 2.0 < center - 5
                        : (p.X0 + p.X1) / 2.0 > center + 5)
                    .OrderByDescending(p => p.Y1)
                    .ToList();
                if (colParas.Count == 0) continue;

                var cluster = new List<PdfParagraph> { colParas[0] };
                for (int i = 1; i < colParas.Count; i++)
                {
                    var prev = cluster[^1];
                    var curr = colParas[i];
                    double gap = prev.Y0 - curr.Y1;
                    if (gap > 55)
                    {
                        result.Add(UnionParagraphBboxes(cluster, 4.0));
                        cluster = new List<PdfParagraph>();
                    }
                    cluster.Add(curr);
                }
                if (cluster.Count > 0)
                    result.Add(UnionParagraphBboxes(cluster, 4.0));
            }
            return result;
        }

        public static TableMaskRegion UnionParagraphBboxes(IReadOnlyList<PdfParagraph> paras, double pad)
        {
            return new TableMaskRegion(
                paras.Min(p => p.X0) - pad,
                paras.Min(p => p.Y0) - pad,
                paras.Max(p => p.X1) + pad,
                paras.Max(p => p.Y1) + pad);
        }

        public static List<TableMaskRegion> MergeOverlappingGrayRegions(
            List<TableMaskRegion> rawBounds, double pageWidth = 0)
        {
            if (rawBounds.Count <= 1) return rawBounds;
            var merged = new List<TableMaskRegion>();
            var used = new bool[rawBounds.Count];
            double center = pageWidth / 2.0;
            for (int i = 0; i < rawBounds.Count; i++)
            {
                if (used[i]) continue;
                var r = rawBounds[i];
                double x0 = r.X0, y0 = r.Y0, x1 = r.X1, y1 = r.Y1;
                bool rLeftCol = pageWidth <= 0 || (r.X0 + r.X1) / 2.0 < center - 5;
                used[i] = true;
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    for (int j = 0; j < rawBounds.Count; j++)
                    {
                        if (used[j]) continue;
                        var o = rawBounds[j];
                        if (pageWidth > 0)
                        {
                            bool oLeftCol = (o.X0 + o.X1) / 2.0 < center - 5;
                            if (rLeftCol != oLeftCol) continue;
                        }
                        bool closeX = o.X0 <= x1 + 12 && o.X1 >= x0 - 12;
                        bool closeY = o.Y0 <= y1 + 12 && o.Y1 >= y0 - 12;
                        if (closeX && closeY)
                        {
                            x0 = Math.Min(x0, o.X0);
                            y0 = Math.Min(y0, o.Y0);
                            x1 = Math.Max(x1, o.X1);
                            y1 = Math.Max(y1, o.Y1);
                            used[j] = true;
                            changed = true;
                        }
                    }
                }
                merged.Add(new TableMaskRegion(x0, y0, x1, y1));
            }
            return merged;
        }

        public static List<TableMaskRegion> ExpandGrayShadedRegions(
            IReadOnlyList<TableMaskRegion> grayRegions, double inset = 3.0)
        {
            return grayRegions
                .Select(r => new TableMaskRegion(r.X0 - inset, r.Y0 - inset, r.X1 + inset, r.Y1 + inset))
                .ToList();
        }

        /// <summary>Union bbox of flagged gray-prompt paragraphs (covers p7 workflow pages without vector gray rects).</summary>
        public static List<TableMaskRegion> BuildGrayPromptParagraphMaskRegions(
            IReadOnlyList<PdfParagraph> paragraphs, double pad = 2.0)
        {
            var regions = new List<TableMaskRegion>();
            foreach (var para in paragraphs)
            {
                if (!para.IsGrayPromptContent && !PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(para)) continue;
                regions.Add(new TableMaskRegion(
                    para.X0 - pad, para.Y0 - pad, para.X1 + pad, para.Y1 + pad));
            }
            return regions;
        }

        public static List<TableMaskRegion> CombineGrayMaskRegions(
            IReadOnlyList<TableMaskRegion> shadedRegions,
            IReadOnlyList<TableMaskRegion> paragraphRegions)
        {
            var combined = new List<TableMaskRegion>();
            if (shadedRegions.Count > 0) combined.AddRange(shadedRegions);
            if (paragraphRegions.Count > 0) combined.AddRange(paragraphRegions);
            return combined;
        }
    }
}

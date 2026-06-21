using System;
using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfTableMaskPlanner
    {
        /// <summary>Cluster table cells into separate mask regions instead of one page-wide bounding box.</summary>
        public static List<TableMaskRegion> BuildTableMaskRegions(
            List<PdfParagraph> tableParas, double pageWidth, Func<PdfParagraph, bool>? excludePara = null)
        {
            if (excludePara != null)
                tableParas = tableParas.Where(p => !excludePara(p)).ToList();
            var regions = new List<TableMaskRegion>();
            // Only cluster actual table cells (short rows), not body paragraphs mis-marked as tables.
            var cellParas = tableParas.Where(p => p.Height <= 35).ToList();
            if (cellParas.Count < 2) return regions;

            double center = pageWidth / 2.0;
            var groups = new List<List<PdfParagraph>>();
            foreach (var cand in cellParas)
            {
                bool added = false;
                foreach (var group in groups)
                {
                    bool close = false;
                    foreach (var member in group)
                    {
                        bool candLeft = (cand.X0 + cand.Width / 2) < center;
                        bool memberLeft = (member.X0 + member.Width / 2) < center;
                        if (candLeft != memberLeft) continue;

                        double verticalDist = 0;
                        if (cand.Y1 < member.Y0)
                        {
                            verticalDist = member.Y0 - cand.Y1;
                        }
                        else if (member.Y1 < cand.Y0)
                        {
                            verticalDist = cand.Y0 - member.Y1;
                        }

                        if (verticalDist < 45)
                        {
                            close = true;
                            break;
                        }
                    }
                    if (close)
                    {
                        group.Add(cand);
                        added = true;
                        break;
                    }
                }
                if (!added)
                {
                    groups.Add(new List<PdfParagraph> { cand });
                }
            }

            foreach (var group in groups)
            {
                if (group.Count < 2) continue;
                regions.Add(new TableMaskRegion(
                    group.Min(p => p.X0) - 8,
                    group.Min(p => p.Y0) - 8,
                    group.Max(p => p.X1) + 8,
                    group.Max(p => p.Y1) + 12));
            }

            return regions;
        }

        public static bool ParagraphOverlapsAnyTableMask(
            double paraX0, double paraY0, double paraX1, double paraY1,
            IReadOnlyList<TableMaskRegion> regions,
            double minOverlapX = 30.0, double minOverlapY = 5.0)
        {
            foreach (var region in regions)
            {
                if (ParagraphOverlapsTableMask(paraX0, paraY0, paraX1, paraY1,
                        region.X0, region.Y0, region.X1, region.Y1, minOverlapX, minOverlapY))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool ParagraphOverlapsTableMask(
            double paraX0, double paraY0, double paraX1, double paraY1,
            double tableMaskX0, double tableMaskY0, double tableMaskX1, double tableMaskY1,
            double minOverlapX = 30.0, double minOverlapY = 5.0)
        {
            double overlapX = Math.Min(paraX1, tableMaskX1) - Math.Max(paraX0, tableMaskX0);
            double overlapY = Math.Min(paraY1, tableMaskY1) - Math.Max(paraY0, tableMaskY0);
            return overlapX >= minOverlapX && overlapY >= minOverlapY;
        }

        /// <summary>
        /// Raise the bottom edge of a white mask so it cannot paint over a table's top border.
        /// Table captions sit above the mask region; only the padded mask rect reaches into it.
        /// Top border lines sit ~9pt below region.Y1 (region adds 12pt above max cell Y1).
        /// </summary>
        public static double ClampMaskBottomAboveTables(
            double maskX0, double maskY0, double maskX1, double maskY1,
            IReadOnlyList<TableMaskRegion> regions,
            double minOverlapX = 10.0)
        {
            const double tableTopBorderInset = 9.0;
            const double borderClearance = 1.5;

            double clampedY0 = maskY0;
            foreach (var region in regions)
            {
                double overlapX = Math.Min(maskX1, region.X1) - Math.Max(maskX0, region.X0);
                if (overlapX < minOverlapX) continue;

                // Mask overlaps table region from above: keep bottom above top border line.
                if (maskY1 > region.Y0 && clampedY0 < region.Y1)
                {
                    double minMaskBottom = region.Y1 - tableTopBorderInset + borderClearance;
                    clampedY0 = Math.Max(clampedY0, minMaskBottom);
                }
            }
            return clampedY0;
        }
    }
}

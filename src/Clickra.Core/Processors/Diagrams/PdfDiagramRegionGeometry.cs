using System;
using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfDiagramRegionGeometry
    {
        public static double ParagraphLetterOverlapRatio(
            PdfParagraph para, IReadOnlyList<TableMaskRegion> regions)
        {
            if (para.AllLetters.Count == 0 || regions.Count == 0) return 0;
            int hit = 0;
            foreach (var letter in para.AllLetters)
            {
                foreach (var region in regions)
                {
                    if (letter.X >= region.X0 && letter.X <= region.X1 &&
                        letter.Y >= region.Y0 && letter.Y <= region.Y1)
                    {
                        hit++;
                        break;
                    }
                }
            }
            return (double)hit / para.AllLetters.Count;
        }

        /// <summary>When vector/image bounds are missing, infer diagram masks from large table bboxes on figure pages.</summary>
        public static List<TableMaskRegion> GetEffectiveDiagramMaskRegions(
            IReadOnlyList<TableMaskRegion> diagramRegions,
            IReadOnlyList<TableMaskRegion> tableMaskRegions,
            IReadOnlyList<PdfParagraph> pageList)
        {
            if (diagramRegions.Count > 0)
            {
                return new List<TableMaskRegion>(diagramRegions);
            }

            if (tableMaskRegions.Count == 0) return new List<TableMaskRegion>();

            bool hasFigureContent = pageList.Any(p =>
            {
                string t = p.TextWithPlaceholders.Trim();
                return t.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                       t.StartsWith("Fig.", StringComparison.OrdinalIgnoreCase) ||
                       t.StartsWith("Fig ", StringComparison.OrdinalIgnoreCase);
            });
            if (!hasFigureContent) return new List<TableMaskRegion>();

            return tableMaskRegions
                .Where(r => (r.X1 - r.X0) > 100 && (r.Y1 - r.Y0) > 60)
                .ToList();
        }

        public static bool OverlapsAnyRegion(
            PdfParagraph para, IReadOnlyList<TableMaskRegion> regions)
        {
            double cx = para.X0 + para.Width / 2;
            double cy = para.Y0 + para.Height / 2;
            foreach (var region in regions)
            {
                bool intersectX = para.X0 <= region.X1 && para.X1 >= region.X0;
                bool intersectY = para.Y0 <= region.Y1 && para.Y1 >= region.Y0;
                if (intersectX && intersectY) return true;
                if (cx >= region.X0 && cx <= region.X1 && cy >= region.Y0 && cy <= region.Y1) return true;
            }
            return false;
        }
    }
}

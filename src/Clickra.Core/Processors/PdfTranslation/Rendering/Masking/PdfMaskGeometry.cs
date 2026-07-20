using System;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfMaskGeometry
    {
        /// <summary>Union paragraph bbox with per-letter ink extents for white-mask coverage.</summary>
        public static void GetParagraphPaintBounds(
            PdfParagraph para, out double x0, out double y0, out double x1, out double y1)
        {
            x0 = Math.Min(para.OriginalX0, para.X0);
            y0 = Math.Min(para.OriginalY0, para.Y0);
            x1 = Math.Max(para.OriginalX1, para.X1);
            y1 = Math.Max(para.OriginalY1, para.Y1);
            if (para.AllLetters.Count == 0) return;
            foreach (var letter in para.AllLetters)
            {
                if (letter.Left < x0) x0 = letter.Left;
                if (letter.Bottom < y0) y0 = letter.Bottom;
                if (letter.Right > x1) x1 = letter.Right;
                if (letter.Top > y1) y1 = letter.Top;
            }
        }

        /// <summary>Expand white masks to full column width for body prose to erase orphan glyph runs.</summary>
        public static void ExpandMaskToColumnWidth(
            ref double maskX0, ref double maskX1, PdfParagraph para, double pageWidth)
        {
            // Findings callouts are fixed vector containers. Expanding their
            // text mask to the whole column erases the source fill and border.
            if (PdfParagraphRoleClassifier.IsFindingCallout(para)) return;

            if (!PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) &&
                !PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para))
            {
                return;
            }

            double center = pageWidth / 2.0;
            double paraCenter = (para.X0 + para.X1) / 2.0;
            if (paraCenter < center - 8)
            {
                maskX0 = Math.Min(maskX0, 48);
                maskX1 = Math.Max(maskX1, center - 12);
            }
            else if (paraCenter > center + 8)
            {
                maskX0 = Math.Min(maskX0, center + 12);
                maskX1 = Math.Max(maskX1, pageWidth - 48);
            }
        }

        public static bool ParagraphSharesColumnWithRegion(
            double paraX0, double paraX1, TableMaskRegion region, double pageWidth, double minSharedWidth = 20.0)
        {
            double center = pageWidth / 2.0;
            double paraCenter = (paraX0 + paraX1) / 2.0;
            double regionCenter = (region.X0 + region.X1) / 2.0;
            bool paraLeft = paraCenter < center - 5;
            bool paraRight = paraCenter > center + 5;
            bool regionLeft = regionCenter < center - 5;
            bool regionRight = regionCenter > center + 5;
            if (paraLeft && regionRight) return false;
            if (paraRight && regionLeft) return false;
            double overlapX = Math.Min(paraX1, region.X1) - Math.Max(paraX0, region.X0);
            return overlapX >= minSharedWidth;
        }
    }
}

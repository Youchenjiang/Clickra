using System;
using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfAnnotationSpatialMatcher
    {
        public static int ScoreAnnotationParagraph(
            PdfParagraph para,
            List<PdfLetter> overlappingLetters,
            double annotCenterX,
            double annotCenterY)
        {
            int score = overlappingLetters.Count;
            bool centerInside = annotCenterX >= para.X0 && annotCenterX <= para.X1 &&
                                annotCenterY >= para.Y0 && annotCenterY <= para.Y1;
            if (centerInside) score += 1000;
            if (para.IsBypassed || para.IsCode) score += 2000;
            if (!para.IsBypassed && !para.IsCode && overlappingLetters.Count <= 4) score -= 300;
            return score;
        }

        public static List<RenderedChar>? PickOccurrenceBySpatialPosition(
            List<List<RenderedChar>> occurrences,
            double targetPdfX,
            double targetPdfY,
            int occurrenceIdx,
            bool preferVerticalAlignment = false)
        {
            if (occurrences.Count == 1) return occurrences[0];

            int bestIdx = 0;
            double minDist = double.MaxValue;
            for (int i = 0; i < occurrences.Count; i++)
            {
                double dist = GetOccurrenceCenterDistance(
                    occurrences[i], targetPdfX, targetPdfY, preferVerticalAlignment);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestIdx = i;
                }
            }

            if (occurrenceIdx > 0 && occurrenceIdx < occurrences.Count)
            {
                double idxDist = GetOccurrenceCenterDistance(
                    occurrences[occurrenceIdx], targetPdfX, targetPdfY, preferVerticalAlignment);
                if (idxDist <= minDist * 1.5 + 2.0)
                {
                    bestIdx = occurrenceIdx;
                }
            }

            return occurrences[bestIdx];
        }

        public static List<RenderedChar>? MapRenderedCharsBySpatialPosition(
            List<RenderedChar> cleanRendered,
            double targetPdfX,
            double targetPdfY,
            double relWidth,
            double paraWidth)
        {
            if (cleanRendered.Count == 0) return null;

            double targetWidth = Math.Max(8.0, paraWidth * Math.Max(relWidth, 0.02));
            if (relWidth < 0.08)
            {
                targetWidth = Math.Min(targetWidth, 14.0);
            }
            double bestLineY = cleanRendered
                .OrderBy(rc => Math.Abs(((rc.Bottom + rc.Top) / 2.0) - targetPdfY))
                .Select(rc => (rc.Bottom + rc.Top) / 2.0)
                .First();
            double lineTolerance = 4.0;

            var lineChars = cleanRendered
                .Select((rc, idx) => (rc, idx))
                .Where(t => Math.Abs(((t.rc.Bottom + t.rc.Top) / 2.0) - bestLineY) <= lineTolerance)
                .ToList();
            if (lineChars.Count == 0)
            {
                lineChars = cleanRendered.Select((rc, idx) => (rc, idx)).ToList();
            }

            int bestStart = 0;
            double minDist = double.MaxValue;
            for (int start = 0; start < lineChars.Count; start++)
            {
                var cluster = new List<RenderedChar>();
                double usedWidth = 0;
                for (int j = start; j < lineChars.Count; j++)
                {
                    cluster.Add(lineChars[j].rc);
                    usedWidth += lineChars[j].rc.Right - lineChars[j].rc.Left;
                    if (usedWidth >= targetWidth) break;
                }
                if (cluster.Count == 0) continue;

                double cx = cluster.Average(rc => (rc.Left + rc.Right) / 2.0);
                double cy = cluster.Average(rc => (rc.Bottom + rc.Top) / 2.0);
                double dx = cx - targetPdfX;
                double dy = cy - targetPdfY;
                double dist = dx * dx + dy * dy;
                if (dist < minDist)
                {
                    minDist = dist;
                    bestStart = start;
                }
            }

            var result = new List<RenderedChar>();
            double widthUsed = 0;
            for (int j = bestStart; j < lineChars.Count; j++)
            {
                result.Add(lineChars[j].rc);
                widthUsed += lineChars[j].rc.Right - lineChars[j].rc.Left;
                if (widthUsed >= targetWidth) break;
            }

            return result.Count > 0 ? result : null;
        }

        private static double GetOccurrenceCenterDistance(
            List<RenderedChar> occurrence,
            double targetPdfX,
            double targetPdfY,
            bool preferVerticalAlignment = false)
        {
            double cx = occurrence.Average(rc => (rc.Left + rc.Right) / 2.0);
            double cy = occurrence.Average(rc => (rc.Bottom + rc.Top) / 2.0);
            double dx = cx - targetPdfX;
            double dy = cy - targetPdfY;
            if (preferVerticalAlignment)
            {
                return Math.Abs(dy) * 4.0 + Math.Abs(dx);
            }
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}

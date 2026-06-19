using System;
using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfParagraphPostProcessor
    {
        public static void MergeVerticallyAdjacentParagraphs(
            List<PdfParagraph> paragraphs,
            Func<PdfParagraph, bool> isHeadingParagraph)
        {
            if (paragraphs.Count <= 1) return;

            bool mergedAny = true;
            while (mergedAny)
            {
                mergedAny = false;
                // Sort by Y1 descending (top to bottom on the page)
                var sorted = paragraphs.OrderByDescending(p => p.Y1).ToList();

                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    var p1 = sorted[i];
                    if (p1.IsBypassed || string.IsNullOrWhiteSpace(p1.TextWithPlaceholders)) continue;

                    // If p1 is a heading, do not merge anything into it
                    if (isHeadingParagraph(p1)) continue;

                    // If p1 ends with sentence-ending punctuation, do not merge subsequent paragraphs
                    string clean1 = p1.TextWithPlaceholders.Trim();
                    if (clean1.EndsWith(".") || clean1.EndsWith("?") || clean1.EndsWith("!") || clean1.EndsWith(":") ||
                        clean1.EndsWith("。") || clean1.EndsWith("」") || clean1.EndsWith("\""))
                    {
                        continue;
                    }

                    for (int j = i + 1; j < sorted.Count; j++)
                    {
                        var p2 = sorted[j];
                        if (p2.IsBypassed || string.IsNullOrWhiteSpace(p2.TextWithPlaceholders)) continue;

                        // Check same column / horizontal overlap > 60%
                        double minWidth = Math.Min(p1.Width, p2.Width);
                        if (minWidth <= 0) continue;

                        double overlap = Math.Min(p1.X1, p2.X1) - Math.Max(p1.X0, p2.X0);
                        if (overlap / minWidth <= 0.6) continue;

                        // Check vertical gap
                        double gap = p1.Y0 - p2.Y1;

                        // Allow a vertical gap of up to 6 pt (tightened from 14 pt to prevent paragraph merging)
                        if (gap > 6 || gap < -10) continue;

                        // Ensure p2 does not start a new list item, reference, or heading
                        if (PdfParagraphBlockMerger.StartsNewParagraphOrSection(p2.TextWithPlaceholders)) continue;

                        // Only merge reference/list multi-line items; never merge ordinary body paragraphs
                        bool isP1RefOrList = ReferenceSectionDetector.IsReferenceParagraph(p1) || PdfParagraphBlockMerger.StartsNewParagraphOrSection(p1.TextWithPlaceholders);
                        bool isP2RefOrList = ReferenceSectionDetector.IsReferenceParagraph(p2) || PdfParagraphBlockMerger.StartsNewParagraphOrSection(p2.TextWithPlaceholders);
                        if (!isP1RefOrList && !isP2RefOrList) continue;

                        // Merge p2 into p1
                        p1.MergeWith(p2);

                        // Remove p2 from the lists
                        paragraphs.Remove(p2);
                        mergedAny = true;
                        break;
                    }
                    if (mergedAny) break;
                }
            }
        }
    }
}

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

        /// <summary>
        /// Joins visual line fragments that Docstrum split at a wrapped line's
        /// right edge.  Acknowledgement text and running-header continuations
        /// are ordinary paragraphs, not separate paragraphs; leaving each
        /// line as an independent translation unit makes the renderer apply
        /// the height reflow (and its fallback font size) independently.
        /// </summary>
        public static void MergeWrappedLineFragments(
            List<PdfParagraph> paragraphs,
            Func<PdfParagraph, bool> isHeadingParagraph,
            double pageHeight)
        {
            if (paragraphs.Count <= 1) return;

            bool mergedAny = true;
            while (mergedAny)
            {
                mergedAny = false;
                var sorted = paragraphs.OrderByDescending(p => p.Y1).ToList();
                foreach (var upper in sorted)
                {
                    // Pages are two-column; choose the nearest lower fragment
                    // in the same visual column rather than the next global
                    // item (which is often a right-column reference).
                    var lower = sorted
                        .Where(candidate => candidate != upper && candidate.Y1 <= upper.Y0 + 1.0)
                        .OrderByDescending(candidate => candidate.Y1)
                        .FirstOrDefault(candidate => CanMergeWrappedFragment(
                            upper, candidate, isHeadingParagraph, pageHeight));
                    if (lower == null) continue;

                    upper.MergeWith(lower);
                    paragraphs.Remove(lower);
                    mergedAny = true;
                    break;
                }
            }
        }

        private static bool CanMergeWrappedFragment(
            PdfParagraph upper,
            PdfParagraph lower,
            Func<PdfParagraph, bool> isHeadingParagraph,
            double pageHeight)
        {
            if (upper.IsBypassed || lower.IsBypassed || upper.IsTable || lower.IsTable ||
                upper.IsDiagram || lower.IsDiagram || upper.IsCode || lower.IsCode ||
                upper.IsGrayPromptContent || lower.IsGrayPromptContent ||
                string.IsNullOrWhiteSpace(upper.TextWithPlaceholders) ||
                string.IsNullOrWhiteSpace(lower.TextWithPlaceholders))
                return false;

            if (isHeadingParagraph(upper) || isHeadingParagraph(lower) ||
                PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(upper) ||
                PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(lower) ||
                PdfParagraphBlockMerger.StartsNewParagraphOrSection(upper.TextWithPlaceholders) ||
                PdfParagraphBlockMerger.StartsNewParagraphOrSection(lower.TextWithPlaceholders))
                return false;

            if (PdfParagraphRoleClassifier.IsRunningHeaderOrFooter(upper, pageHeight) !=
                PdfParagraphRoleClassifier.IsRunningHeaderOrFooter(lower, pageHeight))
                return false;

            // Same visual column and left edge; a wrapped line can have a
            // shorter right edge but should not jump across a gutter.
            double centerUpper = (upper.X0 + upper.X1) / 2.0;
            double centerLower = (lower.X0 + lower.X1) / 2.0;
            if (Math.Abs(upper.X0 - lower.X0) > 12.0 ||
                Math.Abs(centerUpper - centerLower) > Math.Max(24.0, Math.Min(upper.Width, lower.Width) * 0.25))
                return false;

            double upperFont = upper.SourceVisualFontSize > 0 ? upper.SourceVisualFontSize : upper.AverageFontSize;
            double lowerFont = lower.SourceVisualFontSize > 0 ? lower.SourceVisualFontSize : lower.AverageFontSize;
            if (upperFont <= 0 || lowerFont <= 0 || Math.Abs(upperFont - lowerFont) > 0.75)
                return false;

            // Only a wrapped-line gap is eligible.  PdfPig's line boxes can
            // leave several points between glyph boxes even when the source
            // PDF has one text block (the ASTER acknowledgement is 5 such
            // lines). Separate body paragraphs normally have a larger gap
            // and/or sentence punctuation.
            double gap = upper.Y0 - lower.Y1;
            if (gap < -1.0 || gap > 8.0)
                return false;

            string upperText = upper.TextWithPlaceholders.Trim();
            if (upperText.EndsWith('.') || upperText.EndsWith('?') || upperText.EndsWith('!') ||
                upperText.EndsWith(':') || upperText.EndsWith('。') || upperText.EndsWith('」') ||
                upperText.EndsWith('"'))
                return false;

            return true;
        }
    }
}

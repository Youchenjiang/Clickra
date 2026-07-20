using System;
using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    public static class PdfAnnotationTextMatcher
    {
        public static int ScoreAnnotationParagraph(
            PdfParagraph para,
            List<PdfLetter> overlappingLetters,
            double annotCenterX,
            double annotCenterY)
        {
            return PdfAnnotationSpatialMatcher.ScoreAnnotationParagraph(
                para,
                overlappingLetters,
                annotCenterX,
                annotCenterY);
        }

        public static List<RenderedChar>? FindAnnotationCharacters(
            List<RenderedChar> renderedChars,
            string searchText,
            int occurrenceIdx,
            double relCenterX,
            double relCenterY,
            double relWidth,
            double paraX0,
            double paraY0,
            double paraWidth,
            double paraHeight,
            int figureOccurrenceIdx = -1)
        {
            if (renderedChars == null || renderedChars.Count == 0) return null;

            var cleanRendered = renderedChars.Where(rc => !char.IsWhiteSpace(rc.Character)).ToList();
            if (cleanRendered.Count == 0) return null;

            double targetPdfX = paraX0 + relCenterX * paraWidth;
            double targetPdfY = paraY0 + relCenterY * paraHeight;

            string figureDigits = new string(searchText.Where(char.IsDigit).ToArray());
            if (figureDigits.Length > 0 && figureDigits.Length <= 2)
            {
                bool includeParen = searchText.Contains(')');
                var figureOccurrences = PdfAnnotationOccurrenceFinder.FindFigureRefOccurrences(
                    cleanRendered, figureDigits, includeParen);
                if (figureOccurrences.Count > 0)
                {
                    return PdfAnnotationSpatialMatcher.PickOccurrenceBySpatialPosition(
                        figureOccurrences, targetPdfX, targetPdfY,
                        figureOccurrenceIdx >= 0 ? figureOccurrenceIdx : occurrenceIdx,
                        preferVerticalAlignment: true);
                }

                if (figureDigits.Length == 1)
                {
                    var looseFigure = PdfAnnotationOccurrenceFinder.FindLooseFigureDigitOccurrences(
                        cleanRendered, figureDigits);
                    if (looseFigure.Count > 0)
                    {
                        return PdfAnnotationSpatialMatcher.PickOccurrenceBySpatialPosition(
                            looseFigure, targetPdfX, targetPdfY,
                            figureOccurrenceIdx >= 0 ? figureOccurrenceIdx : occurrenceIdx,
                            preferVerticalAlignment: true);
                    }

                    var digitOccurrences = PdfAnnotationOccurrenceFinder.FindTextOccurrences(cleanRendered, figureDigits);
                    if (digitOccurrences.Count > 0)
                    {
                        return PdfAnnotationSpatialMatcher.PickOccurrenceBySpatialPosition(
                            digitOccurrences, targetPdfX, targetPdfY, occurrenceIdx, preferVerticalAlignment: true);
                    }
                }
            }

            var searchPatterns = PdfAnnotationPatternBuilder.BuildAnnotationSearchPatterns(searchText);
            foreach (var pattern in searchPatterns)
            {
                var occurrences = PdfAnnotationOccurrenceFinder.FindTextOccurrences(cleanRendered, pattern);
                if (occurrences.Count > 0)
                {
                    bool preferVertical = PdfAnnotationPatternBuilder.PrefersVerticalAlignment(pattern);
                    return PdfAnnotationSpatialMatcher.PickOccurrenceBySpatialPosition(
                        occurrences, targetPdfX, targetPdfY, occurrenceIdx, preferVerticalAlignment: preferVertical);
                }
            }

            string romanSection = PdfAnnotationPatternBuilder.ExtractRomanSectionNumeral(searchText);
            if (!string.IsNullOrEmpty(romanSection))
            {
                var sectionOccurrences = PdfAnnotationOccurrenceFinder.FindSectionRomanOccurrences(
                    cleanRendered, romanSection);
                if (sectionOccurrences.Count > 0)
                {
                    return PdfAnnotationSpatialMatcher.PickOccurrenceBySpatialPosition(
                        sectionOccurrences, targetPdfX, targetPdfY, occurrenceIdx, preferVerticalAlignment: true);
                }
            }

            var spatial = PdfAnnotationSpatialMatcher.MapRenderedCharsBySpatialPosition(
                cleanRendered, targetPdfX, targetPdfY, relWidth, paraWidth);
            if (spatial != null && spatial.Count > 0)
            {
                double cx = spatial.Average(rc => (rc.Left + rc.Right) / 2.0);
                double cy = spatial.Average(rc => (rc.Bottom + rc.Top) / 2.0);
                double dx = cx - targetPdfX;
                double dy = cy - targetPdfY;
                if (Math.Sqrt(dx * dx + dy * dy) <= Math.Max(24.0, paraWidth * 0.15))
                {
                    return spatial;
                }
            }

            return null;
        }

        public static string NormalizeAnnotationSearchText(string raw)
        {
            return PdfAnnotationPatternBuilder.NormalizeAnnotationSearchText(raw);
        }
    }
}

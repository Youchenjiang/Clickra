using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfAnnotationOccurrenceMatcher
    {
        public static int GetOccurrenceIndex(List<PdfLetter> allLetters, List<PdfLetter> targetLetters, string searchText)
        {
            ArgumentNullException.ThrowIfNull(allLetters);
            ArgumentNullException.ThrowIfNull(targetLetters);
            if (targetLetters.Count == 0)
            {
                throw new ArgumentException("Target letters cannot be empty.", nameof(targetLetters));
            }
            if (string.IsNullOrEmpty(searchText))
            {
                throw new ArgumentException("Search text cannot be null or empty.", nameof(searchText));
            }

            var occurrences = new List<int>();
            for (int i = 0; i <= allLetters.Count - searchText.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < searchText.Length; j++)
                {
                    if (allLetters[i + j].Value.Length == 0 || !CharEqualsNormalized(allLetters[i + j].Value[0], searchText[j]))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    occurrences.Add(i);
                }
            }

            if (occurrences.Count <= 1) return 0;

            double targetAvgIndex = targetLetters.Average(tl => allLetters.IndexOf(tl));
            int bestIdx = 0;
            double minDist = double.MaxValue;
            for (int k = 0; k < occurrences.Count; k++)
            {
                double occurrenceAvgIndex = occurrences[k] + (searchText.Length - 1) / 2.0;
                double dist = Math.Abs(occurrenceAvgIndex - targetAvgIndex);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestIdx = k;
                }
            }
            return bestIdx;
        }

        public static int GetFigureReferenceIndex(IReadOnlyList<PdfLetter> allLetters, int targetLetterIndex)
        {
            if (targetLetterIndex < 0 || allLetters.Count == 0) return -1;

            int ordinal = 0;
            for (int i = 0; i <= Math.Min(targetLetterIndex, allLetters.Count - 1); i++)
            {
                if (allLetters[i].Value.Length == 0 || !char.IsDigit(allLetters[i].Value[0])) continue;
                string prefix = string.Concat(allLetters.Take(i).Select(letter => letter.Value));
                if (Regex.IsMatch(prefix, @"(?:Fig(?:ure)?\.?)[\s\u00A0]*$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                {
                    if (i == targetLetterIndex) return ordinal;
                    ordinal++;
                }
            }

            return -1;
        }

        private static bool CharEqualsNormalized(char c1, char c2)
        {
            if (c1 == c2) return true;
            if (char.ToLowerInvariant(c1) == char.ToLowerInvariant(c2)) return true;
            if ((c1 == '-' || c1 == '–' || c1 == '—') && (c2 == '-' || c2 == '–' || c2 == '—')) return true;
            return false;
        }
    }
}

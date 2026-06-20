using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfAnnotationOccurrenceFinder
    {
        public static List<List<RenderedChar>> FindSectionRomanOccurrences(
            List<RenderedChar> cleanRendered,
            string roman)
        {
            var occurrences = new List<List<RenderedChar>>();
            if (string.IsNullOrEmpty(roman)) return occurrences;

            for (int i = 0; i < cleanRendered.Count; i++)
            {
                if (cleanRendered[i].Character != '第') continue;

                int digitStart = i + 1;
                while (digitStart < cleanRendered.Count && cleanRendered[digitStart].Character == ' ')
                {
                    digitStart++;
                }

                if (digitStart + roman.Length > cleanRendered.Count) continue;

                bool match = true;
                for (int r = 0; r < roman.Length; r++)
                {
                    if (!PdfAnnotationPatternBuilder.CharEqualsNormalized(cleanRendered[digitStart + r].Character, roman[r]))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    occurrences.Add(cleanRendered.GetRange(digitStart, roman.Length));
                }
            }

            if (occurrences.Count == 0)
            {
                var plainOccurrences = FindTextOccurrences(cleanRendered, roman);
                occurrences.AddRange(plainOccurrences);
            }

            string chinese = PdfAnnotationPatternBuilder.RomanToChineseSectionNumeral(roman);
            if (occurrences.Count == 0 && !string.IsNullOrEmpty(chinese))
            {
                for (int i = 0; i < cleanRendered.Count; i++)
                {
                    if (cleanRendered[i].Character != chinese[0]) continue;
                    if (i + chinese.Length > cleanRendered.Count) continue;
                    bool match = true;
                    for (int c = 0; c < chinese.Length; c++)
                    {
                        if (cleanRendered[i + c].Character != chinese[c])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        occurrences.Add(cleanRendered.GetRange(i, chinese.Length));
                    }
                }
            }

            return occurrences;
        }

        public static List<List<RenderedChar>> FindFigureRefOccurrences(
            List<RenderedChar> cleanRendered,
            string digits,
            bool includeClosingParen)
        {
            var occurrences = new List<List<RenderedChar>>();
            if (string.IsNullOrEmpty(digits)) return occurrences;

            for (int i = 0; i < cleanRendered.Count; i++)
            {
                char c = cleanRendered[i].Character;
                if (c != '圖' && c != '图') continue;

                int digitStart = i + 1;
                while (digitStart < cleanRendered.Count &&
                       (cleanRendered[digitStart].Character == ':' ||
                        cleanRendered[digitStart].Character == '：'))
                {
                    digitStart++;
                }

                if (digitStart + digits.Length > cleanRendered.Count) continue;

                bool match = true;
                for (int d = 0; d < digits.Length; d++)
                {
                    if (cleanRendered[digitStart + d].Character != digits[d])
                    {
                        match = false;
                        break;
                    }
                }
                if (!match) continue;

                int end = digitStart + digits.Length;
                if (includeClosingParen && end < cleanRendered.Count && cleanRendered[end].Character == ')')
                {
                    end++;
                }

                occurrences.Add(cleanRendered.GetRange(i, end - i));
            }

            return occurrences;
        }

        public static List<List<RenderedChar>> FindTextOccurrences(List<RenderedChar> cleanRendered, string cleanSearch)
        {
            var occurrences = new List<List<RenderedChar>>();
            if (string.IsNullOrEmpty(cleanSearch)) return occurrences;

            bool requireDigitBoundary = cleanSearch.All(c => char.IsDigit(c) || c == ')') &&
                cleanSearch.Any(char.IsDigit);
            bool requireRomanBoundary = PdfAnnotationPatternBuilder.IsRomanNumeralPattern(cleanSearch);

            for (int i = 0; i <= cleanRendered.Count - cleanSearch.Length; i++)
            {
                if (requireDigitBoundary && !IsStandaloneDigitOccurrence(cleanRendered, i, cleanSearch.Length))
                {
                    continue;
                }
                if (requireRomanBoundary && !IsStandaloneRomanOccurrence(cleanRendered, i, cleanSearch.Length))
                {
                    continue;
                }

                bool match = true;
                for (int j = 0; j < cleanSearch.Length; j++)
                {
                    if (!PdfAnnotationPatternBuilder.CharEqualsNormalized(cleanRendered[i + j].Character, cleanSearch[j]))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    occurrences.Add(cleanRendered.GetRange(i, cleanSearch.Length));
                }
            }

            return occurrences;
        }

        public static List<List<RenderedChar>> FindLooseFigureDigitOccurrences(
            List<RenderedChar> cleanRendered,
            string digits)
        {
            var occurrences = new List<List<RenderedChar>>();
            if (string.IsNullOrEmpty(digits)) return occurrences;

            for (int i = 0; i < cleanRendered.Count; i++)
            {
                if (!char.IsDigit(cleanRendered[i].Character)) continue;
                if (!IsStandaloneDigitOccurrence(cleanRendered, i, 1)) continue;

                bool match = true;
                for (int d = 0; d < digits.Length; d++)
                {
                    if (i + d >= cleanRendered.Count || cleanRendered[i + d].Character != digits[d])
                    {
                        match = false;
                        break;
                    }
                }
                if (!match) continue;

                int figStart = i;
                for (int back = 1; back <= 6 && i - back >= 0; back++)
                {
                    char c = cleanRendered[i - back].Character;
                    if (c == '圖' || c == '图')
                    {
                        figStart = i - back;
                        break;
                    }
                    if (!char.IsPunctuation(c) && c != '即' && c != ':' && c != '：')
                    {
                        break;
                    }
                }

                occurrences.Add(cleanRendered.GetRange(figStart, i + digits.Length - figStart));
            }

            return occurrences;
        }

        private static bool IsStandaloneDigitOccurrence(List<RenderedChar> chars, int start, int length)
        {
            if (start > 0 && char.IsDigit(chars[start - 1].Character)) return false;
            int end = start + length;
            if (end < chars.Count && char.IsDigit(chars[end].Character)) return false;
            return true;
        }

        private static bool IsStandaloneRomanOccurrence(List<RenderedChar> chars, int start, int length)
        {
            if (start > 0)
            {
                char prev = chars[start - 1].Character;
                if (char.IsLetter(prev) && prev < 128) return false;
            }
            int end = start + length;
            if (end < chars.Count)
            {
                char next = chars[end].Character;
                if (char.IsLetter(next) && next < 128) return false;
            }
            return true;
        }
    }
}

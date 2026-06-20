using System;
using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    public static class PdfAnnotationTextMatcher
    {
        private static bool CharEqualsNormalized(char c1, char c2)
        {
            if (c1 == c2) return true;
            if (char.ToUpperInvariant(c1) == char.ToUpperInvariant(c2)) return true;
            return false;
        }
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

        public static List<RenderedChar> FindAnnotationCharacters(
            List<RenderedChar> renderedChars,
            string searchText,
            int occurrenceIdx,
            double relCenterX,
            double relCenterY,
            double relWidth,
            double paraX0,
            double paraY0,
            double paraWidth,
            double paraHeight)
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
                var figureOccurrences = FindFigureRefOccurrences(cleanRendered, figureDigits, includeParen);
                if (figureOccurrences.Count > 0)
                {
                    return PickOccurrenceBySpatialPosition(
                        cleanRendered, figureOccurrences, targetPdfX, targetPdfY, occurrenceIdx, preferVerticalAlignment: true);
                }

                if (figureDigits.Length == 1)
                {
                    var looseFigure = FindLooseFigureDigitOccurrences(cleanRendered, figureDigits);
                    if (looseFigure.Count > 0)
                    {
                        return PickOccurrenceBySpatialPosition(
                            cleanRendered, looseFigure, targetPdfX, targetPdfY, occurrenceIdx, preferVerticalAlignment: true);
                    }

                    var digitOccurrences = FindTextOccurrences(cleanRendered, figureDigits);
                    if (digitOccurrences.Count > 0)
                    {
                        return PickOccurrenceBySpatialPosition(
                            cleanRendered, digitOccurrences, targetPdfX, targetPdfY, occurrenceIdx, preferVerticalAlignment: true);
                    }
                }
            }

            var searchPatterns = BuildAnnotationSearchPatterns(searchText);
            foreach (var pattern in searchPatterns)
            {
                var occurrences = FindTextOccurrences(cleanRendered, pattern);
                if (occurrences.Count > 0)
                {
                    bool preferVertical = pattern.StartsWith("圖", StringComparison.Ordinal) ||
                        pattern.StartsWith(":圖", StringComparison.Ordinal) ||
                        pattern.StartsWith("即圖", StringComparison.Ordinal) ||
                        pattern.StartsWith("表", StringComparison.Ordinal) ||
                        IsRomanNumeralPattern(pattern);
                    return PickOccurrenceBySpatialPosition(
                        cleanRendered, occurrences, targetPdfX, targetPdfY, occurrenceIdx, preferVerticalAlignment: preferVertical);
                }
            }

            string romanSection = ExtractRomanSectionNumeral(searchText);
            if (!string.IsNullOrEmpty(romanSection))
            {
                var sectionOccurrences = FindSectionRomanOccurrences(cleanRendered, romanSection);
                if (sectionOccurrences.Count > 0)
                {
                    return PickOccurrenceBySpatialPosition(
                        cleanRendered, sectionOccurrences, targetPdfX, targetPdfY, occurrenceIdx, preferVerticalAlignment: true);
                }
            }

            var spatial = MapRenderedCharsBySpatialPosition(cleanRendered, targetPdfX, targetPdfY, relWidth, paraWidth);
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

        private static bool IsRomanNumeralPattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            string stripped = pattern.TrimStart('第').Trim();
            return stripped.Length >= 1 && stripped.Length <= 6 &&
                stripped.All(c => "IVXLCDMivxlcdm".Contains(c));
        }

        private static string ExtractRomanSectionNumeral(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText)) return "";

            string clean = new string(searchText.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (clean.Length == 0) return "";

            var sectionRoman = System.Text.RegularExpressions.Regex.Match(
                clean,
                @"Section\s*([IVXLCDM]+)\)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (sectionRoman.Success)
            {
                return sectionRoman.Groups[1].Value.ToUpperInvariant();
            }

            var embeddedRoman = System.Text.RegularExpressions.Regex.Match(
                clean,
                @"([IVXLCDM]{2,})\)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (embeddedRoman.Success && clean.Length <= 12)
            {
                return embeddedRoman.Groups[1].Value.ToUpperInvariant();
            }

            return "";
        }

        private static List<List<RenderedChar>> FindSectionRomanOccurrences(
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
                    if (!CharEqualsNormalized(cleanRendered[digitStart + r].Character, roman[r]))
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

            string chinese = RomanToChineseSectionNumeral(roman);
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

        private static List<List<RenderedChar>> FindFigureRefOccurrences(
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

        private static List<string> BuildAnnotationSearchPatterns(string searchText)
        {
            var patterns = new List<string>();
            if (string.IsNullOrWhiteSpace(searchText)) return patterns;

            string cleanSearch = new string(searchText.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (cleanSearch.Length == 0) return patterns;

            void AddPattern(string pattern)
            {
                if (!string.IsNullOrEmpty(pattern) && !patterns.Contains(pattern))
                {
                    patterns.Add(pattern);
                }
            }

            int openBracket = cleanSearch.IndexOf('[');
            int closeBracket = cleanSearch.IndexOf(']');
            if (openBracket >= 0 && closeBracket > openBracket)
            {
                AddPattern(cleanSearch.Substring(openBracket, closeBracket - openBracket + 1));
            }

            var sectionRomanMatch = System.Text.RegularExpressions.Regex.Match(
                cleanSearch,
                @"(?:Section\s*)?([IVXLCDM]{2,})\)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (sectionRomanMatch.Success)
            {
                string roman = sectionRomanMatch.Groups[1].Value.ToUpperInvariant();
                AddPattern(roman);
                AddPattern($"第{roman}");
                AddPattern($"第 {roman}");
                AddPattern($"表{roman}");
                AddPattern($"表 {roman}");
                string chinese = RomanToChineseSectionNumeral(roman);
                if (!string.IsNullOrEmpty(chinese))
                {
                    AddPattern($"第{chinese}");
                    AddPattern(chinese);
                    AddPattern($"表{chinese}");
                }
            }

            var singleRomanMatch = System.Text.RegularExpressions.Regex.Match(
                cleanSearch,
                @"^([IVXLCDM]{1,4})[,.]?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (singleRomanMatch.Success)
            {
                string roman = singleRomanMatch.Groups[1].Value.ToUpperInvariant();
                AddPattern(roman);
                AddPattern($"第{roman}");
                AddPattern($"表{roman}");
                AddPattern($"表 {roman}");
                string chinese = RomanToChineseSectionNumeral(roman);
                if (!string.IsNullOrEmpty(chinese))
                {
                    AddPattern(chinese);
                    AddPattern($"第{chinese}");
                    AddPattern($"表{chinese}");
                }
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(cleanSearch, @"^\d+\)$"))
            {
                string listingNum = new string(cleanSearch.Where(char.IsDigit).ToArray());
                AddPattern(cleanSearch);
                AddPattern($"{listingNum})");
                AddPattern($"第{listingNum}");
                AddPattern($"清單{listingNum}");
                AddPattern($"清單 {listingNum}");
            }

            var sectionMatch = System.Text.RegularExpressions.Regex.Match(
                cleanSearch,
                @"^([IVXLCDM]+)-([A-Z])\)?[,;.:]?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (sectionMatch.Success)
            {
                AddPattern($"{sectionMatch.Groups[1].Value}-{sectionMatch.Groups[2].Value}");
            }
            else
            {
                var embeddedSection = System.Text.RegularExpressions.Regex.Match(
                    cleanSearch,
                    @"([IVXLCDM]+)-([A-Z])\)?",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (embeddedSection.Success)
                {
                    AddPattern($"{embeddedSection.Groups[1].Value}-{embeddedSection.Groups[2].Value}");
                }
            }

            string trimmed = cleanSearch.TrimEnd(')', ',', '.', ';', ':');
            string digitsOnly = new string(cleanSearch.Where(char.IsDigit).ToArray());
            bool looksLikeFigureNum = digitsOnly.Length > 0 && digitsOnly.Length <= 2 &&
                (cleanSearch.TrimEnd(')', ',', '.', ';', ':').All(c => char.IsDigit(c)) ||
                 System.Text.RegularExpressions.Regex.IsMatch(cleanSearch, @"^\d+\)$"));
            if (looksLikeFigureNum)
            {
                foreach (var prefix in new[] { "圖", ":圖", "即圖", "表", "Fig.", "Figure", "Table" })
                {
                    AddPattern(prefix + digitsOnly);
                    AddPattern(prefix + digitsOnly + ")");
                }
                if (cleanSearch.Contains(')'))
                {
                    AddPattern(digitsOnly + ")");
                }
            }
            else if (trimmed.Length > 0)
            {
                AddPattern(trimmed);
            }

            if (!looksLikeFigureNum)
            {
                AddPattern(cleanSearch);
            }

            var romanOrDigits = new string(cleanSearch.Where(c => char.IsDigit(c) || "IVXLCDMivxlcdm".Contains(c)).ToArray());
            bool isBareNumber = cleanSearch.Length <= 3 && romanOrDigits.Length == cleanSearch.TrimEnd(')', ',', '.').Length;
            if (!looksLikeFigureNum && (romanOrDigits.Length >= 2 || isBareNumber))
            {
                AddPattern(romanOrDigits);
            }

            return patterns;
        }

        public static string NormalizeAnnotationSearchText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            string clean = new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (clean.Length == 0) return raw.Trim();

            var citation = System.Text.RegularExpressions.Regex.Match(clean, @"\[\d+\]");
            if (citation.Success) return citation.Value;

            var tableRoman = System.Text.RegularExpressions.Regex.Match(
                clean,
                @"(?:Table|TABLE)\s*([IVXLCDM]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (tableRoman.Success)
            {
                return tableRoman.Groups[1].Value.ToUpperInvariant();
            }

            var sectionRoman = System.Text.RegularExpressions.Regex.Match(
                clean,
                @"Section\s*([IVXLCDM]+)\)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (sectionRoman.Success)
            {
                return sectionRoman.Groups[1].Value.ToUpperInvariant();
            }

            if (clean.Length <= 8)
            {
                var embeddedRoman = System.Text.RegularExpressions.Regex.Match(
                    clean,
                    @"([IVXLCDM]{2,})\)?",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (embeddedRoman.Success)
                {
                    return embeddedRoman.Groups[1].Value.ToUpperInvariant();
                }

                var leadingRoman = System.Text.RegularExpressions.Regex.Match(
                    clean,
                    @"^([IVXLCDM]{1,4})(?![IVXLCDM])",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (leadingRoman.Success)
                {
                    return leadingRoman.Groups[1].Value.ToUpperInvariant();
                }

                var bareRomanPunct = System.Text.RegularExpressions.Regex.Match(
                    clean,
                    @"^([IVXLCDM]{1,4})[,.]$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (bareRomanPunct.Success)
                {
                    return bareRomanPunct.Groups[1].Value.ToUpperInvariant();
                }
            }

            var section = System.Text.RegularExpressions.Regex.Match(
                clean,
                @"([IVXLCDM]+)-([A-Z])\)?\.?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (section.Success)
            {
                string core = $"{section.Groups[1].Value}-{section.Groups[2].Value}";
                return clean.Contains(core + ")", System.StringComparison.OrdinalIgnoreCase) ? core + ")" : core;
            }

            var figure = System.Text.RegularExpressions.Regex.Match(
                clean,
                @"(?:Figure|Fig\.?|圖)(\d+)\)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (figure.Success)
            {
                string num = figure.Groups[1].Value;
                return clean.Contains(num + ")", System.StringComparison.Ordinal) ? num + ")" : num;
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"^\d\)?$"))
            {
                return clean;
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(clean, @"^\d+\)$"))
            {
                return clean;
            }

            var loneDigit = System.Text.RegularExpressions.Regex.Match(clean, @"(?<!\d)(\d)\)?(?!\d)");
            if (loneDigit.Success && clean.Length <= 8)
            {
                return loneDigit.Value;
            }

            return raw.Trim();
        }

        private static string RomanToChineseSectionNumeral(string roman)
        {
            if (string.IsNullOrEmpty(roman)) return "";
            return roman.ToUpperInvariant() switch
            {
                "I" => "一",
                "II" => "二",
                "III" => "三",
                "IV" => "四",
                "V" => "五",
                "VI" => "六",
                "VII" => "七",
                "VIII" => "八",
                "IX" => "九",
                "X" => "十",
                _ => ""
            };
        }

        private static List<List<RenderedChar>> FindTextOccurrences(List<RenderedChar> cleanRendered, string cleanSearch)
        {
            var occurrences = new List<List<RenderedChar>>();
            if (string.IsNullOrEmpty(cleanSearch)) return occurrences;

            bool requireDigitBoundary = cleanSearch.All(c => char.IsDigit(c) || c == ')') &&
                cleanSearch.Any(char.IsDigit);
            bool requireRomanBoundary = IsRomanNumeralPattern(cleanSearch);

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
                    if (!CharEqualsNormalized(cleanRendered[i + j].Character, cleanSearch[j]))
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

        private static bool IsStandaloneDigitOccurrence(List<RenderedChar> chars, int start, int length)
        {
            if (start > 0 && char.IsDigit(chars[start - 1].Character)) return false;
            int end = start + length;
            if (end < chars.Count && char.IsDigit(chars[end].Character)) return false;
            return true;
        }

        private static List<List<RenderedChar>> FindLooseFigureDigitOccurrences(
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

        private static List<RenderedChar> PickOccurrenceBySpatialPosition(
            List<RenderedChar> cleanRendered,
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

        private static List<RenderedChar> MapRenderedCharsBySpatialPosition(
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

    }
}

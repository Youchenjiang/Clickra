using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Clickra.Core.Processors
{
    internal static class PdfAnnotationPatternBuilder
    {
        public static List<string> BuildAnnotationSearchPatterns(string searchText)
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

            var sectionRomanMatch = Regex.Match(
                cleanSearch,
                @"(?:Section\s*)?([IVXLCDM]{2,})\)?",
                RegexOptions.IgnoreCase);
            if (sectionRomanMatch.Success)
            {
                string roman = sectionRomanMatch.Groups[1].Value.ToUpperInvariant();
                AddRomanSectionPatterns(AddPattern, roman);
            }

            var singleRomanMatch = Regex.Match(
                cleanSearch,
                @"^([IVXLCDM]{1,4})[,.]?$",
                RegexOptions.IgnoreCase);
            if (singleRomanMatch.Success)
            {
                string roman = singleRomanMatch.Groups[1].Value.ToUpperInvariant();
                AddRomanSectionPatterns(AddPattern, roman);
            }

            if (Regex.IsMatch(cleanSearch, @"^\d+\)$"))
            {
                string listingNum = new string(cleanSearch.Where(char.IsDigit).ToArray());
                AddPattern(cleanSearch);
                AddPattern($"{listingNum})");
                AddPattern($"第{listingNum}");
                AddPattern($"清單{listingNum}");
                AddPattern($"清單 {listingNum}");
            }

            var sectionMatch = Regex.Match(
                cleanSearch,
                @"^([IVXLCDM]+)-([A-Z])\)?[,;.:]?$",
                RegexOptions.IgnoreCase);
            if (sectionMatch.Success)
            {
                AddPattern($"{sectionMatch.Groups[1].Value}-{sectionMatch.Groups[2].Value}");
            }
            else
            {
                var embeddedSection = Regex.Match(
                    cleanSearch,
                    @"([IVXLCDM]+)-([A-Z])\)?",
                    RegexOptions.IgnoreCase);
                if (embeddedSection.Success)
                {
                    AddPattern($"{embeddedSection.Groups[1].Value}-{embeddedSection.Groups[2].Value}");
                }
            }

            string trimmed = cleanSearch.TrimEnd(')', ',', '.', ';', ':');
            string digitsOnly = new string(cleanSearch.Where(char.IsDigit).ToArray());
            bool looksLikeFigureNum = digitsOnly.Length > 0 && digitsOnly.Length <= 2 &&
                (cleanSearch.TrimEnd(')', ',', '.', ';', ':').All(c => char.IsDigit(c)) ||
                 Regex.IsMatch(cleanSearch, @"^\d+\)$"));
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

        public static bool CharEqualsNormalized(char c1, char c2)
        {
            if (c1 == c2) return true;
            if (char.ToUpperInvariant(c1) == char.ToUpperInvariant(c2)) return true;
            return false;
        }

        public static bool IsRomanNumeralPattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            string stripped = pattern.TrimStart('第').Trim();
            return stripped.Length >= 1 && stripped.Length <= 6 &&
                stripped.All(c => "IVXLCDMivxlcdm".Contains(c));
        }

        public static bool PrefersVerticalAlignment(string pattern)
        {
            return pattern.StartsWith("圖", StringComparison.Ordinal) ||
                pattern.StartsWith(":圖", StringComparison.Ordinal) ||
                pattern.StartsWith("即圖", StringComparison.Ordinal) ||
                pattern.StartsWith("表", StringComparison.Ordinal) ||
                IsRomanNumeralPattern(pattern);
        }

        public static string ExtractRomanSectionNumeral(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText)) return "";

            string clean = new string(searchText.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (clean.Length == 0) return "";

            var sectionRoman = Regex.Match(
                clean,
                @"Section\s*([IVXLCDM]+)\)?",
                RegexOptions.IgnoreCase);
            if (sectionRoman.Success)
            {
                return sectionRoman.Groups[1].Value.ToUpperInvariant();
            }

            var embeddedRoman = Regex.Match(
                clean,
                @"([IVXLCDM]{2,})\)?",
                RegexOptions.IgnoreCase);
            if (embeddedRoman.Success && clean.Length <= 12)
            {
                return embeddedRoman.Groups[1].Value.ToUpperInvariant();
            }

            return "";
        }

        public static string NormalizeAnnotationSearchText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            string clean = new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (clean.Length == 0) return raw.Trim();

            var citation = Regex.Match(clean, @"\[\d+\]");
            if (citation.Success) return citation.Value;

            var tableRoman = Regex.Match(
                clean,
                @"(?:Table|TABLE)\s*([IVXLCDM]+)",
                RegexOptions.IgnoreCase);
            if (tableRoman.Success)
            {
                return tableRoman.Groups[1].Value.ToUpperInvariant();
            }

            var sectionRoman = Regex.Match(
                clean,
                @"Section\s*([IVXLCDM]+)\)?",
                RegexOptions.IgnoreCase);
            if (sectionRoman.Success)
            {
                return sectionRoman.Groups[1].Value.ToUpperInvariant();
            }

            if (clean.Length <= 8)
            {
                var embeddedRoman = Regex.Match(
                    clean,
                    @"([IVXLCDM]{2,})\)?",
                    RegexOptions.IgnoreCase);
                if (embeddedRoman.Success)
                {
                    return embeddedRoman.Groups[1].Value.ToUpperInvariant();
                }

                var leadingRoman = Regex.Match(
                    clean,
                    @"^([IVXLCDM]{1,4})(?![IVXLCDM])",
                    RegexOptions.IgnoreCase);
                if (leadingRoman.Success)
                {
                    return leadingRoman.Groups[1].Value.ToUpperInvariant();
                }

                var bareRomanPunct = Regex.Match(
                    clean,
                    @"^([IVXLCDM]{1,4})[,.]$",
                    RegexOptions.IgnoreCase);
                if (bareRomanPunct.Success)
                {
                    return bareRomanPunct.Groups[1].Value.ToUpperInvariant();
                }
            }

            var section = Regex.Match(
                clean,
                @"([IVXLCDM]+)-([A-Z])\)?\.?",
                RegexOptions.IgnoreCase);
            if (section.Success)
            {
                string core = $"{section.Groups[1].Value}-{section.Groups[2].Value}";
                return clean.Contains(core + ")", StringComparison.OrdinalIgnoreCase) ? core + ")" : core;
            }

            var figure = Regex.Match(
                clean,
                @"(?:Figure|Fig\.?|圖)(\d+)\)?",
                RegexOptions.IgnoreCase);
            if (figure.Success)
            {
                string num = figure.Groups[1].Value;
                return clean.Contains(num + ")", StringComparison.Ordinal) ? num + ")" : num;
            }

            if (Regex.IsMatch(clean, @"^\d\)?$"))
            {
                return clean;
            }

            if (Regex.IsMatch(clean, @"^\d+\)$"))
            {
                return clean;
            }

            var loneDigit = Regex.Match(clean, @"(?<!\d)(\d)\)?(?!\d)");
            if (loneDigit.Success && clean.Length <= 8)
            {
                return loneDigit.Value;
            }

            return raw.Trim();
        }

        public static string RomanToChineseSectionNumeral(string roman)
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

        private static void AddRomanSectionPatterns(Action<string> addPattern, string roman)
        {
            addPattern(roman);
            addPattern($"第{roman}");
            addPattern($"第 {roman}");
            addPattern($"表{roman}");
            addPattern($"表 {roman}");

            string chinese = RomanToChineseSectionNumeral(roman);
            if (!string.IsNullOrEmpty(chinese))
            {
                addPattern($"第{chinese}");
                addPattern(chinese);
                addPattern($"表{chinese}");
            }
        }
    }
}

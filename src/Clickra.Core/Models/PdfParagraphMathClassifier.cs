using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;

namespace Clickra.Core.Models
{
    internal static class PdfParagraphMathClassifier
    {
        public static string CleanFontName(string fontName)
        {
            int plusIdx = fontName.IndexOf('+');
            return plusIdx >= 0 && plusIdx < fontName.Length - 1
                ? fontName.Substring(plusIdx + 1)
                : fontName;
        }

        public static bool IsMathWord(Word word)
        {
            foreach (var letter in word.Letters)
            {
                var fontName = letter.FontName;
                if (fontName != null && PdfParagraph.MathFontRegex.IsMatch(CleanFontName(fontName)))
                {
                    return true;
                }

                if (letter.Value == null) continue;
                if (letter.Value.StartsWith("(cid:", StringComparison.OrdinalIgnoreCase)) return true;
                foreach (int cp in GetCodepoints(letter.Value))
                {
                    if (IsMathCodepoint(cp)) return true;
                }
            }
            return false;
        }

        public static bool IsMathCharacter(Letter letter, bool isMathWord, double averageFontSize)
        {
            if (letter.Value == "\u2022" || letter.Value == "\u2022")
            {
                return false;
            }

            var fontName = letter.FontName;
            if (fontName != null && PdfParagraph.MathFontRegex.IsMatch(CleanFontName(fontName)))
            {
                return true;
            }

            if (letter.Value != null && letter.Value.StartsWith("(cid:", StringComparison.OrdinalIgnoreCase)) return true;

            if (letter.Value != null)
            {
                foreach (int cp in GetCodepoints(letter.Value))
                {
                    if (IsMathCodepoint(cp)) return true;
                }

                if (isMathWord && letter.PointSize < averageFontSize * 0.79) return true;
            }

            return false;
        }

        public static bool IsMathLine(TextLine line)
        {
            if (Regex.IsMatch(line.Text.Trim(), @"\(\d+\)\s*$")) return true;

            if (Regex.IsMatch(line.Text.Trim(), @"^\s*(?:[•\-*]|\d+[\.\)]|[a-zA-Z][\.\)]|\[\d+\])\s*$")) return false;

            int proseLetters = 0;
            foreach (var word in line.Words)
            {
                bool isProseWord = IsProseWord(word);
                if (isProseWord)
                {
                    proseLetters += word.Letters.Count;
                }
            }

            return proseLetters <= 2;
        }

        private static bool IsProseWord(Word word)
        {
            if (word.Letters.Count <= 1) return false;

            int nonAlphaCount = word.Letters.Count(l => l.Value.Length > 0 && !char.IsLetter(l.Value[0]));
            if ((double)nonAlphaCount / word.Letters.Count > 0.3) return false;

            foreach (var letter in word.Letters)
            {
                var fontName = letter.FontName;
                if (fontName != null && PdfParagraph.MathFontRegex.IsMatch(CleanFontName(fontName)))
                {
                    return false;
                }

                if (letter.Value == null) continue;
                if (letter.Value.StartsWith("(cid:", StringComparison.OrdinalIgnoreCase)) return false;
                foreach (int cp in GetCodepoints(letter.Value))
                {
                    if (IsMathCodepoint(cp)) return false;
                }
            }

            return true;
        }

        private static bool IsMathCodepoint(int codepoint)
        {
            if ((codepoint >= 0x0370 && codepoint <= 0x03FF) || (codepoint >= 0x1F00 && codepoint <= 0x1FFF)) return true;
            if (codepoint >= 0x2200 && codepoint <= 0x22FF) return true;
            if (codepoint >= 0x2A00 && codepoint <= 0x2AFF) return true;
            if (codepoint >= 0x2100 && codepoint <= 0x214F) return true;
            if (codepoint >= 0x2190 && codepoint <= 0x21FF) return true;
            if (codepoint >= 0x27F0 && codepoint <= 0x27FF) return true;
            if (codepoint >= 0x2900 && codepoint <= 0x297F) return true;
            if ((codepoint >= 0x27C0 && codepoint <= 0x27EF) || (codepoint >= 0x2980 && codepoint <= 0x29FF)) return true;
            if (codepoint >= 0x1D400 && codepoint <= 0x1D7FF) return true;
            return false;
        }

        private static IEnumerable<int> GetCodepoints(string s)
        {
            if (string.IsNullOrEmpty(s)) yield break;
            for (int i = 0; i < s.Length; i++)
            {
                if (i < s.Length - 1 && char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1]))
                {
                    yield return char.ConvertToUtf32(s[i], s[i + 1]);
                    i++;
                }
                else
                {
                    yield return s[i];
                }
            }
        }
    }
}

using System;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    public static class FontUtilities
    {
        public static bool IsLaTeXMediumFont(string? fontName)
        {
            return !string.IsNullOrEmpty(fontName) &&
                   fontName.Contains("Medi", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSourceFontBold(string? fontName)
        {
            if (string.IsNullOrEmpty(fontName)) return false;
            // IEEE PDFs commonly encode bold Nimbus text as
            // "NimbusRomNo9L-Medi" rather than using the word Bold.  Do not
            // classify other TeX medium/math faces this way.
            if (fontName.Contains("NimbusRom", StringComparison.OrdinalIgnoreCase) &&
                fontName.Contains("Medi", StringComparison.OrdinalIgnoreCase))
                return true;
            if (IsLaTeXMediumFont(fontName)) return false;
            return fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("bx", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("bf", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCjkTranslationFont(string fontName)
        {
            return fontName.Contains("DFKai", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("kaiu", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("Malgun", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("JhengHei", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("Microsoft JhengHei", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("Microsoft YaHei", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("MS Gothic", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsLineBold(TextLine line)
        {
            if (line == null || line.Words == null || line.Words.Count == 0) return false;
            int totalCount = 0;
            int boldCount = 0;
            foreach (var word in line.Words)
            {
                foreach (var letter in word.Letters)
                {
                    totalCount++;
                    if (IsSourceFontBold(letter.FontName))
                    {
                        boldCount++;
                    }
                }
            }
            return totalCount > 0 && ((double)boldCount / totalCount) > 0.5;
        }

        public static string NormalizeMathValue(string val)
        {
            if (string.IsNullOrEmpty(val)) return val;
            var sb = new StringBuilder();
            for (int i = 0; i < val.Length; i++)
            {
                int cp = val[i];
                if (i < val.Length - 1 && char.IsHighSurrogate(val[i]) && char.IsLowSurrogate(val[i + 1]))
                {
                    cp = char.ConvertToUtf32(val[i], val[i + 1]);
                    i++;
                }

                if (cp < 0x20 || cp == 0x7F)
                {
                    continue;
                }

                if (cp == 0x20DD)
                {
                    if (sb.Length > 0 && sb[^1] is >= '1' and <= '9')
                    {
                        sb[^1] = (char)(0x2460 + (sb[^1] - '1'));
                    }
                    else if (sb.Length > 0 && sb[^1] == '0')
                    {
                        sb[^1] = '\u24EA';
                    }
                    continue;
                }

                if (cp == 0x02D8)
                {
                    continue;
                }

                if (cp >= 0x1D400 && cp <= 0x1D7FF)
                {
                    sb.Append(NormalizeMathCodePoint(cp));
                }
                else
                {
                    sb.Append(char.ConvertFromUtf32(cp));
                }
            }
            return sb.ToString();
        }

        private static string NormalizeMathCodePoint(int cp)
        {
            int[] latinUpper = [0x1D400, 0x1D434, 0x1D468, 0x1D49C, 0x1D4D0, 0x1D504, 0x1D538, 0x1D56C, 0x1D5A0, 0x1D5D4, 0x1D608, 0x1D63C, 0x1D670];
            foreach (int start in latinUpper)
            {
                if (cp >= start && cp <= start + 25) return ((char)('A' + (cp - start))).ToString();
            }

            int[] latinLower = [0x1D41A, 0x1D44E, 0x1D482, 0x1D4B6, 0x1D4EA, 0x1D51E, 0x1D552, 0x1D586, 0x1D5BA, 0x1D5EE, 0x1D622, 0x1D656, 0x1D68A];
            foreach (int start in latinLower)
            {
                if (cp >= start && cp <= start + 25) return ((char)('a' + (cp - start))).ToString();
            }

            int[] greekUpper = [0x1D6A8, 0x1D6E2, 0x1D71C, 0x1D756, 0x1D790];
            foreach (int start in greekUpper)
            {
                if (cp >= start && cp <= start + 24) return ((char)(0x0391 + (cp - start))).ToString();
            }

            int[] greekLower = [0x1D6C2, 0x1D6FC, 0x1D736, 0x1D770, 0x1D7AA];
            foreach (int start in greekLower)
            {
                if (cp >= start && cp <= start + 24) return ((char)(0x03B1 + (cp - start))).ToString();
            }

            int[] digits = [0x1D7CE, 0x1D7D8, 0x1D7E2, 0x1D7EC, 0x1D7F6];
            foreach (int start in digits)
            {
                if (cp >= start && cp <= start + 9) return ((char)('0' + (cp - start))).ToString();
            }

            return char.ConvertFromUtf32(cp);
        }

        /// <summary>
        /// Applies compatibility normalization while preserving Unicode
        /// circled caption markers. FormKD expands ①/②/③ into bare digits,
        /// which would destroy restored figure-caption semantics at render time.
        /// </summary>
        public static string NormalizeRenderValue(string val)
        {
            if (string.IsNullOrEmpty(val)) return val;

            var sb = new StringBuilder(val.Length);
            int segmentStart = 0;
            for (int i = 0; i < val.Length; i++)
            {
                char c = val[i];
                if (!IsCircledCaptionMarker(c)) continue;

                if (i > segmentStart)
                    sb.Append(val.Substring(segmentStart, i - segmentStart).Normalize(NormalizationForm.FormKD));
                sb.Append(c);
                segmentStart = i + 1;
            }

            if (segmentStart < val.Length)
                sb.Append(val.Substring(segmentStart).Normalize(NormalizationForm.FormKD));

            return NormalizeMathValue(sb.ToString());
        }

        public static bool IsCircledCaptionMarker(char c) =>
            c is >= '\u2460' and <= '\u2473' or '\u24EA';

        public static XFont GetMathFont(string originalFontName, double fontSize)
        {
            bool isItalic = originalFontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                            originalFontName.Contains("CMMI", StringComparison.OrdinalIgnoreCase) ||
                            originalFontName.Contains("mi", StringComparison.OrdinalIgnoreCase);
            bool isBold = IsSourceFontBold(originalFontName);

            var style = XFontStyleEx.Regular;
            if (isItalic && isBold) style = XFontStyleEx.BoldItalic;
            else if (isItalic) style = XFontStyleEx.Italic;
            else if (isBold) style = XFontStyleEx.Bold;

            string fontName = "Times New Roman";
            if (originalFontName.Contains("Helvetica", StringComparison.OrdinalIgnoreCase) ||
                originalFontName.Contains("Arial", StringComparison.OrdinalIgnoreCase) ||
                originalFontName.Contains("Sans", StringComparison.OrdinalIgnoreCase) ||
                originalFontName.Contains("SFNSText", StringComparison.OrdinalIgnoreCase))
            {
                fontName = "Arial";
            }
            else if (originalFontName.Contains("Sym", StringComparison.OrdinalIgnoreCase) ||
                originalFontName.Contains("Math", StringComparison.OrdinalIgnoreCase) ||
                originalFontName.Contains("MSAM", StringComparison.OrdinalIgnoreCase) ||
                originalFontName.Contains("MSBM", StringComparison.OrdinalIgnoreCase) ||
                originalFontName.Contains("CMSY", StringComparison.OrdinalIgnoreCase))
            {
                fontName = "Cambria Math";
            }
            else if (originalFontName.Contains("Courier", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("Console", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("Inconsolata", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("Typewriter", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("NimbusMon", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("MonL", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("cmtt", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("ectt", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("sftt", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("Teletype", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
                     originalFontName.Contains("Code", StringComparison.OrdinalIgnoreCase) ||
                     Regex.IsMatch(originalFontName, @"tt\d+", RegexOptions.IgnoreCase))
            {
                fontName = "Courier New";
            }

            try
            {
                return new XFont(fontName, fontSize, style);
            }
            catch
            {
                // Some embedded/subset math fonts request a style that the active
                // resolver cannot provide. Keep PDF reconstruction alive with a
                // broadly available regular face.
                try { return new XFont(fontName, fontSize, XFontStyleEx.Regular); }
                catch { return new XFont("Arial", fontSize, XFontStyleEx.Regular); }
            }
        }

        public static bool IsMathOrGreekCharacter(char c)
        {
            return (c >= 0x0370 && c <= 0x03FF) ||
                   (c >= 0x1F00 && c <= 0x1FFF) ||
                   (c >= 0x2200 && c <= 0x22FF) ||
                   (c >= 0x2100 && c <= 0x214F) ||
                   (c >= 0x2190 && c <= 0x21FF) ||
                   (c >= 0x27C0 && c <= 0x27EF) ||
                   (c >= 0x2980 && c <= 0x29FF) ||
                   (c >= 0x2900 && c <= 0x297F) ||
                   (c >= 0x27F0 && c <= 0x27FF) ||
                   c == '\u00D7' || c == '\u00F7' || c == '\u00B1' || c == '\u2213' || c == '\u2217';
        }

        public static bool IsLatinExtendedOrSymbol(char c)
        {
            if (c >= 0x0080 && c <= 0x024F) return true;
            if (c is >= '\u2460' and <= '\u2473' or '\u24EA') return true;
            return IsMathOrGreekCharacter(c);
        }

        public static bool IsCjkCharacter(char c)
        {
            return (c >= 0x4E00 && c <= 0x9FFF) ||
                   (c >= 0x3400 && c <= 0x4DBF) ||
                   (c >= 0x3000 && c <= 0x303F) ||
                   (c >= 0x3040 && c <= 0x30FF) ||
                   (c >= 0x3100 && c <= 0x312F) ||
                   (c >= 0xAC00 && c <= 0xD7AF) ||
                   (c >= 0x1100 && c <= 0x11FF) ||
                   c == '\uFF0C' || c == '\u3002' || c == '\u3001' || c == '\uFF1B' || c == '\uFF1A' || c == '\uFF1F' || c == '\uFF01';
        }

        public static string GetCleanFontName(string fontName)
        {
            if (string.IsNullOrEmpty(fontName)) return "";
            int plusIdx = fontName.IndexOf('+');
            if (plusIdx >= 0 && plusIdx < fontName.Length - 1)
            {
                return fontName.Substring(plusIdx + 1);
            }
            return fontName;
        }

        public static bool IsMonospaceFont(string fontName)
        {
            if (string.IsNullOrEmpty(fontName)) return false;
            return fontName.Contains("Courier", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("Console", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("Inconsolata", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("Typewriter", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("NimbusMon", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("MonL", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("cmtt", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("ectt", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("sftt", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("Teletype", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
                   fontName.Contains("Code", StringComparison.OrdinalIgnoreCase) ||
                   Regex.IsMatch(fontName, @"tt\d+", RegexOptions.IgnoreCase);
        }

        public static bool ShouldMergeFormula(MathFormula formula, double averageFontSize)
        {
            if (formula.Letters.Count <= 1) return false;

            foreach (var l in formula.Letters)
            {
                if (l.Value.Length == 1 && IsMathOrGreekCharacter(l.Value[0]))
                {
                    return false;
                }
            }

            double minY = formula.Letters.Min(l => l.RelativeY);
            double maxY = formula.Letters.Max(l => l.RelativeY);
            double yDiff = maxY - minY;

            if (yDiff > averageFontSize * 0.15) return false;

            return true;
        }
    }
}

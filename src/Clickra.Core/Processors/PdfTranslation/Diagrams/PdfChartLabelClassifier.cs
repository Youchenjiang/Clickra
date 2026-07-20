using System;
using System.Text.RegularExpressions;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfChartLabelClassifier
    {
        public static bool IsLikelyChartLabel(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;
            // ACM charts often extract all subfigure labels into one line,
            // e.g. "(e) CargoTracker (f) PetClinic (g) DayTrader (h) App X".
            // This is figure artwork, not translatable prose.
            if (Regex.IsMatch(
                    txt,
                    @"^\([a-h]\)\s+\S+(?:\s+\S+){0,2}(?:\s+\([a-h]\)\s+\S+(?:\s+\S+){0,2})+$",
                    RegexOptions.IgnoreCase))
            {
                return true;
            }
            int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount <= 4 && para.Height <= 22 && txt.IndexOf('.') < 0) return true;
            if (para.Height <= 14 && txt.Length <= 8) return true;
            if (txt.StartsWith("(a)", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("(b)", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("(c)", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (Regex.IsMatch(txt, @"^(I\.G\.|V\.A\.|E\.?|Cost|Models?)$", RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (txt.Contains('%') && para.Width < 30 && para.Height >= 25)
            {
                return true;
            }
            if (IsLikelyBarChartAxisLabel(para))
            {
                return true;
            }
            if (txt.Equals("LLM", StringComparison.OrdinalIgnoreCase) && para.Height <= 14)
            {
                return true;
            }
            if (wordCount <= 6 && para.Height <= 12 &&
                (txt.Contains('–') || txt.Contains('-')) &&
                txt.IndexOf('.') < 0)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Universal chart tick detector: any paragraph that is physically tiny
        /// (height &lt; 7pt, width &lt; 14pt) and contains only digits, a percent value,
        /// or a single letter is an axis tick / legend mark that must never be translated.
        /// Extremely tiny glyphs (height &lt; 5pt, width &lt; 8pt) are bypassed unconditionally
        /// since no body text can be this small — these are legend color patches or tick marks.
        /// </summary>
        public static bool IsChartTickGlyph(PdfParagraph para)
        {
            // Tier 1: unconditional bypass for micro-glyphs (legend patches, dot ticks, etc.)
            if (para.Height < 5.0 && para.Width < 8.0) return true;
            // Tier 2: tiny glyphs with numeric/single-letter content. Some ACM bar charts
            // render tick labels at ~7.6pt high and ~6.8pt wide (PentestAgent Fig. 7);
            // if these are translated/masked, the mask expands to the whole column and
            // erases the bars behind them.
            if (para.Height > 8.2 || para.Width > 20.0) return false;
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;
            // Pure integer or decimal (e.g. "0", "100", "3.5")
            if (Regex.IsMatch(txt, @"^\d+(\.\d+)?%?$")) return true;
            // Single ASCII letter (e.g. axis tick labels like "A", "B")
            if (txt.Length == 1 && char.IsLetter(txt[0]) && txt[0] < 128) return true;
            return false;
        }

        public static bool IsLikelyBarChartAxisLabel(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;
            if (Regex.IsMatch(txt,
                    @"^(?:Compeletion|Completion)\s+Level\s*\(\s*%\s*\)$",
                    RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (Regex.IsMatch(txt,
                    @"^Success\s+Rate\s*\(\s*%\s*\)(?:\s+\d+)?$",
                    RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (txt.Equals("Models", StringComparison.OrdinalIgnoreCase) && para.Height <= 22 && para.Width <= 70)
            {
                return true;
            }
            if (Regex.IsMatch(txt, @"^\(\s*[abc]\s*\)\s*$", RegexOptions.IgnoreCase) &&
                para.Height <= 18)
            {
                return true;
            }
            return false;
        }
    }
}

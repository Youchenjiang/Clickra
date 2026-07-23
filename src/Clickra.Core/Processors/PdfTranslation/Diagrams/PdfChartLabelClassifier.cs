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
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(1)))
            {
                return true;
            }
            int wordCount = txt.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount <= 4 && para.Height <= 22 && txt.IndexOf('.') < 0) return true;
            if (para.Height <= 14 && txt.Length <= 8) return true;
            if (txt.StartsWith("(a)", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("(b)", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("(c)", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (Regex.IsMatch(txt, @"^(I\.G\.|V\.A\.|E\.?|Cost|Models?)$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
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

        public static bool IsChartTickGlyph(PdfParagraph para)
        {
            if (para.Height < 5.0 && para.Width < 8.0) return true;
            if (para.Height > 8.2 || para.Width > 20.0) return false;
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;
            if (Regex.IsMatch(txt, @"^\d+(\.\d+)?%?$", RegexOptions.None, TimeSpan.FromSeconds(1))) return true;
            if (txt.Length == 1 && char.IsLetter(txt[0]) && txt[0] < 128) return true;
            return false;
        }

        public static bool IsLikelyBarChartAxisLabel(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;
            if (Regex.IsMatch(txt,
                    @"^(?:Compeletion|Completion)\s+Level\s*\(\s*%\s*\)$",
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(1)))
            {
                return true;
            }
            if (Regex.IsMatch(txt,
                    @"^Success\s+Rate\s*\(\s*%\s*\)(?:\s+\d+)?$",
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(1)))
            {
                return true;
            }
            if (txt.Equals("Models", StringComparison.OrdinalIgnoreCase) && para.Height <= 22 && para.Width <= 70)
            {
                return true;
            }
            if (Regex.IsMatch(txt, @"^\(\s*[abc]\s*\)\s*$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)) &&
                para.Height <= 18)
            {
                return true;
            }
            return false;
        }
    }
}

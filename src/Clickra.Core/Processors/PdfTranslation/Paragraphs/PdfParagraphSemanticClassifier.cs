using System;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfParagraphSemanticClassifier
    {
        public static bool IsEquationParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;

            // Matches (1), (2), (3), etc. at the end
            if (Regex.IsMatch(txt, @"\(\d+\)\s*$")) return true;

            // Matches patterns like x : A -> B
            if (Regex.IsMatch(txt, @"^[a-zA-Z0-9_\{\}\s]+:.*(⇀|→|→|↦|⇒|⊆|∈)")) return true;

            // Density based check: if the text has math formulas/variables placeholders
            // and contains common math operator characters
            int formulaTokensCount = para.Formulas.Count;
            if (formulaTokensCount > 0)
            {
                // Check if the non-placeholder part contains mostly math operators or is very short
                string stripped = Regex.Replace(txt, @"\{v\d+\}", "").Trim();
                if (string.IsNullOrEmpty(stripped)) return true;

                int letters = stripped.Count(char.IsLetter);
                int operators = stripped.Count(c => "=+-*/()[]{}<>,.:;|\\&!_^⇀→∈∧↓⟨⟩⊆×Σ∗↑↓⇀".Contains(c));
                int wordCount = stripped.Split(
                    new[] { ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries).Length;

                // Short display equations can retain identifier fragments such
                // as "raw", "QL", and "s.t." around several formula tokens.
                // Translating them as prose linearizes independently positioned
                // glyphs and creates overlapping formulas (SemTaint p8 eq. 10).
                if (formulaTokensCount >= 2 && wordCount <= 5 && para.Height <= 18)
                {
                    return true;
                }

                // If the stripped text contains mostly math operators/punctuation rather than English words
                if (letters < 3 || (double)operators / (letters + operators) > 0.4)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsHeadingParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;

            if (txt.Equals("Keywords", StringComparison.OrdinalIgnoreCase) ||
                txt.Equals("Keyword", StringComparison.OrdinalIgnoreCase) ||
                txt.Equals("關鍵字", StringComparison.OrdinalIgnoreCase) ||
                txt.Equals("关键字", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Section numbering like "1. Introduction" or "3.4.1 Projection before Fusion" or "3.2.1 資料收集"
            if (Regex.IsMatch(txt, @"^\d{1,2}(?:\.\d{1,2}){0,4}\.?(?:\s+[^a-z]|$)")) return true;

            // Lettered subsections like "A. Background" or "C. Case Studies"
            if (Regex.IsMatch(txt, @"^[A-Z]\.\s+")) return true;

            // Appendix subsections like "B.3 Benchmark Coverage"
            if (Regex.IsMatch(txt, @"^[A-Z]\.\d+\s")) return true;

            // Uppercase section headers like "REFERENCES", "ABSTRACT", "APPENDIX"
            if (txt.Length < 30 && txt.Any(char.IsLetter) && txt.All(c => !char.IsLower(c)))
            {
                if (txt.Length <= 6 && !txt.Contains(' ') &&
                    txt.All(c => char.IsUpper(c) || char.IsDigit(c) || c == '&'))
                {
                    return false;
                }
                return true;
            }

            return false;
        }

        /// <summary>Appendix headings (A Prompts, B., B.1, B.2, B.3) must never be gray-prompt content.</summary>
        public static bool IsAppendixSectionHeading(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;
            if (Regex.IsMatch(txt, @"^Appendix\s+[A-Z]", RegexOptions.IgnoreCase))
                return true;
            if (Regex.IsMatch(txt, @"^[A-Z]\s+Prompts\b", RegexOptions.IgnoreCase))
                return true;
            if (Regex.IsMatch(txt, @"^[A-Z]\.\d*\s", RegexOptions.IgnoreCase))
                return true;
            return txt.Length < 80 &&
                   Regex.IsMatch(txt, @"^[A-Z]\.\s+", RegexOptions.IgnoreCase);
        }
    }
}

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
            if (txt.All(c => char.IsDigit(c) || char.IsPunctuation(c) || char.IsWhiteSpace(c)))
                return false;

            if (txt.Equals("Keywords", StringComparison.OrdinalIgnoreCase) ||
                txt.Equals("Keyword", StringComparison.OrdinalIgnoreCase) ||
                txt.Equals("關鍵字", StringComparison.OrdinalIgnoreCase) ||
                txt.Equals("关键字", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Section numbering like "1. Introduction" or "3.4.1 Projection before Fusion" or "3.2.1 資料收集"
            if (Regex.IsMatch(txt, @"^\d{1,2}(?:\.\d{1,2}){0,4}\.?(?:\s+)(?=[A-Za-z\u3400-\u9FFF])")) return true;

            // Lettered subsections like "A. Background" or "C. Case Studies"
            if (Regex.IsMatch(txt, @"^[A-Z]\.\s+")) return true;

            // Appendix subsections like "B.3 Benchmark Coverage"
            if (Regex.IsMatch(txt, @"^[A-Z]\.\d+\s")) return true;

            // Roman-numbered sections are common in IEEE/ACM papers (for
            // example "I. INTRODUCTION").  PdfPig often reports these as a
            // single left-aligned line, so the numbering is the stable
            // semantic signal rather than the extracted alignment.
            if (Regex.IsMatch(txt, @"^(?:[IVXLCDM]{1,6})\.\s+", RegexOptions.IgnoreCase)) return true;

            // Short label-style headings are frequently extracted without their
            // original bold/centering metadata (for example, "The main
            // contributions of this work include:", "Naturalness:").  A
            // sentence ending in a colon and containing only a small number of
            // words is a heading/list introducer, not body prose.
            int labelWordCount = txt.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries).Length;
            if (txt.Length <= 80 && labelWordCount <= 12 &&
                txt.EndsWith(":", StringComparison.Ordinal) &&
                Regex.IsMatch(txt, @"^[A-Za-z][A-Za-z0-9 .,()'&/+-]*:$"))
                return true;

            // A small, conservative vocabulary covers unnumbered section
            // headings that occur in the regression corpus.  Requiring a
            // short line avoids promoting ordinary prose that happens to
            // mention one of these words.
            if (txt.Length <= 36 &&
                Regex.IsMatch(txt, @"^(?:Introduction|Background|Motivation|Methodology|Methods|Results|Discussion|Conclusion|Conclusions|Related Work|Acknowledg(?:e)?ments|References|摘要|引言|結論)$", RegexOptions.IgnoreCase))
                return true;

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

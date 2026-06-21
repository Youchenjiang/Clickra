using System;
using System.Text.RegularExpressions;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    public static class ReferenceSectionDetector
    {
        private static readonly Regex NumberedHeadingRegex = new(
            @"^(\d{1,2})\.\s*(?:REFERENCES?|BIBLIOGRAPHY|參考文獻)\s*\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsHeadingText(string txt)
        {
            if (string.IsNullOrWhiteSpace(txt)) return false;
            txt = txt.Trim();

            if (NumberedHeadingRegex.IsMatch(txt)) return true;

            return txt.Equals("REFERENCES", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("REFERENCE", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("BIBLIOGRAPHY", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("參考文獻", StringComparison.Ordinal);
        }

        public static string GetHeadingNumberPrefix(string txt)
        {
            if (string.IsNullOrWhiteSpace(txt)) return "";
            var headingMatch = NumberedHeadingRegex.Match(txt.Trim());
            return headingMatch.Success ? $"{headingMatch.Groups[1].Value}. " : "";
        }

        public static bool IsHeading(PdfParagraph para)
        {
            if (para.IsTable || para.IsCode || para.IsGrayPromptContent || para.IsDiagram) return false;
            if (!IsHeadingText(para.TextWithPlaceholders.Trim())) return false;

            // A lone "reference" also appears as a small field/cell label in figures and
            // result tables. Require section-heading geometry so it cannot start a
            // cross-page bibliography bypass from inside compact academic content.
            return para.AverageFontSize >= 8.0 ||
                   para.Width >= 70.0 ||
                   para.Height >= 10.0;
        }

        public static bool IsReferenceParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            return Regex.IsMatch(txt, @"^\[\d+\]") ||
                   txt.Contains("http", StringComparison.OrdinalIgnoreCase) ||
                   txt.Contains("doi:", StringComparison.OrdinalIgnoreCase) ||
                   txt.Contains("www.", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsTerminator(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;
            if (IsHeading(para)) return false;
            if (IsReferenceParagraph(para)) return false;

            if (txt.Contains("WORK DIVISION", StringComparison.OrdinalIgnoreCase)) return true;
            if (txt.Equals("APPENDIX", StringComparison.OrdinalIgnoreCase)) return true;
            if (txt.Contains("ACKNOWLEDGMENT", StringComparison.OrdinalIgnoreCase) ||
                txt.Contains("ACKNOWLEDGEMENT", StringComparison.OrdinalIgnoreCase))
                return true;

            if (Regex.IsMatch(txt, @"^\d{1,2}\.\s+[A-Za-z\u4e00-\u9fff]"))
                return true;

            if (Regex.IsMatch(txt, @"^Appendix\s+[A-Z]", RegexOptions.IgnoreCase))
                return true;
            if (Regex.IsMatch(txt, @"^[A-Z]\s+Prompts\b", RegexOptions.IgnoreCase))
                return true;
            if (Regex.IsMatch(txt, @"^[A-Z]\.\d+\s", RegexOptions.IgnoreCase))
                return true;
            if (txt.Length < 40 && Regex.IsMatch(txt, @"^[A-Z]\.\s+[A-Za-z\u4e00-\u9fff]", RegexOptions.IgnoreCase))
                return true;

            return false;
        }
    }
}

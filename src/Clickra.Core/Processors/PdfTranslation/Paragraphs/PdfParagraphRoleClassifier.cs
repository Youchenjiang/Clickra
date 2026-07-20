using System;
using System.Text.RegularExpressions;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfParagraphRoleClassifier
    {
        public static bool IsRunningHeaderOrFooter(PdfParagraph para, double pageHeight)
        {
            if (para.Y1 > pageHeight * 0.88 && para.Height < 22)
            {
                return true;
            }
            if (para.Y0 < pageHeight * 0.08 && para.Height < 14 && para.Width < 45)
            {
                return true;
            }
            return false;
        }

        public static bool IsFigureTableCaptionParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt)) return false;
            return txt.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("Fig.", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("Fig ", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("Table", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("表", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("圖", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsTranslatableBodyProse(PdfParagraph para)
        {
            if (PdfChartLabelClassifier.IsLikelyChartLabel(para)) return false;
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrWhiteSpace(txt)) return false;
            int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount >= 12 && para.Height >= 25 && txt.IndexOf('.') >= 0) return true;
            if (wordCount >= 10 && para.Width > 100 && txt.IndexOf('.') >= 0) return true;
            // A line cut at a page/column boundary can be a perfectly valid
            // continuation even when it has fewer than ten words and no final
            // period (ASTER p.417: "of test assertions, ..."). Treat a wide
            // lower-case continuation as prose so the original line is masked
            // and translated instead of being silently redrawn as a bypass.
            if (wordCount >= 5 && para.Width > 100 && char.IsLower(txt[0])) return true;
            return false;
        }

        public static bool IsTranslatableCalloutProse(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrWhiteSpace(txt)) return false;

            // Short research-question lines (for example ASTER p.417 RQ3)
            // are still prose. Without this classification their one-line
            // source bbox becomes a hard height limit and the renderer can
            // shrink the translated text to roughly half-size.
            if (para.Width > 100 && Regex.IsMatch(txt, @"^RQ\d+\s*:", RegexOptions.IgnoreCase))
                return true;

            // Narrow lower-case continuation lines (for example the second
            // line of ASTER p.417's "Research Questions" heading) are prose,
            // not labels. Keep their source typography instead of fitting the
            // translation into a 6pt extraction box.
            if (para.Width > 20 && para.Height <= 20 &&
                Regex.IsMatch(txt, @"^[a-z][A-Za-z\s,'\-]{2,}[.!?]?$"))
                return true;

            // RQ findings callout boxes (TOGLL p7/p8, ASTER p421). Keep the
            // singular `Finding 5:` form as well as `Findings:`.
            if (IsFindingCallout(para))
            {
                return true;
            }

            // Stage-marker body paragraphs inside workflow pages (section body, not diagram labels).
            if (Regex.IsMatch(txt,
                    @"^(?:Intelligence Gathering|Vulnerability Analysis|Exploitation|Knowledge (?:Acquisition|Extraction)):",
                    RegexOptions.IgnoreCase))
            {
                return true;
            }

            if (PdfParagraphSemanticClassifier.IsHeadingParagraph(para)) return true;
            return IsTranslatableBodyProse(para);
        }

        public static bool IsFindingCallout(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            return Regex.IsMatch(
                txt,
                @"^(?:RQ\d+\s+)?Findings?\s*\d*\s*:",
                RegexOptions.IgnoreCase);
        }
    }
}

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
            return false;
        }

        public static bool IsTranslatableCalloutProse(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrWhiteSpace(txt)) return false;

            // RQ findings callout boxes (TOGLL p7/p8).
            if (Regex.IsMatch(txt, @"^(?:RQ\d+\s+)?Findings?:", RegexOptions.IgnoreCase))
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
    }
}

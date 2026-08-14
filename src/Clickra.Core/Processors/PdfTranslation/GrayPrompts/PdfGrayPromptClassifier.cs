using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfGrayPromptClassifier
    {
        public static bool IsSectionIntroProse(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            return txt.StartsWith("The following", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("From our", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("In this section", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("After obtaining", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsGrayPromptBoxParagraph(PdfParagraph para)
        {
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrWhiteSpace(txt)) return false;
            if (!IsGrayPromptBoxTitleParagraph(para)) return false;
            if (txt.Contains("(Simplified)", StringComparison.OrdinalIgnoreCase)) return true;
            if (Regex.IsMatch(txt, @"\bPrompt\s*(?:\(Simplified\))?\s*$", RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (Regex.IsMatch(txt, @"\bExample\s*$", RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (Regex.IsMatch(txt,
                    @"(?:^|\b)(?:Prompt\s+for|System Message|Role-?play|\bCoT\b|Structured Output\b|Analysis Prompt\b)",
                    RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (txt.Contains("JSON format", StringComparison.OrdinalIgnoreCase) ||
                txt.Contains("FORMAT SPEC", StringComparison.OrdinalIgnoreCase) ||
                txt.Contains("OUTPUT FORMAT", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        /// <summary>Gray prompt titles are single-line box headers, not body prose ending in "prompt".</summary>
        public static bool IsGrayPromptBoxTitleParagraph(PdfParagraph para)
        {
            return para.Height <= 22 && para.Width <= 280;
        }

        public static bool IsGrayPromptBoxContinuationParagraph(PdfParagraph para, PdfParagraph? anchor)
        {
            // Body prose is never a gray-prompt continuation: it is handled by
            // the shaded-region geometry pass instead. Heading/callout exclusions
            // only apply when there is no block anchor (standalone decisions).
            // Inside an active gray-prompt block scan the continuation geometry
            // (same column, small gap) is the stronger signal: a prompt line that
            // ends in a colon can otherwise be misread as a heading and dropped
            // (PentestAgent p7 "Generate a concise summary...").
            if (IsExcludedStandaloneProse(para, anchor))
            {
                return false;
            }
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrWhiteSpace(txt)) return false;
            if (IsPromptListItem(txt, para)) return true;
            if (txt.StartsWith("LLM:", StringComparison.OrdinalIgnoreCase) ||
                txt.StartsWith("User:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (StartsWithAny(txt, PromptInstructionPrefixes)) return true;
            if (ContainsAny(txt, PromptFormatMarkers)) return true;
            if (anchor == null) return false;
            return HasAnchoredContinuationGeometry(para, anchor, txt) ||
                   HasAnchoredHyphenatedGeometry(para, anchor, txt);
        }

        /// <summary>Section list items ("2) Loss of Context:") and parenthesized numbers.</summary>
        private static bool IsPromptListItem(string txt, PdfParagraph para)
        {
            if (Regex.IsMatch(txt, @"^\(\d+\)")) return true;
            if (Regex.IsMatch(txt, @"^AMPLE\}?$", RegexOptions.IgnoreCase)) return true;
            if (!Regex.IsMatch(txt, @"^\d+\)")) return false;
            // Section body like "2) Loss of Context:" — not a prompt list item inside gray boxes.
            if (para.Height > 28 || para.Width > 250) return false;
            if (txt.Contains(" of ", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static bool HasAnchoredContinuationGeometry(PdfParagraph para, PdfParagraph anchor, string txt)
        {
            double gap = anchor.Y1 - para.Y1;
            double overlap = Math.Min(para.X1, anchor.X1) - Math.Max(para.X0, anchor.X0);
            double minWidth = Math.Min(para.Width, anchor.Width);
            return gap >= -2 && gap <= 32 && minWidth > 0 && overlap / minWidth >= 0.55 &&
                para.Height <= 22 && txt.Length <= 160 &&
                !PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) &&
                !PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para);
        }

        /// <summary>Hyphenated prompt lines split across PDF text blocks (e.g. "EX-" / "AMPLE}").</summary>
        private static bool HasAnchoredHyphenatedGeometry(PdfParagraph para, PdfParagraph anchor, string txt)
        {
            double gap = anchor.Y1 - para.Y1;
            double overlap = Math.Min(para.X1, anchor.X1) - Math.Max(para.X0, anchor.X0);
            double minWidth = Math.Min(para.Width, anchor.Width);
            return gap >= -2 && gap <= 18 && minWidth > 0 && overlap / minWidth >= 0.55 &&
                para.Height <= 14 && txt.Length <= 16;
        }

        private static bool IsExcludedStandaloneProse(PdfParagraph para, PdfParagraph? anchor)
        {
            if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para)) return true;
            if (anchor != null || para.IsGrayPromptContent) return false;
            return PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para) ||
                   PdfParagraphSemanticClassifier.IsHeadingParagraph(para) ||
                   PdfParagraphSemanticClassifier.IsAppendixSectionHeading(para);
        }

        private static readonly string[] PromptInstructionPrefixes =
        {
            "You ", "You\u2019re ", "You're ", "Analyze ", "Use your ", "For example",
            "Generate a ", "Your next task", "You should use ", "You should always ",
            "You should ", "When the results"
        };

        private static readonly string[] PromptFormatMarkers =
        {
            "JSON format", "FORMAT SPEC", "OUTPUT FORMAT", "{FORMAT"
        };

        private static bool StartsWithAny(string txt, string[] prefixes)
            => prefixes.Any(p => txt.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        private static bool ContainsAny(string txt, string[] markers)
            => markers.Any(m => txt.Contains(m, StringComparison.OrdinalIgnoreCase));

        public static bool IsGrayPromptSubheading(PdfParagraph para)
        {
            if (para.Height > 20) return false;
            string txt = para.TextWithPlaceholders.Trim();
            if (txt.Length > 48) return false;
            return txt.Equals("RAG", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("CoT", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("Role-play", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("Self-reflection", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("Structured Output", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("GPT-4", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("User:", StringComparison.OrdinalIgnoreCase);
        }

        public static bool HasNearbyGrayPromptAbove(
            PdfParagraph para, List<PdfParagraph> pageList, double maxGap = 55)
        {
            foreach (var other in pageList)
            {
                if (!other.IsGrayPromptContent) continue;
                if (!SharesGrayPromptColumn(para, other)) continue;
                double gap = other.Y0 - para.Y1;
                if (gap >= -2 && gap <= maxGap) return true;
            }
            return false;
        }

        public static bool SharesGrayPromptColumn(PdfParagraph a, PdfParagraph b)
        {
            double overlap = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0);
            double minWidth = Math.Min(a.Width, b.Width);
            return minWidth > 0 && overlap / minWidth >= 0.45;
        }

        public static bool IsGrayPromptCodeParagraph(PdfParagraph para)
        {
            if (para.IsGrayPromptContent) return true;
            if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) ||
                PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para) ||
                PdfParagraphSemanticClassifier.IsHeadingParagraph(para) ||
                PdfParagraphSemanticClassifier.IsAppendixSectionHeading(para))
            {
                return false;
            }
            if (IsGrayPromptBoxParagraph(para) || IsGrayPromptSubheading(para))
            {
                return true;
            }
            return IsGrayPromptBoxContinuationParagraph(para, null);
        }

        public static bool IsMisclassifiedPromptCode(PdfParagraph para)
        {
            if (!para.IsCode) return false;
            if (IsGrayPromptCodeParagraph(para)) return false;
            if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) ||
                PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para) ||
                PdfParagraphSemanticClassifier.IsHeadingParagraph(para) ||
                PdfParagraphSemanticClassifier.IsAppendixSectionHeading(para))
            {
                return true;
            }
            string txt = para.TextWithPlaceholders.Trim();
            int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount >= 8 && para.Width > 80 && txt.IndexOf('.') >= 0) return true;
            if (wordCount >= 6 && para.Height >= 14 && txt.IndexOf('.') >= 0 && txt.Any(char.IsLower))
            {
                return true;
            }
            return false;
        }
    }
}

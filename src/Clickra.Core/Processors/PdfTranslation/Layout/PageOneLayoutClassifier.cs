using System;
using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    public static class PageOneLayoutClassifier
    {
        public static PdfParagraph? FindTitleParagraph(List<PdfParagraph> pageList, double pageHeight)
        {
            return pageList
                .Where(para => para.Y1 > pageHeight * 0.85)
                // A wrapped paper title can be split into lines with unreliable
                // extracted font averages. Prefer the widest top-band paragraph
                // as the title anchor; this keeps a narrow continuation such as
                // "Generation" from becoming the page title by mistake.
                .OrderByDescending(para => para.Width)
                .ThenByDescending(para => para.AverageFontSize)
                .ThenByDescending(para => para.Y1)
                .FirstOrDefault();
        }

        public static bool TryGetAuthorBand(
            List<PdfParagraph> pageList, double pageHeight,
            out double titleBottom, out double abstractTop, out PdfParagraph? titlePara)
        {
            titleBottom = abstractTop = -1;
            titlePara = FindTitleParagraph(pageList, pageHeight);
            if (titlePara != null)
                titleBottom = titlePara.Y0;

            foreach (var p in pageList)
            {
                string txt = p.TextWithPlaceholders.TrimStart('\n', '\r', ' ', '\t');
                if (txt.StartsWith("ABSTRACT", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("摘要", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("Abstract", StringComparison.Ordinal))
                {
                    abstractTop = p.Y1;
                    break;
                }
            }

            return titlePara != null && titleBottom > 0 && abstractTop > 0 && titleBottom > abstractTop;
        }

        public static bool IsInAuthorBand(
            PdfParagraph para, double titleBottom, double abstractTop, PdfParagraph titlePara)
        {
            if (para.AverageFontSize >= 15.0) return false;
            if (para.Y0 < abstractTop || para.Y1 > titleBottom) return false;
            return true;
        }

        public static bool IsAuthorBlockParagraph(
            PdfParagraph para, List<PdfParagraph> pageList, double pageHeight)
        {
            if (!TryGetAuthorBand(pageList, pageHeight, out double titleBottom, out double abstractTop, out var titlePara) ||
                titlePara == null)
            {
                return false;
            }

            return IsInAuthorBand(para, titleBottom, abstractTop, titlePara);
        }

        public static double GetTitleClipBottom(
            double clipTop, double originalClipBottom, double measuredHeight)
        {
            // CJK glyphs commonly need a taller line box than the source Latin title.
            // Clipping to the original bbox cuts off the translated title even though
            // RenderParagraph has already measured the larger line height.
            return Math.Max(originalClipBottom, clipTop + measuredHeight + 3.0);
        }

        public static void ApplyAuthorBlockFlags(List<PdfParagraph> pageList, double pageHeight)
        {
            if (!TryGetAuthorBand(pageList, pageHeight, out double titleBottom, out double abstractTop, out var titlePara) ||
                titlePara == null)
            {
                Console.Error.WriteLine($"[AUTHOR] p1 TryGetAuthorBand FAILED titleBottom={titleBottom} abstractTop={abstractTop} titlePara={titlePara != null}");
                return;
            }

            foreach (var para in pageList)
            {
                if (!IsInAuthorBand(para, titleBottom, abstractTop, titlePara)) continue;
                para.IsBypassed = true;
                para.IsTable = false;
                para.IsDiagram = false;
                para.IsCode = false;
                para.IsGrayPromptContent = false;
            }
        }

        public static void MergeTitleWithSubtitle(List<PdfParagraph> pageList, double pageHeight)
        {
            var titlePara = FindTitleParagraph(pageList, pageHeight);
            if (titlePara == null) return;
            titlePara.IsPageTitle = true;

            // The PDF extractor can split a wrapped paper title into several
            // same-sized line paragraphs (ASTER's second line is split into
            // "Generation" and "with LLMs").  The old implementation merged
            // only one candidate into the title, leaving the other line as a
            // normal paragraph.  That produced mixed font sizes and allowed
            // source glyphs to remain over the translated title.  Keep the
            // first line as the page-title anchor and coalesce every
            // continuation line into one equally-sized title paragraph.
            var continuationLines = pageList
                .Where(para => para != titlePara && IsTitleSubtitleCandidate(para, titlePara))
                .OrderByDescending(para => para.Y1)
                .ThenBy(para => para.X0)
                .ToList();

            foreach (var continuation in continuationLines)
                continuation.IsPageTitle = true;

            if (continuationLines.Count > 1)
            {
                var continuationAnchor = continuationLines[0];
                foreach (var continuation in continuationLines.Skip(1))
                {
                    continuationAnchor.MergeWith(continuation);
                    pageList.Remove(continuation);
                }
            }
        }

        /// <summary>Subtitle merged into title (e.g. "with LLMs") — not part of the author grid.</summary>
        private static bool IsTitleSubtitleCandidate(PdfParagraph para, PdfParagraph titlePara)
        {
            double gap = titlePara.Y0 - para.Y1;
            if (gap < -2 || gap > 25) return false;
            int wordCount = para.TextWithPlaceholders.Trim()
                .Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount < 1 || wordCount > 8) return false;
            double titleCenterX = titlePara.X0 + titlePara.Width / 2;
            double paraCenterX = para.X0 + para.Width / 2;
            return Math.Abs(paraCenterX - titleCenterX) <= titlePara.Width * 0.25 &&
                   para.Width <= titlePara.Width * 0.5;
        }
    }
}

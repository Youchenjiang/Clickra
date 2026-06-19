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
                .OrderByDescending(para => para.Y1)
                .ThenByDescending(para => para.AverageFontSize)
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

            PdfParagraph? subtitlePara = null;
            foreach (var para in pageList)
            {
                if (para == titlePara) continue;
                if (!IsTitleSubtitleCandidate(para, titlePara)) continue;
                if (subtitlePara == null || para.Y1 > subtitlePara.Y1)
                    subtitlePara = para;
            }

            if (subtitlePara != null)
            {
                titlePara.MergeWith(subtitlePara);
                pageList.Remove(subtitlePara);
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

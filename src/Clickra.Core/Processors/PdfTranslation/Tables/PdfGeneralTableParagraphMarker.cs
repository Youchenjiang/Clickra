using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfGeneralTableParagraphMarker
    {
        public static void Mark(
            List<PdfParagraph> pageList,
            double pageWidth,
            double pageHeight,
            bool isTablePage)
        {
            bool hasAuthorBand = PageOneLayoutClassifier.TryGetAuthorBand(
                pageList, pageHeight, out double authorTitleBottom, out double authorAbstractTop, out var authorTitlePara);
            var candidates = CollectCandidates(pageList, pageWidth, isTablePage);

            candidates = KeepCandidatesWithTableNeighbors(candidates, isTablePage);
            if (candidates.Count >= 2)
            {
                MarkGroupedCandidates(
                    pageList,
                    pageWidth,
                    candidates,
                    isTablePage,
                    hasAuthorBand,
                    authorTitleBottom,
                    authorAbstractTop,
                    authorTitlePara);
            }

            // Caption-delimited tables do not require multiple independently
            // segmented cell candidates. Some PDFs merge the entire table body
            // into one or two narrow text blocks, so this pass must always run.
            MarkMergedTableBlocks(pageList, pageWidth, isTablePage);
        }

        private static List<PdfParagraph> CollectCandidates(
            List<PdfParagraph> pageList,
            double pageWidth,
            bool isTablePage)
        {
            var candidates = new List<PdfParagraph>();
            foreach (var para in pageList)
            {
                string txt = para.TextWithPlaceholders.Trim();
                if (ShouldSkipCandidateText(txt, para)) continue;

                string[] allWords = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (allWords.Length > 50) continue;

                if (para.Width < pageWidth * 0.45 && para.Height < 120)
                {
                    int rowAlignedCount = 0;
                    int colAlignedCount = 0;

                    foreach (var other in pageList)
                    {
                        if (other == para) continue;

                        double overlapY = Math.Min(para.Y1, other.Y1) - Math.Max(para.Y0, other.Y0);
                        double minHeight = Math.Min(para.Height, other.Height);
                        if (overlapY > minHeight * 0.1)
                        {
                            rowAlignedCount++;
                        }

                        double overlapX = Math.Min(para.X1, other.X1) - Math.Max(para.X0, other.X0);
                        double minWidth = Math.Min(para.Width, other.Width);
                        if (overlapX > minWidth * 0.5)
                        {
                            colAlignedCount++;
                        }
                    }

                    bool colAlignedOk = (colAlignedCount >= 1) || isTablePage;
                    bool rowStyleTable = isTablePage && colAlignedCount >= 2 && para.Height < 35 && para.Width > 80;
                    if ((rowAlignedCount >= 1 && colAlignedOk) || rowStyleTable)
                    {
                        candidates.Add(para);
                    }
                }
            }

            return candidates;
        }

        private static bool ShouldSkipCandidateText(string txt, PdfParagraph para)
        {
            if (string.IsNullOrEmpty(txt)) return true;
            if (IsFigureOrTableCaptionLike(txt)) return true;
            if (PdfParagraphSemanticClassifier.IsEquationParagraph(para)) return true;

            if (txt.StartsWith("[") ||
                txt.IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0 ||
                txt.IndexOf("doi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                txt.IndexOf("www.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                Regex.IsMatch(txt, @"\b10\.\d{4,}/"))
            {
                return true;
            }

            if (Regex.IsMatch(txt, @"^(?:\d+|[a-zA-Z])\.$") ||
                Regex.IsMatch(txt, @"^\((?:\d+|[a-zA-Z])\)$") ||
                Regex.IsMatch(txt, @"^(?:\d+\.\s*)+$"))
            {
                return true;
            }

            if (Regex.IsMatch(txt, @"^\d+(?:\.\d+)*\.?\s+[A-Z]"))
            {
                return true;
            }

            return txt.Length <= 2 && !Regex.IsMatch(txt, @"^[0-9✓xX-]$");
        }

        private static List<PdfParagraph> KeepCandidatesWithTableNeighbors(
            List<PdfParagraph> candidates,
            bool isTablePage)
        {
            var filteredCandidates = new List<PdfParagraph>();
            foreach (var cand in candidates)
            {
                bool isRowStyle = isTablePage && cand.Height < 35 && cand.Width > 80;
                if (isRowStyle || HasTableNeighbor(cand, candidates))
                {
                    filteredCandidates.Add(cand);
                }
            }

            return filteredCandidates;
        }

        private static bool HasTableNeighbor(PdfParagraph cand, List<PdfParagraph> candidates)
        {
            foreach (var other in candidates)
            {
                if (other == cand) continue;
                double overlapY = Math.Min(cand.Y1, other.Y1) - Math.Max(cand.Y0, other.Y0);
                double minH = Math.Min(cand.Height, other.Height);
                if (overlapY > minH * 0.1)
                {
                    double overlapX = Math.Min(cand.X1, other.X1) - Math.Max(cand.X0, other.X0);
                    if (overlapX <= 0) return true;
                }
            }
            return false;
        }

        private static void MarkGroupedCandidates(
            List<PdfParagraph> pageList,
            double pageWidth,
            List<PdfParagraph> candidates,
            bool isTablePage,
            bool hasAuthorBand,
            double authorTitleBottom,
            double authorAbstractTop,
            PdfParagraph? authorTitlePara)
        {
            foreach (var group in BuildCandidateGroups(candidates, pageWidth))
            {
                if (group.Count < 2) continue;

                bool hasHorizontalPair = HasHorizontalPair(group);
                bool isRowStyleGroup = isTablePage && group.All(p => p.Height < 35 && p.Width > 80);
                if (!hasHorizontalPair && !isRowStyleGroup) continue;

                foreach (var member in group)
                {
                    member.IsTable = true;
                }

                MarkParagraphsInsideGroupBounds(
                    pageList,
                    group,
                    hasAuthorBand,
                    authorTitleBottom,
                    authorAbstractTop,
                    authorTitlePara);
            }
        }

        private static List<List<PdfParagraph>> BuildCandidateGroups(List<PdfParagraph> candidates, double pageWidth)
        {
            var groups = new List<List<PdfParagraph>>();
            foreach (var cand in candidates)
            {
                bool added = false;
                foreach (var group in groups)
                {
                    bool close = false;
                    foreach (var member in group)
                    {
                        double center = pageWidth / 2;
                        bool candIsLeft = cand.X1 <= center + 5;
                        bool memberIsLeft = member.X1 <= center + 5;
                        if (candIsLeft != memberIsLeft) continue;
                        double verticalDist = 0;
                        if (cand.Y1 < member.Y0)
                        {
                            verticalDist = member.Y0 - cand.Y1;
                        }
                        else if (member.Y1 < cand.Y0)
                        {
                            verticalDist = cand.Y0 - member.Y1;
                        }

                        if (verticalDist < 45)
                        {
                            close = true;
                            break;
                        }
                    }
                    if (close)
                    {
                        group.Add(cand);
                        added = true;
                        break;
                    }
                }
                if (!added)
                {
                    groups.Add(new List<PdfParagraph> { cand });
                }
            }

            return groups;
        }

        private static bool HasHorizontalPair(List<PdfParagraph> group)
        {
            for (int i = 0; i < group.Count; i++)
            {
                for (int j = i + 1; j < group.Count; j++)
                {
                    var p1 = group[i];
                    var p2 = group[j];
                    double overlapY = Math.Min(p1.Y1, p2.Y1) - Math.Max(p1.Y0, p2.Y0);
                    double minH = Math.Min(p1.Height, p2.Height);
                    if (overlapY > minH * 0.1)
                    {
                        double overlapX = Math.Min(p1.X1, p2.X1) - Math.Max(p1.X0, p2.X0);
                        if (overlapX <= 0)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static void MarkParagraphsInsideGroupBounds(
            List<PdfParagraph> pageList,
            List<PdfParagraph> group,
            bool hasAuthorBand,
            double authorTitleBottom,
            double authorAbstractTop,
            PdfParagraph? authorTitlePara)
        {
            double minY = group.Min(p => p.Y0) - 15;
            double maxY = group.Max(p => p.Y1) + 15;
            double minX = group.Min(p => p.X0) - 15;
            double maxX = group.Max(p => p.X1) + 15;

            foreach (var para in pageList)
            {
                string txt = para.TextWithPlaceholders.Trim();
                if (IsFigureOrTableCaptionLike(txt)) continue;

                double centerX = para.X0 + para.Width / 2;
                double centerY = para.Y0 + para.Height / 2;

                if (centerX >= minX && centerX <= maxX && centerY >= minY && centerY <= maxY)
                {
                    if (hasAuthorBand && authorTitlePara != null &&
                        PageOneLayoutClassifier.IsInAuthorBand(para, authorTitleBottom, authorAbstractTop, authorTitlePara))
                    {
                        continue;
                    }

                    string[] words = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    if (Regex.IsMatch(txt, @"^[A-Z]\.\s")) continue;
                    if (para.Height > 30 && words.Length > 20) continue;
                    if (words.Length <= 150)
                    {
                        para.IsTable = true;
                    }
                }
            }
        }

        private static void MarkMergedTableBlocks(
            List<PdfParagraph> pageList,
            double pageWidth,
            bool isTablePage)
        {
            if (isTablePage)
            {
                pageList.Where(para => IsDigitDenseTableCandidate(para, pageWidth))
                    .ToList()
                    .ForEach(para => para.IsTable = true);
            }

            MarkTableRegionByCaption(pageList, pageWidth);
        }

        private static bool IsDigitDenseTableCandidate(PdfParagraph para, double pageWidth)
        {
            if (para.IsTable) return false;
            string txt = para.TextWithPlaceholders.Trim();
            if (string.IsNullOrEmpty(txt) || IsFigureOrTableCaptionLike(txt)) return false;

            if (para.Height >= 35 && para.Height < 120 && para.Width > 80 && para.Width < pageWidth * 0.45)
            {
                int digitGroups = Regex.Matches(txt, @"\b\d+\b").Count;
                int wordCount = txt.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
                return digitGroups >= 4 && wordCount <= 18;
            }
            return false;
        }

        public static void MarkTableRegionByCaption(List<PdfParagraph> pageList, double pageWidth)
        {
            var captions = pageList.Where(para => Regex.IsMatch(
                    para.TextWithPlaceholders.Trim(),
                    @"^(?:TABLE|Table)\s+[IVXLCDM\d]+",
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(1)))
                .ToList();
            if (captions.Count == 0) return;

            foreach (var caption in captions)
            {
                ProcessCaptionTableRegion(caption, pageList, pageWidth);
            }
        }

        private static void ProcessCaptionTableRegion(PdfParagraph caption, List<PdfParagraph> pageList, double pageWidth)
        {
            double captionCenterX = caption.X0 + caption.Width / 2;
            bool captionOnLeft = captionCenterX < pageWidth / 2;
            double prevBottom = caption.Y0;

            foreach (var para in pageList.OrderByDescending(p => p.Y1))
            {
                // The caption itself has the largest Y1, so it sorts first.
                // Skip it instead of breaking: IsCandidateForCaptionRegion
                // returns false for it and would abort the whole region scan
                // before a single table body paragraph is examined.
                if (para == caption) continue;

                // A paragraph spanning nearly the full page width is a title or
                // running header, not a merged table body (merged cells stay
                // inside the column grid). Stop the region scan there so page
                // titles directly below the caption are not misclassified.
                if (para.Width > pageWidth * 0.45) break;

                if (!IsCandidateForCaptionRegion(para, caption, captionOnLeft, pageWidth, prevBottom))
                    break;

                string txt = para.TextWithPlaceholders.Trim();
                if (ShouldStopTableRegionScanning(para, txt, pageWidth))
                    break;

                para.IsTable = true;
                prevBottom = para.Y0;
            }
        }

        private static bool IsCandidateForCaptionRegion(
            PdfParagraph para,
            PdfParagraph caption,
            bool captionOnLeft,
            double pageWidth,
            double prevBottom)
        {
            if (para == caption) return false;

            double paraCenterX = para.X0 + para.Width / 2;
            if ((paraCenterX < pageWidth / 2) != captionOnLeft) return false;
            if (para.Y1 > caption.Y0 + 5) return false;

            double gap = prevBottom - para.Y1;
            return gap <= 30;
        }

        private static bool ShouldStopTableRegionScanning(PdfParagraph para, string txt, double pageWidth)
        {
            if (IsCaptionOrSectionBreak(txt)) return true;
            if (Regex.IsMatch(txt, @"^[IVXLC]+\.\s", RegexOptions.None, TimeSpan.FromSeconds(1))) return true;
            return IsLargeProseParagraph(para, txt, pageWidth);
        }

        private static bool IsCaptionOrSectionBreak(string txt) =>
            txt.StartsWith("Listing", StringComparison.OrdinalIgnoreCase) ||
            txt.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
            txt.StartsWith("Fig", StringComparison.OrdinalIgnoreCase);

        private static bool IsLargeProseParagraph(PdfParagraph para, string txt, double pageWidth) =>
            para.Height > 30 && para.Width > pageWidth * 0.35 &&
            txt.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length > 25;

        private static bool IsFigureOrTableCaptionLike(string txt)
        {
            return txt.StartsWith("Table", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("Fig", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("表", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("圖", StringComparison.OrdinalIgnoreCase);
        }
    }
}

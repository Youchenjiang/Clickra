using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    public static class PdfTableClassifier
    {
        public static void ReclassifyTableMisclassifiedProse(List<PdfParagraph> pageList, double pageWidth)
        {
            PdfParagraph? workDivisionCaption = FindWorkDivisionCaption(pageList);
            PdfParagraph? appendixTableCaption = FindAppendixFeatureTableCaption(pageList);
            foreach (var para in pageList)
            {
                if (!para.IsTable) continue;
                string txt = para.TextWithPlaceholders.Trim();
                if (string.IsNullOrEmpty(txt)) continue;

                if (IsWorkDivisionTableParagraph(para, workDivisionCaption, pageWidth))
                    continue;
                if (IsAppendixFeatureTableParagraph(para, appendixTableCaption, pageWidth))
                    continue;

                if (txt.StartsWith("•") || txt.StartsWith("·") ||
                    txt.StartsWith("To sum up", StringComparison.OrdinalIgnoreCase))
                {
                    para.IsTable = false;
                    continue;
                }

                if (txt.StartsWith("and ", StringComparison.OrdinalIgnoreCase) && para.Height <= 20)
                {
                    para.IsTable = false;
                    continue;
                }

                if (Regex.IsMatch(txt, @"^\d+\s+[A-Za-z]") && para.Height <= 25 && para.Width > 120)
                {
                    para.IsTable = false;
                    continue;
                }

                if (Regex.IsMatch(txt, @"^\d{1,2}(?:\.\d{1,2})?$"))
                {
                    para.IsTable = false;
                    continue;
                }

                int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                if (wordCount >= 2 && para.Height <= 25 && !IsLikelyTableCellValue(txt))
                {
                    if (para.Width > 90 && (wordCount >= 3 || txt.Length > 18))
                    {
                        para.IsTable = false;
                        continue;
                    }

                    if (char.IsLower(txt[0]))
                    {
                        para.IsTable = false;
                    }
                }
            }
        }

        public static void ReclassifyWorkDivisionTableText(List<PdfParagraph> pageList, double pageWidth)
        {
            PdfParagraph? caption = FindWorkDivisionCaption(pageList);
            if (caption == null) return;

            foreach (var para in pageList)
            {
                if (para == caption) continue;
                if (!IsWorkDivisionTableParagraph(para, caption, pageWidth)) continue;

                para.IsDiagram = false;
                para.IsTable = true;
            }
        }

        public static void MarkTableParagraphs(
            List<PdfParagraph> pageList, double pageWidth, double pageHeight, bool isTablePage)
        {
            bool hasAuthorBand = PageOneLayoutClassifier.TryGetAuthorBand(
                pageList, pageHeight, out double authorTitleBottom, out double authorAbstractTop, out var authorTitlePara);
            var candidates = new List<PdfParagraph>();
            foreach (var para in pageList)
            {
                string txt = para.TextWithPlaceholders.Trim();
                if (string.IsNullOrEmpty(txt)) continue;

                if (txt.StartsWith("Table", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("Fig", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("表", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("圖", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (PdfParagraphSemanticClassifier.IsEquationParagraph(para)) continue;

                // Exclude citations, references, and links from becoming table candidates
                if (txt.StartsWith("[") ||
                    txt.IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    txt.IndexOf("doi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    txt.IndexOf("www.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    Regex.IsMatch(txt, @"\b10\.\d{4,}/"))
                {
                    continue;
                }

                // Exclude list labels (e.g. "1.", "2.", "a.", "(1)")
                if (Regex.IsMatch(txt, @"^(?:\d+|[a-zA-Z])\.$") ||
                    Regex.IsMatch(txt, @"^\((?:\d+|[a-zA-Z])\)$") ||
                    Regex.IsMatch(txt, @"^(?:\d+\.\s*)+$"))
                {
                    continue;
                }

                // Exclude section numbering headings (e.g. "3.2", "3.2.1", "10. WORK DIVISION")
                if (Regex.IsMatch(txt, @"^\d+(?:\.\d+)*\.?\s+[A-Z]"))
                {
                    continue;
                }

                // Exclude single character / punctuation-only paragraphs
                if (txt.Length <= 2 && !Regex.IsMatch(txt, @"^[0-9✓xX-]$"))
                {
                    continue;
                }

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
                    // Row-style tables (one PDF paragraph per row) align vertically, not side-by-side.
                    bool rowStyleTable = isTablePage && colAlignedCount >= 2 && para.Height < 35 && para.Width > 80;
                    if ((rowAlignedCount >= 1 && colAlignedOk) || rowStyleTable)
                    {
                        candidates.Add(para);
                    }
                }
            }

            // Filter candidates to keep only those that have a horizontal neighbor,
            // or vertically stacked full-width rows on table pages (e.g. TABLE V).
            var filteredCandidates = new List<PdfParagraph>();
            foreach (var cand in candidates)
            {
                bool isRowStyle = isTablePage && cand.Height < 35 && cand.Width > 80;
                bool hasNeighbor = false;
                foreach (var other in candidates)
                {
                    if (other == cand) continue;
                    double overlapY = Math.Min(cand.Y1, other.Y1) - Math.Max(cand.Y0, other.Y0);
                    double minH = Math.Min(cand.Height, other.Height);
                    if (overlapY > minH * 0.1)
                    {
                        double overlapX = Math.Min(cand.X1, other.X1) - Math.Max(cand.X0, other.X0);
                        if (overlapX <= 0)
                        {
                            hasNeighbor = true;
                            break;
                        }
                    }
                }
                if (hasNeighbor || isRowStyle)
                {
                    filteredCandidates.Add(cand);
                }
            }
            candidates = filteredCandidates;

            if (candidates.Count < 2) return;

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
                        else
                        {
                            verticalDist = 0;
                        }

                        // Tightened threshold from 80 to 45 to prevent multi-column chaining
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

            foreach (var group in groups)
            {
                if (group.Count < 2) continue;

                // Enforce that a table group must have at least one pair of horizontally adjacent cells
                bool hasHorizontalPair = false;
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
                            if (overlapX <= 0) // No horizontal overlap means they are side-by-side
                            {
                                hasHorizontalPair = true;
                                break;
                            }
                        }
                    }
                    if (hasHorizontalPair) break;
                }
                bool isRowStyleGroup = isTablePage && group.All(p => p.Height < 35 && p.Width > 80);
                if (!hasHorizontalPair && !isRowStyleGroup) continue;

                foreach (var member in group)
                {
                    member.IsTable = true;
                }

                double minY = group.Min(p => p.Y0);
                double maxY = group.Max(p => p.Y1);
                double minX = group.Min(p => p.X0);
                double maxX = group.Max(p => p.X1);

                minY -= 15;
                maxY += 15;
                minX -= 15;
                maxX += 15;

                foreach (var para in pageList)
                {
                    string txt = para.TextWithPlaceholders.Trim();
                    if (txt.StartsWith("Table", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("Fig", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("表", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("圖", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

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
                        // Skip multi-line body paragraphs that happen to fall inside expanded table bbox
                        if (para.Height > 30 && words.Length > 20) continue;
                        // Increased word count limit to 150 to allow long cell descriptions (like work division) to be bypassed
                        if (words.Length <= 150)
                        {
                            para.IsTable = true;
                        }
                    }
                }
            }

            // Fallback: merged multi-row table blocks (when row splitting still groups rows together).
            if (isTablePage)
            {
                foreach (var para in pageList)
                {
                    if (para.IsTable) continue;
                    string txt = para.TextWithPlaceholders.Trim();
                    if (string.IsNullOrEmpty(txt)) continue;
                    if (txt.StartsWith("Table", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("Fig", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("表", StringComparison.OrdinalIgnoreCase) ||
                        txt.StartsWith("圖", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (para.Height >= 35 && para.Height < 120 && para.Width > 80 && para.Width < pageWidth * 0.45)
                    {
                        int digitGroups = Regex.Matches(txt, @"\b\d+\b").Count;
                        int wordCount = txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                        if (digitGroups >= 4 && wordCount <= 18)
                        {
                            para.IsTable = true;
                        }
                    }
                }

                MarkTableRegionByCaption(pageList, pageWidth);
            }
        }

        public static void ReclassifyAppendixFeatureTableText(List<PdfParagraph> pageList, double pageWidth)
        {
            PdfParagraph? caption = FindAppendixFeatureTableCaption(pageList);
            if (caption == null) return;

            foreach (var para in pageList)
            {
                if (!IsAppendixFeatureTableParagraph(para, caption, pageWidth)) continue;

                para.IsDiagram = false;
                para.IsGrayPromptContent = false;
                para.IsCode = false;
                para.IsTable = true;
                para.IsBypassed = true;
            }
        }

        public static void MarkCompactAcademicTableBodies(
            List<PdfParagraph> pageList,
            double pageWidth,
            Func<PdfParagraph, bool> isFigureTableCaption,
            Func<PdfParagraph, bool> isHeading,
            Func<PdfParagraph, bool> isAppendixSectionHeading)
        {
            var captions = pageList.Where(para =>
                Regex.IsMatch(
                    para.TextWithPlaceholders.Trim(),
                    @"^TABLE\s+[IVXLCDM]+\s*$",
                    RegexOptions.IgnoreCase))
                .ToList();

            foreach (var caption in captions)
            {
                bool leftColumn = caption.X0 + caption.Width / 2 < pageWidth / 2;
                double bodyTop = caption.Y0 - 15;
                double bodyBottom = caption.Y0 - 115;

                foreach (var para in pageList)
                {
                    if (ReferenceEquals(para, caption)) continue;
                    double centerX = para.X0 + para.Width / 2;
                    if ((centerX < pageWidth / 2) != leftColumn) continue;
                    if (para.Y1 > bodyTop || para.Y0 < bodyBottom) continue;
                    if (isFigureTableCaption(para)) continue;
                    if (isHeading(para) || isAppendixSectionHeading(para)) continue;

                    para.IsDiagram = false;
                    para.IsGrayPromptContent = false;
                    para.IsCode = false;
                    para.IsTable = true;
                    para.IsBypassed = true;
                }
            }
        }

        public static void MarkSplitPromptPerformanceTable(
            List<PdfParagraph> pageList,
            Func<PdfParagraph, bool> isFigureTableCaption)
        {
            bool hasCaption = pageList.Any(para =>
                para.TextWithPlaceholders.Trim().Equals(
                    "TABLE I", StringComparison.OrdinalIgnoreCase));
            var codeHeader = pageList.FirstOrDefault(para =>
                para.TextWithPlaceholders.Contains(
                    "Code LLM", StringComparison.OrdinalIgnoreCase));
            var promptHeader = pageList.FirstOrDefault(para =>
                para.TextWithPlaceholders.Contains(
                    "Prompt Details", StringComparison.OrdinalIgnoreCase));
            var bottomAnchors = pageList.Where(para =>
            {
                string text = para.TextWithPlaceholders.Trim();
                return text.StartsWith("Avg:", StringComparison.OrdinalIgnoreCase) ||
                       text.StartsWith("P6:", StringComparison.OrdinalIgnoreCase);
            }).ToList();

            if (!hasCaption || codeHeader == null || promptHeader == null ||
                bottomAnchors.Count == 0)
            {
                return;
            }

            double tableTop = Math.Max(codeHeader.Y1, promptHeader.Y1) + 8.0;
            double tableBottom = bottomAnchors.Min(para => para.Y0) - 5.0;

            foreach (var para in pageList)
            {
                if (para.Y1 > tableTop || para.Y0 < tableBottom) continue;
                if (isFigureTableCaption(para)) continue;

                para.IsDiagram = false;
                para.IsGrayPromptContent = false;
                para.IsCode = false;
                para.IsTable = true;
                para.IsBypassed = true;
            }
        }

        private static bool IsLikelyTableCellValue(string txt)
        {
            return txt.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("Manual", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("Large", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("Small", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("No", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase);
        }

        private static void MarkTableRegionByCaption(List<PdfParagraph> pageList, double pageWidth)
        {
            PdfParagraph? caption = null;
            foreach (var para in pageList)
            {
                string txt = para.TextWithPlaceholders.Trim();
                if (Regex.IsMatch(
                        txt, @"^(?:TABLE|Table)\s+[IVXLCDM\d]+", RegexOptions.IgnoreCase))
                {
                    caption = para;
                    break;
                }
            }
            if (caption == null) return;

            double captionCenterX = caption.X0 + caption.Width / 2;
            bool captionOnLeft = captionCenterX < pageWidth / 2;
            double prevBottom = caption.Y0;

            foreach (var para in pageList.OrderByDescending(p => p.Y1))
            {
                if (para == caption) continue;

                double paraCenterX = para.X0 + para.Width / 2;
                if ((paraCenterX < pageWidth / 2) != captionOnLeft) continue;
                if (para.Y1 > caption.Y0 + 5) continue;

                double gap = prevBottom - para.Y1;
                if (gap > 28) break;

                string txt = para.TextWithPlaceholders.Trim();
                if (txt.StartsWith("Listing", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("Figure", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("Fig", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (Regex.IsMatch(txt, @"^[IVXLC]+\.\s"))
                {
                    break;
                }

                if (para.Height > 30 && para.Width > pageWidth * 0.35 &&
                    txt.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length > 25)
                {
                    break;
                }

                para.IsTable = true;
                prevBottom = para.Y0;
            }
        }

        private static PdfParagraph? FindWorkDivisionCaption(List<PdfParagraph> pageList)
        {
            foreach (var para in pageList)
            {
                string txt = para.TextWithPlaceholders.Trim();
                if (txt.StartsWith("10. WORK DIVISION", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("WORK DIVISION", StringComparison.OrdinalIgnoreCase))
                {
                    return para;
                }
            }
            return null;
        }

        private static bool IsWorkDivisionTableParagraph(PdfParagraph para, PdfParagraph? caption, double pageWidth)
        {
            if (caption == null || para == caption) return false;
            if (para.Y1 > caption.Y0 + 5) return false;
            if (para.Y1 < 100) return false;

            double tableLeft = caption.X0 - 15;
            double tableRight = Math.Min(pageWidth - 20, caption.X1 + 230);
            double centerX = para.X0 + para.Width / 2;
            return centerX >= tableLeft && centerX <= tableRight;
        }

        private static PdfParagraph? FindAppendixFeatureTableCaption(List<PdfParagraph> pageList)
        {
            return pageList.FirstOrDefault(para =>
                Regex.IsMatch(
                    para.TextWithPlaceholders.Trim(),
                    @"^Table\s+(?:18|19)\b",
                    RegexOptions.IgnoreCase));
        }

        private static bool IsAppendixFeatureTableParagraph(
            PdfParagraph para,
            PdfParagraph? caption,
            double pageWidth)
        {
            if (caption == null || para == caption) return false;
            if (para.Y1 > caption.Y0 + 5 || para.Y1 < 100) return false;

            double centerX = para.X0 + para.Width / 2;
            return centerX >= 45 && centerX <= pageWidth - 30;
        }
    }
}

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

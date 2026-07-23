using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfTableMisclassifiedProseCleanup
    {
        public static bool IsTallFullColumnProse(
            PdfParagraph paragraph,
            int wordCount,
            double pageWidth) =>
            paragraph.Height > 35 &&
            wordCount > 20 &&
            paragraph.Width > pageWidth * 0.35;

        public static void Reclassify(List<PdfParagraph> pageList, double pageWidth)
        {
            PdfParagraph? workDivisionCaption = PdfSpecialTableRegionClassifier.FindWorkDivisionCaption(pageList);
            PdfParagraph? appendixTableCaption = PdfSpecialTableRegionClassifier.FindAppendixFeatureTableCaption(pageList);
            foreach (var para in pageList)
            {
                if (!para.IsTable) continue;
                string txt = para.TextWithPlaceholders.Trim();
                if (string.IsNullOrEmpty(txt)) continue;

                if (ShouldDemoteTableToProse(para, txt, workDivisionCaption, appendixTableCaption, pageWidth))
                {
                    para.IsTable = false;
                }
            }
        }

        private static bool ShouldDemoteTableToProse(
            PdfParagraph para,
            string txt,
            PdfParagraph? workDivisionCaption,
            PdfParagraph? appendixTableCaption,
            double pageWidth)
        {
            if (IsLikelyTableHeader(para, txt)) return false;
            if (PdfSpecialTableRegionClassifier.IsWorkDivisionTableParagraph(para, workDivisionCaption, pageWidth)) return false;
            if (PdfSpecialTableRegionClassifier.IsAppendixFeatureTableParagraph(para, appendixTableCaption, pageWidth)) return false;

            if (txt.StartsWith("•") || txt.StartsWith("·") ||
                txt.StartsWith("To sum up", StringComparison.OrdinalIgnoreCase)) return true;

            if (txt.StartsWith("and ", StringComparison.OrdinalIgnoreCase) && para.Height <= 20) return true;
            if (Regex.IsMatch(txt, @"^\d+\s+[A-Za-z]", RegexOptions.None, TimeSpan.FromSeconds(1)) && para.Height <= 25 && para.Width > 120) return true;
            if (Regex.IsMatch(txt, @"^\d{1,2}(?:\.\d{1,2})?$", RegexOptions.None, TimeSpan.FromSeconds(1))) return true;

            int wordCount = txt.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount >= 2 && para.Height <= 25 && !IsLikelyTableCellValue(txt))
            {
                if (para.Width > 90 && (wordCount >= 3 || txt.Length > 18)) return true;
                if (char.IsLower(txt[0])) return true;
            }

            return IsTallFullColumnProse(para, wordCount, pageWidth);
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

        internal static bool IsLikelyTableHeader(PdfParagraph para, string? text = null)
        {
            if (!para.IsBold || para.Height > 16 || para.AverageFontSize > 8.5) return false;
            string txt = (text ?? para.TextWithPlaceholders).Trim();
            if (string.IsNullOrEmpty(txt)) return false;
            string[] markers =
            {
                "Model Name", "Provider", "Update Date", "Model Size", "License", "Data Type",
                "Dataset", "Classes/Modules", "Methods", "NCLOC"
            };
            return markers.Any(marker => txt.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfTableMisclassifiedProseCleanup
    {
        public static void Reclassify(List<PdfParagraph> pageList, double pageWidth)
        {
            PdfParagraph? workDivisionCaption = PdfSpecialTableRegionClassifier.FindWorkDivisionCaption(pageList);
            PdfParagraph? appendixTableCaption = PdfSpecialTableRegionClassifier.FindAppendixFeatureTableCaption(pageList);
            foreach (var para in pageList)
            {
                if (!para.IsTable) continue;
                string txt = para.TextWithPlaceholders.Trim();
                if (string.IsNullOrEmpty(txt)) continue;

                if (PdfSpecialTableRegionClassifier.IsWorkDivisionTableParagraph(para, workDivisionCaption, pageWidth))
                    continue;
                if (PdfSpecialTableRegionClassifier.IsAppendixFeatureTableParagraph(para, appendixTableCaption, pageWidth))
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

        private static bool IsLikelyTableCellValue(string txt)
        {
            return txt.Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("Manual", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("Large", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("Small", StringComparison.OrdinalIgnoreCase) ||
                   txt.Equals("No", StringComparison.OrdinalIgnoreCase) ||
                   txt.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase);
        }
    }
}

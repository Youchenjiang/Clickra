using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfSpecialTableRegionClassifier
    {
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

        public static PdfParagraph? FindWorkDivisionCaption(List<PdfParagraph> pageList)
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

        public static bool IsWorkDivisionTableParagraph(PdfParagraph para, PdfParagraph? caption, double pageWidth)
        {
            if (caption == null || para == caption) return false;
            if (para.Y1 > caption.Y0 + 5) return false;
            if (para.Y1 < 100) return false;

            double tableLeft = caption.X0 - 15;
            double tableRight = Math.Min(pageWidth - 20, caption.X1 + 230);
            double centerX = para.X0 + para.Width / 2;
            return centerX >= tableLeft && centerX <= tableRight;
        }

        public static PdfParagraph? FindAppendixFeatureTableCaption(List<PdfParagraph> pageList)
        {
            return pageList.FirstOrDefault(para =>
                Regex.IsMatch(
                    para.TextWithPlaceholders.Trim(),
                    @"^Table\s+(?:18|19)\b",
                    RegexOptions.IgnoreCase));
        }

        public static bool IsAppendixFeatureTableParagraph(
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

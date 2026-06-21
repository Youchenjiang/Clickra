using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfCompactAcademicTableMarker
    {
        public static void Mark(
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
    }
}

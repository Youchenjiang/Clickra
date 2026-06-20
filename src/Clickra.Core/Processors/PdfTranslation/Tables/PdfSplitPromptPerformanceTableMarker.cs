using System;
using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfSplitPromptPerformanceTableMarker
    {
        public static void Mark(
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
    }
}

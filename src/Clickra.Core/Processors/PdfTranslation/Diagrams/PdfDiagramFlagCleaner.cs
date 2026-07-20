using System;
using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    internal static class PdfDiagramFlagCleaner
    {
        public static void ReclassifyCalloutFindingsText(List<PdfParagraph> pageList)
        {
            foreach (var para in pageList)
            {
                if (!para.IsDiagram) continue;
                if (PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para))
                {
                    para.IsDiagram = false;
                }
            }
        }

        public static void ClearDiagramFlagOnFigureCaptions(List<PdfParagraph> pageList)
        {
            foreach (var para in pageList)
            {
                if (!PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para)) continue;
                para.IsDiagram = false;
                para.IsTable = false;
                if (!para.IsCode)
                {
                    para.IsBypassed = false;
                }
            }
        }

        public static void ClearDiagramFlagOnSectionHeadings(List<PdfParagraph> pageList)
        {
            foreach (var para in pageList)
            {
                if (!para.IsDiagram) continue;
                if (!PdfParagraphSemanticClassifier.IsHeadingParagraph(para) &&
                    !PdfParagraphSemanticClassifier.IsAppendixSectionHeading(para))
                {
                    continue;
                }

                para.IsDiagram = false;
                if (!para.IsTable && !para.IsCode)
                {
                    para.IsBypassed = false;
                }
            }
        }

        public static void ClearDiagramFlagOnTranslatableProse(
            List<PdfParagraph> pageList,
            IReadOnlyList<TableMaskRegion> diagramRegions)
        {
            foreach (var para in pageList)
            {
                if (!para.IsDiagram) continue;

                // Selectable text embedded in a vector workflow diagram can look
                // prose-like (lowercase words, colons, periods). If its letters
                // materially overlap the detected diagram geometry, keep the
                // original label instead of masking and reflowing it as body text.
                double letterRatio = PdfDiagramRegionGeometry.ParagraphLetterOverlapRatio(para, diagramRegions);
                string text = para.TextWithPlaceholders.Trim();
                int wordCount = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                bool shortFigureLabel = PdfDiagramRegionGeometry.OverlapsAnyRegion(para, diagramRegions) &&
                                        para.Height <= 22 && text.Length <= 80 && wordCount <= 6;
                if (letterRatio >= 0.15 || shortFigureLabel)
                {
                    continue;
                }

                bool shouldClear = false;
                if (PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) ||
                    PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para))
                {
                    shouldClear = true;
                }
                else
                {
                    string txt = text;
                    if (para.Width >= 120 && txt.Any(char.IsLower) &&
                        (txt.IndexOf('.') >= 0 || txt.Contains("{v")))
                    {
                        shouldClear = true;
                    }
                }

                if (!shouldClear) continue;
                para.IsDiagram = false;
                if (!para.IsTable && !para.IsCode)
                {
                    para.IsBypassed = false;
                }
            }
        }

        public static void ClearDiagramFlagOnRunningHeaders(List<PdfParagraph> pageList, double pageHeight)
        {
            foreach (var para in pageList)
            {
                if (!PdfParagraphRoleClassifier.IsRunningHeaderOrFooter(para, pageHeight)) continue;
                para.IsDiagram = false;
                para.IsBypassed = para.IsCode || para.IsOnlyMath ||
                                  string.IsNullOrWhiteSpace(para.TextWithPlaceholders) ||
                                  PdfParagraphSemanticClassifier.IsEquationParagraph(para) ||
                                  PdfTableParagraphClassifier.IsTableParagraph(para) ||
                                  para.IsTable;
            }
        }

        /// <summary>Bar-chart legend/axis labels misclassified as table cells on chart-heavy pages.</summary>
        public static void ReclassifyChartLabelsMisclassifiedAsTable(
            List<PdfParagraph> pageList,
            IReadOnlyList<TableMaskRegion> diagramRegions)
        {
            if (diagramRegions.Count == 0) return;
            foreach (var para in pageList)
            {
                if (!para.IsTable) continue;
                if (PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para)) continue;

                double letterRatio = PdfDiagramRegionGeometry.ParagraphLetterOverlapRatio(para, diagramRegions);
                bool inDiagram = letterRatio >= 0.25 ||
                                 PdfDiagramRegionGeometry.OverlapsAnyRegion(para, diagramRegions);
                if (!inDiagram) continue;

                if (PdfChartLabelClassifier.IsLikelyChartLabel(para) || para.Height <= 60 || letterRatio >= 0.4)
                {
                    para.IsTable = false;
                    para.IsDiagram = true;
                }
            }
        }
    }
}

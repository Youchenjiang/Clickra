using System;
using System.Collections.Generic;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    public static class PdfTableClassifier
    {
        public static void ReclassifyTableMisclassifiedProse(List<PdfParagraph> pageList, double pageWidth)
        {
            PdfTableMisclassifiedProseCleanup.Reclassify(pageList, pageWidth);
        }

        public static void ReclassifyWorkDivisionTableText(List<PdfParagraph> pageList, double pageWidth)
        {
            PdfSpecialTableRegionClassifier.ReclassifyWorkDivisionTableText(pageList, pageWidth);
        }

        public static void MarkTableParagraphs(
            List<PdfParagraph> pageList, double pageWidth, double pageHeight, bool isTablePage)
        {
            PdfGeneralTableParagraphMarker.Mark(pageList, pageWidth, pageHeight, isTablePage);
        }

        public static void MarkCaptionDelimitedTableRegions(
            List<PdfParagraph> pageList,
            double pageWidth)
        {
            PdfGeneralTableParagraphMarker.MarkTableRegionByCaption(pageList, pageWidth);
        }

        public static void ReclassifyAppendixFeatureTableText(List<PdfParagraph> pageList, double pageWidth)
        {
            PdfSpecialTableRegionClassifier.ReclassifyAppendixFeatureTableText(pageList, pageWidth);
        }

        public static void MarkCompactAcademicTableBodies(
            List<PdfParagraph> pageList,
            double pageWidth,
            Func<PdfParagraph, bool> isFigureTableCaption,
            Func<PdfParagraph, bool> isHeading,
            Func<PdfParagraph, bool> isAppendixSectionHeading)
        {
            PdfCompactAcademicTableMarker.Mark(
                pageList,
                pageWidth,
                isFigureTableCaption,
                isHeading,
                isAppendixSectionHeading);
        }

        public static void MarkSplitPromptPerformanceTable(
            List<PdfParagraph> pageList,
            Func<PdfParagraph, bool> isFigureTableCaption)
        {
            PdfSplitPromptPerformanceTableMarker.Mark(pageList, isFigureTableCaption);
        }
    }
}

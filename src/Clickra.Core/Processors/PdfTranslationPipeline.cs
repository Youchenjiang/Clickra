using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Drawing;
#pragma warning disable CA1416 // Validate platform compatibility
using System.Drawing;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    public static class PdfTranslationPipeline
    {
        /// <summary>Structured paragraph flags after the translation layout pipeline for one page.</summary>
        public static TranslationPageDiagnostics AnalyzePageParagraphDiagnostics(string inputPath, int pageNum)
        {
            return PdfTranslationDiagnosticsAnalyzer.AnalyzePageParagraphDiagnostics(
                inputPath,
                pageNum,
                CreateDiagnosticsDependencies());
        }

        /// <summary>Debug helper: dump paragraph flags after full layout pipeline for one page.</summary>
        public static string DumpPageParagraphDiagnostics(string inputPath, int pageNum)
        {
            var diagnostics = AnalyzePageParagraphDiagnostics(inputPath, pageNum);
            return PdfTranslationDiagnosticsAnalyzer.DumpPageParagraphDiagnostics(diagnostics);
        }

        private static PdfTranslationDiagnosticsDependencies CreateDiagnosticsDependencies() => new()
        {
            BuildPageParagraphs = PdfPageParagraphBuilder.BuildPageParagraphs,
            ApplyReferencesSectionBypass = (pages, widths) =>
                PdfReferenceSectionBypasser.Apply(pages, widths, PdfPageReadingOrder.GetPageReadingOrder),
            BuildTableMaskRegions = PdfTableMaskPlanner.BuildTableMaskRegions,
            BuildProcessedDiagramMaskRegions = PdfDiagramMaskBuilder.BuildProcessedDiagramMaskRegions,
            GetEffectiveDiagramMaskRegions = PdfDiagramRegionGeometry.GetEffectiveDiagramMaskRegions,
            GetFigureClipRegions = PdfOverlayMaskPlanner.GetFigureClipRegions,
            GetGrayPromptShadedRegions = PdfGrayPromptRegionBuilder.GetGrayPromptShadedRegions,
            BuildEffectiveGrayMaskRegions = (page, diagrams, paragraphs, pageWidth) =>
                PdfGrayPromptRegionBuilder.BuildEffectiveGrayMaskRegions(
                    page,
                    diagrams,
                    paragraphs,
                    pageWidth,
                    PdfGrayPromptGeometry.ParagraphCenterInsideAnyRegion),
            IsTranslatableBodyProse = PdfParagraphRoleClassifier.IsTranslatableBodyProse,
            IsTranslatableCalloutProse = PdfParagraphRoleClassifier.IsTranslatableCalloutProse,
            IsHeadingParagraph = PdfParagraphSemanticClassifier.IsHeadingParagraph,
            ParagraphOverlapsAnyTableMask = (x0, y0, x1, y1, regions) =>
                PdfTableMaskPlanner.ParagraphOverlapsAnyTableMask(x0, y0, x1, y1, regions),
            ShouldProtectDiagramRegionFromParagraph = PdfOverlayMaskPlanner.ShouldProtectDiagramRegionFromParagraph
        };

        public static void TranslatePdf(string inputPath, string outputPath, string targetLang, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            try { PdfSharp.Fonts.GlobalFontSettings.FontResolver = new ClickraFontResolver(); } catch { }
            onProgress?.Invoke(10, 100, "正在分析 PDF 版面結構與公式...");

            using var pigDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath);
            int totalPages = pigDoc.NumberOfPages;
            var pageParagraphs = new List<List<PdfParagraph>>();

            var pageWidths = new double[totalPages];
            for (int p = 1; p <= totalPages; p++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = pigDoc.GetPage(p);
                pageWidths[p - 1] = page.Width;
                pageParagraphs.Add(PdfPageParagraphBuilder.BuildPageParagraphs(page));
            }

            PdfReferenceSectionBypasser.Apply(pageParagraphs, pageWidths, PdfPageReadingOrder.GetPageReadingOrder);

            onProgress?.Invoke(30, 100, "正在翻譯文本內容...");
            PdfParagraphTranslationStage.TranslatePages(pageParagraphs, inputPath, targetLang, onProgress, cancellationToken);

            PdfTranslatedPdfRebuilder.Rebuild(inputPath, outputPath, targetLang, pigDoc, pageParagraphs, onProgress, cancellationToken);
        }

        public static string PostProcessTranslation(string originalText, string translatedText, string targetLang) =>
            TranslationPostProcessor.PostProcessTranslation(originalText, translatedText, targetLang);
    }
}

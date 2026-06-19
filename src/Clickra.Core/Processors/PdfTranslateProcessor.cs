using System;
using System.Collections.Generic;
using System.Threading;
using Clickra.Core.Models;

namespace Clickra.Core.Processors
{
    public class PdfTranslateProcessor : SingleFileProcessorBase
    {
        protected override string GetOutputSuffix() => "_translated.pdf";

        protected override void ProcessSingleFile(
            string fullPath,
            string targetOutputPath,
            int fileIndex,
            int totalFiles,
            Dictionary<string, object>? options,
            Action<int, int, string>? onProgress,
            CancellationToken cancellationToken)
        {
            string targetLang = "zh-TW";
            if (options != null &&
                options.TryGetValue("targetLang", out var langObj) &&
                langObj is string langStr &&
                !string.IsNullOrWhiteSpace(langStr))
            {
                targetLang = langStr;
            }

            PdfTranslationPipeline.TranslatePdf(fullPath, targetOutputPath, targetLang, onProgress, cancellationToken);
        }

        public static TranslationPageDiagnostics AnalyzePageParagraphDiagnostics(string inputPath, int pageNum) =>
            PdfTranslationPipeline.AnalyzePageParagraphDiagnostics(inputPath, pageNum);

        public static string DumpPageParagraphDiagnostics(string inputPath, int pageNum) =>
            PdfTranslationPipeline.DumpPageParagraphDiagnostics(inputPath, pageNum);

        public static string PostProcessTranslation(string originalText, string translatedText, string targetLang) =>
            PdfTranslationPipeline.PostProcessTranslation(originalText, translatedText, targetLang);

        public static string RemoveDuplicateFormulaLiterals(string text, IReadOnlyList<MathFormula> formulas) =>
            PdfTranslationPipeline.RemoveDuplicateFormulaLiterals(text, formulas);

        public static bool StartsNewParagraphOrSection(string text) =>
            PdfTranslationPipeline.StartsNewParagraphOrSection(text);

        public static bool IsHeadingLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine line) =>
            PdfTranslationPipeline.IsHeadingLine(line);

        public static List<PdfTranslationPipeline.MergedBlock> GetMergedBlocks(
            IEnumerable<UglyToad.PdfPig.DocumentLayoutAnalysis.TextBlock> docstrumBlocks,
            double pageWidth,
            bool isTablePage = false) =>
            PdfTranslationPipeline.GetMergedBlocks(docstrumBlocks, pageWidth, isTablePage);
    }
}

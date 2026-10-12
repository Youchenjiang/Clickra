using System;
using System.Collections.Generic;
using System.Threading;
using Clickra.Core.Modules.PdfTranslation.Model;
using Clickra.Core.Modules.PdfTranslation.Pipeline;
using Clickra.Core.Modules.PdfTranslation.Pipeline.TextProcessing;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.PdfTranslation.Execution
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
            string language = ExecutionLocalizationOptions.ReadLanguage(options);
            string dataDirectory = ProcessorExecutionOptions.ReadDataDirectory(options);

            PdfTranslationPipeline.TranslatePdf(fullPath, targetOutputPath, targetLang, language, dataDirectory, onProgress, cancellationToken);
        }

        public static TranslationPageDiagnostics AnalyzePageParagraphDiagnostics(string inputPath, int pageNum) =>
            PdfTranslationPipeline.AnalyzePageParagraphDiagnostics(inputPath, pageNum);

        public static string PostProcessTranslation(string originalText, string translatedText, string targetLang) =>
            PdfTranslationPipeline.PostProcessTranslation(originalText, translatedText, targetLang);

        public static string RemoveDuplicateFormulaLiterals(string text, IReadOnlyList<MathFormula> formulas) =>
            FormulaLiteralCleaner.RemoveDuplicateFormulaLiterals(text, formulas);

        public static bool StartsNewParagraphOrSection(string text) =>
            PdfParagraphBlockMerger.StartsNewParagraphOrSection(text);

        public static bool IsHeadingLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextLine line) =>
            PdfParagraphBlockMerger.IsHeadingLine(line);

        public static List<PdfParagraphBlockMerger.MergedBlock> GetMergedBlocks(
            IEnumerable<UglyToad.PdfPig.DocumentLayoutAnalysis.TextBlock> docstrumBlocks,
            double pageWidth,
            bool isTablePage = false) =>
            PdfParagraphBlockMerger.GetMergedBlocks(docstrumBlocks, pageWidth, isTablePage);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Clickra.Core.Models;
using PdfSharp.Pdf.IO;

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
            ClickraDebug.Clear();
            string finalOutputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(finalOutputPath) ?? ".");
            string partialOutputPath = finalOutputPath + ".partial";
            string healthPath = Path.Combine(
                Path.GetDirectoryName(finalOutputPath) ?? ".",
                Path.GetFileNameWithoutExtension(finalOutputPath) + "_health.json");
            PdfTranslationStageReport? stageReport = null;
            PdfTranslationLayoutSummary? layoutSummary = null;
            string providerName = string.Empty;
            int sourcePages = 0;

            try { PdfSharp.Fonts.GlobalFontSettings.FontResolver = new ClickraFontResolver(); } catch { }
            try
            {
                using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadlineCts.CancelAfter(TimeSpan.FromMinutes(10));
                CancellationToken operationToken = deadlineCts.Token;

                onProgress?.Invoke(10, 100, "正在分析 PDF 版面結構與公式...");
                using var pigDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath);
                sourcePages = pigDoc.NumberOfPages;
                var pageParagraphs = new List<List<PdfParagraph>>();

                var pageWidths = new double[sourcePages];
                for (int p = 1; p <= sourcePages; p++)
                {
                    operationToken.ThrowIfCancellationRequested();
                    var page = pigDoc.GetPage(p);
                    pageWidths[p - 1] = page.Width;
                    pageParagraphs.Add(PdfPageParagraphBuilder.BuildPageParagraphs(page));
                }

                PdfReferenceSectionBypasser.Apply(pageParagraphs, pageWidths, PdfPageReadingOrder.GetPageReadingOrder);

                onProgress?.Invoke(30, 100, "正在翻譯文本內容...");
                providerName = TranslationEngineFactory.Create().Name;
                stageReport = PdfParagraphTranslationStage.TranslatePages(
                    pageParagraphs, inputPath, targetLang, onProgress, operationToken);

                if (File.Exists(partialOutputPath)) File.Delete(partialOutputPath);
                layoutSummary = PdfTranslatedPdfRebuilder.Rebuild(
                    inputPath,
                    partialOutputPath,
                    targetLang,
                    pigDoc,
                    pageParagraphs,
                    onProgress,
                    operationToken);

                int outputPages = 0;
                using (var rebuiltDoc = PdfReader.Open(partialOutputPath, PdfDocumentOpenMode.Import))
                {
                    outputPages = rebuiltDoc.PageCount;
                }

                var debugLines = ClickraDebug.Lines;
                int renderEntries = debugLines.Count(line => line.Contains(" RENDER ", StringComparison.Ordinal));
                // LogRender uses invariant boolean formatting (True/False). Keep the
                // health report case-insensitive so a real guard clip cannot be
                // reported as zero merely because of casing.
                int guardClipEntries = debugLines.Count(line => line.Contains("guardClip=true", StringComparison.OrdinalIgnoreCase));
                int overflowEntries = debugLines.Count(line => line.Contains("overflow=true", StringComparison.OrdinalIgnoreCase));
                if (outputPages != sourcePages)
                    throw new InvalidOperationException($"PDF page count changed from {sourcePages} to {outputPages}.");
                if (overflowEntries > 0)
                    throw new InvalidOperationException($"PDF layout still has {overflowEntries} overflowing paragraph(s).");
                if (guardClipEntries > 0)
                    throw new InvalidOperationException($"PDF layout still uses {guardClipEntries} guard clip(s); translated text must be reflowed instead of clipped.");
                if (layoutSummary.MinimumBodyFontRatio < PdfTranslationHealthReport.MinimumAllowedBodyFontRatio - 0.01)
                    throw new InvalidOperationException($"PDF body font ratio fell to {layoutSummary.MinimumBodyFontRatio:F3}; the minimum is {PdfTranslationHealthReport.MinimumAllowedBodyFontRatio:F2}.");
                if (layoutSummary.MaximumBodyFontRatio > PdfTranslationHealthReport.MaximumAllowedBodyFontRatio + 0.01)
                    throw new InvalidOperationException($"PDF body font ratio grew to {layoutSummary.MaximumBodyFontRatio:F3}; the maximum is {PdfTranslationHealthReport.MaximumAllowedBodyFontRatio:F2}.");
                if (layoutSummary.MaximumBodyLineSpacingMultiplier > PdfTranslationHealthReport.MaximumAllowedBodyLineSpacingMultiplier + 0.01)
                    throw new InvalidOperationException($"PDF body line spacing grew to {layoutSummary.MaximumBodyLineSpacingMultiplier:F3}; the maximum is {PdfTranslationHealthReport.MaximumAllowedBodyLineSpacingMultiplier:F2}.");
                if (layoutSummary.MaximumFlowRegionResidualWhitespace > PdfTranslationHealthReport.MaximumAllowedFlowRegionResidualWhitespace)
                    throw new InvalidOperationException($"PDF flow region retained {layoutSummary.MaximumFlowRegionResidualWhitespace:F1}pt of undistributed whitespace; the maximum is {PdfTranslationHealthReport.MaximumAllowedFlowRegionResidualWhitespace:F1}pt.");

                var healthReport = new PdfTranslationHealthReport
                {
                    InputPath = Path.GetFullPath(inputPath),
                    OutputPath = finalOutputPath,
                    Provider = stageReport.Provider,
                    SourcePages = sourcePages,
                    OutputPages = outputPages,
                    TranslatedParagraphs = stageReport.TranslatedParagraphs,
                    BypassedParagraphs = stageReport.BypassedParagraphs,
                    RenderEntries = renderEntries,
                    GuardClipEntries = guardClipEntries,
                    OverflowEntries = overflowEntries,
                    HeadingCount = layoutSummary.HeadingCount,
                    MinimumHeadingFontRatio = layoutSummary.MinimumHeadingFontRatio,
                    MaximumAlignmentAnchorShift = layoutSummary.MaximumAlignmentAnchorShift,
                    MinimumBodyFontRatio = layoutSummary.MinimumBodyFontRatio,
                    MaximumBodyFontRatio = layoutSummary.MaximumBodyFontRatio,
                    MaximumBodyLineSpacingMultiplier = layoutSummary.MaximumBodyLineSpacingMultiplier,
                    MaximumInterParagraphGap = layoutSummary.MaximumInterParagraphGap,
                    MaximumFlowRegionResidualWhitespace = layoutSummary.MaximumFlowRegionResidualWhitespace,
                    ShiftedParagraphCount = layoutSummary.ShiftedParagraphCount,
                    FixedRegionCollisionCount = layoutSummary.FixedCollisionCount,
                    BottomOverflowCount = layoutSummary.BottomOverflowCount,
                    TranslationFailures = stageReport.Failures,
                    Succeeded = true
                };
                healthReport.Save(healthPath);
                File.Move(partialOutputPath, finalOutputPath, overwrite: true);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                WriteFailureHealthReport(new FailureHealthReportConfig(
                    healthPath, inputPath, finalOutputPath, sourcePages, stageReport, providerName,
                    "Translation operation exceeded the 10-minute document deadline.", layoutSummary));
                throw new TimeoutException("PDF translation exceeded the 10-minute document deadline.", ex);
            }
            catch (Exception ex)
            {
                // Keep the full stack in the health report so a deterministic
                // layout failure can be fixed from the artifact without asking
                // the user to reproduce it under a debugger.
                var planningFailure = ex as PdfLayoutPlanningException;
                WriteFailureHealthReport(new FailureHealthReportConfig(
                    healthPath, inputPath, finalOutputPath, sourcePages, stageReport, providerName,
                    ex.ToString(), layoutSummary, planningFailure?.FixedCollisionCount, planningFailure?.BottomOverflowCount));
                throw;
            }
            finally
            {
                if (File.Exists(partialOutputPath))
                {
                    try { File.Delete(partialOutputPath); } catch (IOException) { /* Ignore transient cleanup error */ }
                }
            }
        }

        private readonly record struct FailureHealthReportConfig(
            string HealthPath,
            string InputPath,
            string OutputPath,
            int SourcePages,
            PdfTranslationStageReport? StageReport,
            string ProviderName,
            string Error,
            PdfTranslationLayoutSummary? LayoutSummary = null,
            int? FixedCollisionCount = null,
            int? BottomOverflowCount = null);

        private static void WriteFailureHealthReport(FailureHealthReportConfig cfg)
        {
            try
            {
                var debugLines = ClickraDebug.Lines;
                new PdfTranslationHealthReport
                {
                    InputPath = Path.GetFullPath(cfg.InputPath),
                    OutputPath = cfg.OutputPath,
                    Provider = cfg.StageReport?.Provider ?? cfg.ProviderName,
                    SourcePages = cfg.SourcePages,
                    OutputPages = 0,
                    TranslatedParagraphs = cfg.StageReport?.TranslatedParagraphs ?? 0,
                    BypassedParagraphs = cfg.StageReport?.BypassedParagraphs ?? 0,
                    RenderEntries = debugLines.Count(line => line.Contains(" RENDER ", StringComparison.Ordinal)),
                    GuardClipEntries = debugLines.Count(line => line.Contains("guardClip=true", StringComparison.OrdinalIgnoreCase)),
                    OverflowEntries = debugLines.Count(line => line.Contains("overflow=true", StringComparison.OrdinalIgnoreCase)),
                    HeadingCount = cfg.LayoutSummary?.HeadingCount ?? 0,
                    MinimumHeadingFontRatio = cfg.LayoutSummary?.MinimumHeadingFontRatio ?? 1.0,
                    MaximumAlignmentAnchorShift = cfg.LayoutSummary?.MaximumAlignmentAnchorShift ?? 0,
                    MinimumBodyFontRatio = cfg.LayoutSummary?.MinimumBodyFontRatio ?? 1.0,
                    MaximumBodyFontRatio = cfg.LayoutSummary?.MaximumBodyFontRatio ?? 1.0,
                    MaximumBodyLineSpacingMultiplier = cfg.LayoutSummary?.MaximumBodyLineSpacingMultiplier ?? 0,
                    MaximumInterParagraphGap = cfg.LayoutSummary?.MaximumInterParagraphGap ?? 0,
                    MaximumFlowRegionResidualWhitespace = cfg.LayoutSummary?.MaximumFlowRegionResidualWhitespace ?? 0,
                    ShiftedParagraphCount = cfg.LayoutSummary?.ShiftedParagraphCount ?? 0,
                    FixedRegionCollisionCount = cfg.LayoutSummary?.FixedCollisionCount ?? cfg.FixedCollisionCount ?? 0,
                    BottomOverflowCount = cfg.LayoutSummary?.BottomOverflowCount ?? cfg.BottomOverflowCount ?? 0,
                    LayoutFailureReason = cfg.Error,
                    TranslationFailures = new[] { cfg.Error },
                    Succeeded = false
                }.Save(cfg.HealthPath);
            }
            catch (IOException)
            {
                // Ignore non-fatal diagnostic report write errors
            }
        }

        public static string PostProcessTranslation(string originalText, string translatedText, string targetLang) =>
            TranslationPostProcessor.PostProcessTranslation(originalText, translatedText, targetLang);

        // Compatibility surface for the existing figure regression contract. The
        // classifier owns the implementation; keep this private forwarding method
        // so older reflection-based tests continue to exercise the same rule.
        private static bool IsLikelyBarChartAxisLabel(PdfParagraph para) =>
            PdfChartLabelClassifier.IsLikelyBarChartAxisLabel(para);
    }
}

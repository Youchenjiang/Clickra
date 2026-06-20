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
            var translator = TranslationEngineFactory.Create();
            object logLock = new object();

            for (int p = 0; p < totalPages; p++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var paragraphs = pageParagraphs[p];
                if (paragraphs.Count == 0) continue;

                onProgress?.Invoke(30 + (int)(p * 40.0 / totalPages), 100, $"正在翻譯第 {p + 1}/{totalPages} 頁...");

                var paragraphsToTranslate = new List<PdfParagraph>();
                var textsToTranslate = new List<string>();

                foreach (var para in paragraphs)
                {
                    if (para.IsBypassed)
                    {
                        para.TranslatedText = para.TextWithPlaceholders;
                    }
                    else
                    {
                        paragraphsToTranslate.Add(para);
                        textsToTranslate.Add(para.TextWithPlaceholders);
                    }
                }

                if (textsToTranslate.Count > 0)
                {
                    try
                    {
                        var results = PdfTranslationBatchRunner.TranslatePageBatches(
                            translator,
                            textsToTranslate,
                            targetLang,
                            p,
                            totalPages,
                            onProgress,
                            cancellationToken);
                        if (results.Count == paragraphsToTranslate.Count)
                        {
                            for (int i = 0; i < results.Count; i++)
                            {
                                string rawResult = string.IsNullOrWhiteSpace(results[i])
                                    ? paragraphsToTranslate[i].TextWithPlaceholders
                                    : results[i];
                                paragraphsToTranslate[i].TranslatedText = PostProcessor.Process(
                                    paragraphsToTranslate[i].TextWithPlaceholders,
                                    rawResult,
                                    targetLang
                                );
                            }
                        }
                        else
                        {
                            throw new Exception("Mismatched batch translation results count.");
                        }
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            string logPath = Path.Combine(ClickraStorage.GetDataDir(), "translate_errors.log");
                            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [File: {Path.GetFileName(inputPath)}] [Page {p + 1}] Batch translation failed, falling back to sequential. Error: {ex.Message}{Environment.NewLine}";
                            lock (logLock)
                            {
                                File.AppendAllText(logPath, logLine);
                            }
                        }
                        catch { }

                        for (int i = 0; i < paragraphsToTranslate.Count; i++)
                        {
                            var para = paragraphsToTranslate[i];
                            try
                            {
                                onProgress?.Invoke(
                                    PdfTranslationBatchRunner.GetTranslationProgress(p, totalPages, i, paragraphsToTranslate.Count),
                                    100,
                                    $"第 {p + 1}/{totalPages} 頁批次翻譯失敗，正在逐段重試 {i + 1}/{paragraphsToTranslate.Count}...");
                                string result = translator.TranslateAsync(para.TextWithPlaceholders, targetLang, cancellationToken).GetAwaiter().GetResult();
                                string rawResult = string.IsNullOrWhiteSpace(result) ? para.TextWithPlaceholders : result;
                                para.TranslatedText = PostProcessor.Process(
                                    para.TextWithPlaceholders,
                                    rawResult,
                                    targetLang
                                );
                            }
                            catch (Exception exSub)
                            {
                                para.TranslatedText = para.TextWithPlaceholders;
                                try
                                {
                                    string logPath = Path.Combine(ClickraStorage.GetDataDir(), "translate_errors.log");
                                    string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [File: {Path.GetFileName(inputPath)}] [Page {p + 1}] Sequential fallback error: {exSub.Message}{Environment.NewLine}";
                                    lock (logLock)
                                    {
                                        File.AppendAllText(logPath, logLine);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }

            onProgress?.Invoke(80, 100, "正在重建 PDF 佈局與公式...");
            cancellationToken.ThrowIfCancellationRequested();


            using var finalDoc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);

            string targetFontName = "DFKai-SB";
            if (targetLang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
            {
                targetFontName = "DFKai-SB";
            }
            else if (targetLang.Equals("ja", StringComparison.OrdinalIgnoreCase))
            {
                targetFontName = "DFKai-SB";
            }
            else if (targetLang.Equals("ko", StringComparison.OrdinalIgnoreCase))
            {
                targetFontName = "Malgun Gothic";
            }
            else if (targetLang.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                targetFontName = "Arial";
            }

            for (int p = 0; p < totalPages; p++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = finalDoc.Pages[p];

                var paragraphs = pageParagraphs[p];
                if (paragraphs.Count == 0) continue;

                // Map annotations to paragraphs
                try
                {
                    for (int i = 0; i < page.Annotations.Count; i++)
                    {
                        var annot = page.Annotations[i];
                        var rect = annot.Rectangle;

                        var paraOverlaps = new Dictionary<PdfParagraph, List<PdfLetter>>();
                        foreach (var para in paragraphs)
                        {
                            var overlapping = para.AllLetters
                                .Where(l => l.Right >= rect.X1 - 2.5 && l.Left <= rect.X2 + 2.5 &&
                                            l.Top >= rect.Y1 - 2.5 && l.Bottom <= rect.Y2 + 2.5)
                                .OrderBy(l => para.AllLetters.IndexOf(l))
                                .ToList();
                            if (overlapping.Count > 0)
                            {
                                paraOverlaps[para] = overlapping;
                            }
                        }

                        if (paraOverlaps.Count > 0)
                        {
                            double annotCenterX = (rect.X1 + rect.X2) / 2.0;
                            double annotCenterY = (rect.Y1 + rect.Y2) / 2.0;
                            var bestPair = paraOverlaps
                                .OrderByDescending(kv => PdfAnnotationTextMatcher.ScoreAnnotationParagraph(kv.Key, kv.Value, annotCenterX, annotCenterY))
                                .First();
                            var bestPara = bestPair.Key;
                            var overlappingLetters = bestPair.Value;

                            string searchText = string.Join("", overlappingLetters.Select(l => l.Value)).Trim();
                            searchText = PdfAnnotationTextMatcher.NormalizeAnnotationSearchText(searchText);
                            if (!string.IsNullOrEmpty(searchText))
                            {
                                int occurrenceIdx = PdfAnnotationOccurrenceMatcher.GetOccurrenceIndex(bestPara.AllLetters, overlappingLetters, searchText);
                                int firstLetterIdx = bestPara.AllLetters.IndexOf(overlappingLetters[0]);
                                int lastLetterIdx = bestPara.AllLetters.IndexOf(overlappingLetters[^1]);
                                double relCenterX = bestPara.Width > 0 ? (annotCenterX - bestPara.X0) / bestPara.Width : 0.5;
                                double relCenterY = bestPara.Height > 0 ? (annotCenterY - bestPara.Y0) / bestPara.Height : 0.5;
                                double relWidth = bestPara.Width > 0 ? (rect.X2 - rect.X1) / bestPara.Width : 0.05;
                                bestPara.Annotations.Add(new ParagraphAnnotationInfo
                                {
                                    PdfAnnotation = annot,
                                    Text = searchText,
                                    OccurrenceIndex = occurrenceIdx,
                                    FirstLetterIndex = firstLetterIdx,
                                    LastLetterIndex = lastLetterIdx,
                                    TotalLetterCount = bestPara.AllLetters.Count,
                                    RelCenterX = relCenterX,
                                    RelCenterY = relCenterY,
                                    RelWidth = relWidth
                                });
                            }
                        }
                    }
                }
                catch { }

                // Table pages skip white masks over table regions but still strip text streams;
                // bypassed table cells are redrawn in Pass 2 after stripping.
                bool pageHasTable = paragraphs.Any(para => para.IsTable);
                var pigPage = pigDoc.GetPage(p + 1);
                double pageWidthPts = pigPage.Width;
                double pageHeightPts = pigPage.Height;
                var rawDiagramMaskRegions = PdfDiagramMaskBuilder.BuildProcessedDiagramMaskRegions(pigPage, paragraphs);

                Func<PdfParagraph, bool>? excludeAuthorFromTableMask = null;
                if (p == 0 &&
                    PageOneLayoutClassifier.TryGetAuthorBand(paragraphs, pageHeightPts, out double authorTitleBottom, out double authorAbstractTop, out var authorTitlePara) &&
                    authorTitlePara != null)
                {
                    excludeAuthorFromTableMask = para =>
                        PageOneLayoutClassifier.IsInAuthorBand(para, authorTitleBottom, authorAbstractTop, authorTitlePara);
                }
                var tableMaskRegions = (pageHasTable && p != 0)
                    ? PdfTableMaskPlanner.BuildTableMaskRegions(paragraphs.Where(para => para.IsTable).ToList(), pageWidthPts, excludeAuthorFromTableMask)
                    : new List<TableMaskRegion>();
                var diagramMaskRegions = PdfDiagramRegionGeometry.GetEffectiveDiagramMaskRegions(
                    rawDiagramMaskRegions, tableMaskRegions, paragraphs);
                bool workDivisionPage = paragraphs.Any(para =>
                    para.TextWithPlaceholders.Trim().Contains("WORK DIVISION", StringComparison.OrdinalIgnoreCase));
                var effectiveGrayMaskRegions = workDivisionPage
                    ? new List<TableMaskRegion>()
                    : PdfGrayPromptRegionBuilder.BuildEffectiveGrayMaskRegions(
                        pigPage, diagramMaskRegions, paragraphs, pageWidthPts, PdfGrayPromptGeometry.ParagraphCenterInsideAnyRegion);

                HashSet<string> strippedBaseFonts;
                try
                {
                    var translatableFonts = PdfFontStripper.CollectTranslatableFontBaseNames(paragraphs);
                    var mustStripFonts = PdfFontStripper.CollectTranslatableFontBaseNames(paragraphs.Where(para =>
                        !para.IsBypassed && !para.IsGrayPromptContent && !PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(para)));
                    var protectedOnlyFonts = PdfFontStripper.CollectFontsUsedOnlyInProtectedRegions(
                        paragraphs,
                        effectiveGrayMaskRegions,
                        p,
                        pageHeightPts,
                        new ProtectedNoStripPredicates
                        {
                            IsGrayPromptCodeParagraph = PdfGrayPromptClassifier.IsGrayPromptCodeParagraph,
                            ParagraphCenterInsideAnyRegion = PdfGrayPromptGeometry.ParagraphCenterInsideAnyRegion,
                            IsParagraphInsideGrayShadedRegion = PdfGrayPromptGeometry.IsParagraphInsideGrayShadedRegion,
                            IsLikelyChartLabel = PdfChartLabelClassifier.IsLikelyChartLabel
                        });
                    protectedOnlyFonts.ExceptWith(mustStripFonts);
                    translatableFonts.ExceptWith(protectedOnlyFonts);
                    strippedBaseFonts = PdfFontStripper.StripTextFromPage(page, translatableFonts);
                }
                catch
                {
                    strippedBaseFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                // Ensure the page has /ExtGState with /NormalState to reset overprint and multiply blend modes
                try
                {
                    var extGStatesProp = typeof(PdfResources).GetProperty("ExtGStates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (extGStatesProp != null)
                    {
                        var extGStates = extGStatesProp.GetValue(page.Resources) as PdfDictionary;
                        if (extGStates != null && !extGStates.Elements.ContainsKey("/NormalState"))
                        {
                            var normalState = new PdfDictionary();
                            normalState.Elements["/BM"] = new PdfName("/Normal");
                            normalState.Elements["/op"] = new PdfBoolean(false);
                            normalState.Elements["/OP"] = new PdfBoolean(false);
                            extGStates.Elements["/NormalState"] = normalState;
                        }
                    }
                }
                catch { }

                using var gfx = XGraphics.FromPdfPage(page);
                try
                {
                    gfx.Internals.ContentStringBuilder.Append(" /NormalState gs ");
                }
                catch { }

                // Pass 1: Draw white masks ONLY for translated paragraphs
                var pageOneTitlePara = p == 0 ? PageOneLayoutClassifier.FindTitleParagraph(paragraphs, pageHeightPts) : null;
                double pageOneTitleBottom = 0, pageOneAbstractTop = 0;
                bool hasPageOneAuthorBand = p == 0 &&
                    PageOneLayoutClassifier.TryGetAuthorBand(paragraphs, pageHeightPts, out pageOneTitleBottom, out pageOneAbstractTop, out _);

                foreach (var para in paragraphs)
                {
                    if (para.IsBypassed) continue;
                    if (p == 0 && PageOneLayoutClassifier.IsAuthorBlockParagraph(para, paragraphs, pageHeightPts)) continue;
                    if (para.IsGrayPromptContent) continue;
                    if (PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(para)) continue;
                    if ((para.IsGrayPromptContent || PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(para)) &&
                        effectiveGrayMaskRegions.Count > 0 &&
                        PdfOverlayMaskPlanner.ShouldSuppressOverlayForGrayGeometry(para, effectiveGrayMaskRegions))
                    {
                        continue;
                    }
                    if (para.IsTable) continue; // Skip table cells/diagram boxes to avoid erasing lines
                    if (string.IsNullOrWhiteSpace(para.TranslatedText)) continue;

                    if (tableMaskRegions.Count > 0 &&
                    PdfTableMaskPlanner.ParagraphOverlapsAnyTableMask(para.X0, para.Y0, para.X1, para.Y1, tableMaskRegions))
                    {
                        continue;
                    }

                    if (PdfOverlayMaskPlanner.ShouldProtectDiagramRegionFromParagraph(para, diagramMaskRegions, paragraphs, gfx.PageSize.Width) &&
                        !PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) && !PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para) &&
                        !PdfParagraphSemanticClassifier.IsHeadingParagraph(para) && !PdfParagraphSemanticClassifier.IsAppendixSectionHeading(para))
                    {
                        continue;
                    }

                    double pageHeight = gfx.PageSize.Height;
                    PdfMaskGeometry.GetParagraphPaintBounds(para, out double maskX0, out double maskY0, out double maskX1, out double maskY1);

                    double renderedHeight = PdfTranslatedParagraphRenderer.RenderParagraph(gfx, para, targetFontName, measureOnly: true);
                    double bboxHeight = maskY1 - maskY0;

                    const double maskPad = 2.5;
                    // White masks hide original text only; when translation is taller, grow upward
                    // instead of downward so masks from upper paragraphs cannot erase table borders.
                    double maskPdfX0 = maskX0 - maskPad;
                    double maskPdfX1 = maskX1 + maskPad;
                    double maskPdfY0 = maskY0 - maskPad;
                    double maskPdfY1 = maskY1 + maskPad + Math.Max(0.0, renderedHeight - bboxHeight);
                    PdfMaskGeometry.ExpandMaskToColumnWidth(ref maskPdfX0, ref maskPdfX1, para, gfx.PageSize.Width);

                    // Title mask strictly clipped to title paragraph bbox (never into author band).
                    if (pageOneTitlePara != null && para == pageOneTitlePara)
                    {
                        maskPdfY1 = Math.Min(maskPdfY1, pageOneTitlePara.Y1 + maskPad);
                        maskPdfY0 = Math.Max(maskPdfY0, pageOneTitlePara.Y0 - maskPad);
                    }

                    if (tableMaskRegions.Count > 0)
                    {
                        maskPdfY0 = PdfTableMaskPlanner.ClampMaskBottomAboveTables(
                            maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1, tableMaskRegions);
                    }

                    if (diagramMaskRegions.Count > 0)
                    {
                        var clipRegions = PdfOverlayMaskPlanner.GetFigureClipRegions(paragraphs, diagramMaskRegions, gfx.PageSize.Width);
                        maskPdfY1 = PdfOverlayMaskPlanner.ClampMaskTopBelowDiagrams(
                            maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1, clipRegions, gfx.PageSize.Width);
                    }

                    if (effectiveGrayMaskRegions.Count > 0 &&
                        PdfGrayPromptGeometry.MaskRectIntersectsAnyGrayRegion(
                            maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1,
                            effectiveGrayMaskRegions) &&
                        (para.IsGrayPromptContent || PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(para)) &&
                        PdfOverlayMaskPlanner.ShouldSuppressOverlayForGrayGeometry(para, effectiveGrayMaskRegions))
                    {
                        continue;
                    }

                    double maskY1BeforeClamp = maskPdfY1;
                    maskPdfY1 = PdfOverlayMaskPlanner.ClampMaskTopBelowNeighboringParagraphs(
                        maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1, para, paragraphs, gfx.PageSize.Width);

                    // DEBUG: trace mask suppression for paragraphs that have translated text
                    bool dbgTrace = !string.IsNullOrWhiteSpace(para.TranslatedText);
                    if (dbgTrace)
                        ClickraDebug.LogMask(p + 1, para.Y0, para.Y1, maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1,
                            maskY1BeforeClamp, renderedHeight);

                    if (maskPdfY0 >= maskPdfY1 - 0.5) continue;

                    if (hasPageOneAuthorBand &&
                        PdfGrayPromptGeometry.MaskRectOverlapsPageOneAuthorBand(
                            maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1,
                            pageOneTitleBottom, pageOneAbstractTop))
                    {
                        continue;
                    }

                    if (maskPdfY0 >= maskPdfY1 - 0.5) continue;

                    double paragraphX = maskPdfX0;
                    double paragraphY = pageHeight - maskPdfY1;
                    double paragraphWidth = maskPdfX1 - maskPdfX0;
                    double paragraphHeight = maskPdfY1 - maskPdfY0;

                    gfx.DrawRectangle(XBrushes.White, paragraphX, paragraphY, paragraphWidth, paragraphHeight);
                }

                // Pass 2: Render all paragraphs (translated overlays and selectively redrawn bypassed text)
                foreach (var para in paragraphs)
                {
                    if (para.IsBypassed)
                    {
                        if (!PdfFontStripper.ParagraphUsesStrippedFont(para, strippedBaseFonts)) continue;
                        PdfBypassedParagraphRenderer.Render(gfx, para, targetFontName);
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(para.TranslatedText)) continue;

                        if ((para.IsGrayPromptContent || PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(para)) &&
                            effectiveGrayMaskRegions.Count > 0 &&
                            PdfOverlayMaskPlanner.ShouldSuppressOverlayForGrayGeometry(para, effectiveGrayMaskRegions))
                        {
                            continue;
                        }

                        if (p == 0 && PageOneLayoutClassifier.IsAuthorBlockParagraph(para, paragraphs, pageHeightPts))
                        {
                            continue;
                        }

                        if (pageHasTable && tableMaskRegions.Count > 0 &&
                            PdfTableMaskPlanner.ParagraphOverlapsAnyTableMask(para.X0, para.Y0, para.X1, para.Y1, tableMaskRegions))
                        {
                            continue;
                        }

                        if (PdfOverlayMaskPlanner.ShouldProtectDiagramRegionFromParagraph(para, diagramMaskRegions, paragraphs, gfx.PageSize.Width) &&
                            !PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) && !PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para) &&
                            !PdfParagraphSemanticClassifier.IsHeadingParagraph(para) && !PdfParagraphSemanticClassifier.IsAppendixSectionHeading(para))
                        {
                            continue;
                        }

                        double measuredHeight = PdfTranslatedParagraphRenderer.RenderParagraph(gfx, para, targetFontName, measureOnly: true);
                        var clipState = PdfOverlayMaskPlanner.BeginClipRenderAboveDiagramBelow(
                            gfx, para, gfx.PageSize.Height, diagramMaskRegions, paragraphs, measuredHeight, gfx.PageSize.Width);
                        XGraphicsState? authorClipState = null;
                        if (p == 0 && pageOneTitlePara != null && para == pageOneTitlePara && hasPageOneAuthorBand)
                        {
                            authorClipState = gfx.Save();
                            double clipTop = gfx.PageSize.Height - pageOneTitlePara.Y1 - 1.5;
                            double originalClipBottom = gfx.PageSize.Height - pageOneTitlePara.Y0 + 1.5;
                            double clipBottom = PageOneLayoutClassifier.GetTitleClipBottom(
                                clipTop, originalClipBottom, measuredHeight);
                            gfx.IntersectClip(new XRect(
                                0, clipTop, gfx.PageSize.Width, Math.Max(1, clipBottom - clipTop)));
                        }
                        try
                        {
                            ClickraDebug.LogRender(p + 1, para.Y0, para.Y1, para.X0, para.X1,
                                clipState != null, measuredHeight);
                            PdfTranslatedParagraphRenderer.RenderParagraph(gfx, para, targetFontName);
                        }
                        finally
                        {
                            if (authorClipState != null) gfx.Restore(authorClipState);
                            if (clipState != null) gfx.Restore(clipState);
                        }
                    }
                }
            }

            onProgress?.Invoke(95, 100, "正在儲存翻譯後的檔案...");
            finalDoc.Save(outputPath);
            finalDoc.Close();
        }

        public static string PostProcessTranslation(string originalText, string translatedText, string targetLang) =>
            TranslationPostProcessor.PostProcessTranslation(originalText, translatedText, targetLang);
    }
}

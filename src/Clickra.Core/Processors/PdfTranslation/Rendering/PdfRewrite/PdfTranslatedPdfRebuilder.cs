using Clickra.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Processors;

internal static class PdfTranslatedPdfRebuilder
{
    public static PdfTranslationLayoutSummary Rebuild(
        string inputPath,
        string outputPath,
        string targetLang,
        UglyToad.PdfPig.PdfDocument pigDoc,
        IReadOnlyList<List<PdfParagraph>> pageParagraphs,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        int totalPages = pageParagraphs.Count;
        var layoutSummary = new PdfTranslationLayoutSummary();
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
                            if (!searchText.Any(char.IsDigit))
                            {
                                double centerX = (rect.X1 + rect.X2) / 2.0;
                                double centerY = (rect.Y1 + rect.Y2) / 2.0;
                                var nearbyDigit = bestPara.AllLetters
                                    .Where(letter => letter.Value.Length == 1 && char.IsDigit(letter.Value[0]))
                                    .OrderBy(letter =>
                                    {
                                        double dx = ((letter.Left + letter.Right) / 2.0) - centerX;
                                        double dy = ((letter.Bottom + letter.Top) / 2.0) - centerY;
                                        return dx * dx + dy * dy;
                                    })
                                    .FirstOrDefault();
                                if (nearbyDigit != null)
                                {
                                    searchText = nearbyDigit.Value;
                                }
                            }
                            if (!string.IsNullOrEmpty(searchText))
                            {
                                int occurrenceIdx = PdfAnnotationOccurrenceMatcher.GetOccurrenceIndex(bestPara.AllLetters, overlappingLetters, searchText);
                                int firstLetterIdx = bestPara.AllLetters.IndexOf(overlappingLetters[0]);
                                int lastLetterIdx = bestPara.AllLetters.IndexOf(overlappingLetters[^1]);
                                int figureOccurrenceIdx = PdfAnnotationOccurrenceMatcher.GetFigureReferenceIndex(bestPara.AllLetters, firstLetterIdx);
                                double relCenterX = bestPara.Width > 0 ? (annotCenterX - bestPara.X0) / bestPara.Width : 0.5;
                                double relCenterY = bestPara.Height > 0 ? (annotCenterY - bestPara.Y0) / bestPara.Height : 0.5;
                                double relWidth = bestPara.Width > 0 ? (rect.X2 - rect.X1) / bestPara.Width : 0.05;
                                bestPara.Annotations.Add(new ParagraphAnnotationInfo
                                {
                                    PdfAnnotation = annot,
                                    Text = searchText,
                                    OccurrenceIndex = occurrenceIdx,
                                    FigureOccurrenceIndex = figureOccurrenceIdx,
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
                var vectorMarkers = PdfVectorMarkerRenderer.Detect(pigPage, paragraphs);
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
                    if (p == 0)
                    {
                        protectedOnlyFonts.UnionWith(
                            PdfFontStripper.CollectFontsUsedByPageOneAuthorBlock(
                                paragraphs, pageHeightPts));
                    }
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

                // Append masks and translated overlays above the existing page
                // content. The implicit PdfSharp mode can prepend content,
                // leaving a failed-to-strip source line visible on top of the
                // corrected overlay (ASTER page 11 header/acknowledgements).
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                try
                {
                    gfx.Internals.ContentStringBuilder?.Append(" /NormalState gs ");
                }
                catch { }

                // Capture source typography and apply the only supported
                // vertical reflow before masking or drawing.  All later passes
                // consume the effective paragraph coordinates, while
                // OriginalX*/OriginalY* remain the source anchors.
                var layoutPlan = PdfTranslationLayoutPlanner.BuildAndApply(
                    gfx,
                    paragraphs,
                    targetFontName,
                    pageWidthPts,
                    pageHeightPts,
                    tableMaskRegions.Concat(diagramMaskRegions).ToList());
                layoutSummary.HeadingCount += layoutPlan.HeadingCount;
                layoutSummary.ShiftedParagraphCount += layoutPlan.ShiftedParagraphCount;
                layoutSummary.FixedCollisionCount += layoutPlan.FixedCollisionCount;
                layoutSummary.BottomOverflowCount += layoutPlan.BottomOverflowCount;
                layoutSummary.MaximumAlignmentAnchorShift = Math.Max(
                    layoutSummary.MaximumAlignmentAnchorShift,
                    layoutPlan.MaximumAlignmentAnchorShift);
                int previousBodyCount = layoutSummary.BodyParagraphCount;
                int pageBodyCount = layoutPlan.Snapshots.Count(s =>
                    s.Role == PdfParagraphSemanticRole.Body &&
                    !string.IsNullOrWhiteSpace(s.Paragraph.TranslatedText) &&
                    s.Paragraph.TranslatedText.Any(FontUtilities.IsCjkCharacter));
                if (pageBodyCount > 0)
                {
                    layoutSummary.MinimumBodyFontRatio = previousBodyCount == 0
                        ? layoutPlan.MinimumBodyFontRatio
                        : Math.Min(layoutSummary.MinimumBodyFontRatio, layoutPlan.MinimumBodyFontRatio);
                    layoutSummary.MaximumBodyFontRatio = Math.Max(
                        layoutSummary.MaximumBodyFontRatio,
                        layoutPlan.MaximumBodyFontRatio);
                    layoutSummary.BodyParagraphCount += pageBodyCount;
                }
                layoutSummary.MaximumBodyLineSpacingMultiplier = Math.Max(
                    layoutSummary.MaximumBodyLineSpacingMultiplier,
                    layoutPlan.MaximumBodyLineSpacingMultiplier);
                layoutSummary.MaximumInterParagraphGap = Math.Max(
                    layoutSummary.MaximumInterParagraphGap,
                    layoutPlan.MaximumInterParagraphGap);
                layoutSummary.MaximumFlowRegionResidualWhitespace = Math.Max(
                    layoutSummary.MaximumFlowRegionResidualWhitespace,
                    layoutPlan.MaximumFlowRegionResidualWhitespace);
                if (layoutPlan.Snapshots.Count > 0)
                {
                    layoutSummary.MinimumHeadingFontRatio = Math.Min(
                        layoutSummary.MinimumHeadingFontRatio,
                        layoutPlan.Snapshots
                            .Where(s => s.Role is PdfParagraphSemanticRole.PageTitle or PdfParagraphSemanticRole.AbstractHeading or PdfParagraphSemanticRole.SectionHeading or PdfParagraphSemanticRole.SubsectionHeading)
                            .Select(s => s.FontRatio)
                            .DefaultIfEmpty(1.0)
                            .Min());
                }

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
                    bool isFigureCaption = PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para);
                    bool isFindingCallout = PdfParagraphRoleClassifier.IsFindingCallout(para);
                    PdfMaskGeometry.GetParagraphPaintBounds(para, out double maskX0, out double maskY0, out double maskX1, out double maskY1);
                    // Caption marker formulas can carry a bogus absolute Y
                    // extent from PdfPig (their relative glyph offset is not a
                    // page coordinate). For captions, the source paragraph
                    // bbox is the authoritative paint region; otherwise the
                    // union can invert the mask and leave the original circles
                    // visible over the translated caption.
                    if (isFigureCaption)
                    {
                        maskX0 = Math.Min(para.OriginalX0, para.X0);
                        maskY0 = Math.Min(para.OriginalY0, para.Y0);
                        maskX1 = Math.Max(para.OriginalX1, para.X1);
                        maskY1 = Math.Max(para.OriginalY1, para.Y1);
                    }

                    if (isFindingCallout)
                    {
                        // Replace the source callout surface as one unit. A
                        // normal white text mask would erase the light-blue
                        // fill and rounded border before the translated text
                        // is drawn in Pass 2.
                        DrawFindingCalloutSurface(gfx, para, pageHeight);
                        continue;
                    }

                    double renderedHeight = PdfTranslatedParagraphRenderer.RenderParagraph(gfx, para, targetFontName, measureOnly: true);
                    double bboxHeight = maskY1 - maskY0;

                    const double maskPad = 1.5;
                    // White masks hide original text only; when translation is taller, grow upward
                    // instead of downward so masks from upper paragraphs cannot erase table borders.
                    double maskPdfX0 = maskX0 - maskPad;
                    double maskPdfX1 = maskX1 + maskPad;
                    double maskPdfY0 = 0.0;
                    double maskPdfY1 = 0.0;
                    if (isFigureCaption)
                    {
                        // Captions must erase the source caption, but must not
                        // grow into the diagram above. Their source bbox is the
                        // authoritative paint region; translated height is not
                        // allowed to enlarge the caption mask vertically.
                        const double captionMaskPad = 1.0;
                        // Keep the normal lower padding so descenders from a
                        // wrapped source caption are fully erased.
                        maskPdfY0 = maskY0 - maskPad;
                        maskPdfY1 = maskY1 + captionMaskPad;
                    }
                    else
                    {
                        maskPdfY0 = maskY0 - maskPad;
                        maskPdfY1 = maskY1 + maskPad + Math.Max(0.0, renderedHeight - bboxHeight);
                    }
                    PdfMaskGeometry.ExpandMaskToColumnWidth(ref maskPdfX0, ref maskPdfX1, para, gfx.PageSize.Width);

                    // Title mask strictly clipped to title paragraph bbox (never into author band).
                    if (pageOneTitlePara != null && para == pageOneTitlePara)
                    {
                        maskPdfY1 = Math.Min(maskPdfY1, pageOneTitlePara.Y1 + maskPad);
                        maskPdfY0 = Math.Max(maskPdfY0, pageOneTitlePara.Y0 - maskPad);
                    }

                    if (tableMaskRegions.Count > 0 && !isFigureCaption)
                    {
                        maskPdfY0 = PdfTableMaskPlanner.ClampMaskBottomAboveTables(
                            maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1, tableMaskRegions);
                    }

                    // Figure captions use their source-height mask above. Other
                    // translated paragraphs may grow upward, so clamp those
                    // masks below the diagram region.
                    if (diagramMaskRegions.Count > 0 && !isFigureCaption)
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

                    bool isPageOneHeading = p == 0 &&
                        (para == pageOneTitlePara || PdfParagraphSemanticClassifier.IsHeadingParagraph(para));
                    if (hasPageOneAuthorBand && !isPageOneHeading &&
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

                if (vectorMarkers.Count > 0)
                    PdfVectorMarkerRenderer.EraseSource(gfx, vectorMarkers);

                // Pass 2: Render all paragraphs (translated overlays and selectively redrawn bypassed text)
                var renderedCharsByParagraph = new Dictionary<PdfParagraph, List<RenderedChar>>();
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

                        // Page-one title paragraphs are rendered once by the
                        // final source scrub below. Skipping them here avoids
                        // duplicate translated title text in the PDF content
                        // stream while retaining the normal two-pass flow for
                        // every other paragraph.
                        if (p == 0 &&
                            (para.IsPageTitle || para.SemanticRole == PdfParagraphSemanticRole.PageTitle))
                            continue;

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
                            ClickraDebug.LogRenderSkip(
                                p + 1, "table-overlap", para.Y0, para.Y1,
                                para.OriginalY0, para.OriginalY1, para.TextWithPlaceholders);
                            continue;
                        }

                        if (PdfOverlayMaskPlanner.ShouldProtectDiagramRegionFromParagraph(para, diagramMaskRegions, paragraphs, gfx.PageSize.Width) &&
                            !PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) && !PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para) &&
                            !PdfParagraphSemanticClassifier.IsHeadingParagraph(para) && !PdfParagraphSemanticClassifier.IsAppendixSectionHeading(para))
                        {
                            ClickraDebug.LogRenderSkip(
                                p + 1, "diagram-overlap", para.Y0, para.Y1,
                                para.OriginalY0, para.OriginalY1, para.TextWithPlaceholders);
                            continue;
                        }

                        PdfParagraphRenderMetrics renderMetrics = default;
                        double measuredHeight = PdfTranslatedParagraphRenderer.RenderParagraph(
                            gfx,
                            para,
                            targetFontName,
                            measureOnly: true,
                            metricsSink: metrics => renderMetrics = metrics);
                        // Do not hide translated body text with a broad figure/column clip.
                        // The renderer already reflows to the paragraph bounds; the old
                        // guard clip made the output appear successful while silently
                        // deleting the lower lines of translated paragraphs. White masks
                        // remain bounded by diagram geometry in Pass 1.
                        // Outer clipping is intentionally disabled. Layout
                        // planning must resolve collisions before rendering;
                        // an IntersectClip here would hide missing lines while
                        // still producing a seemingly successful PDF.
                        XGraphicsState? clipState = null;
                        try
                        {
                            ClickraDebug.LogRender(
                                p + 1,
                                para.Y0,
                                para.Y1,
                                para.X0,
                                para.X1,
                                guardClip: clipState != null,
                                horizontalOverflow: renderMetrics.HorizontalOverflow,
                                verticalOverflow: renderMetrics.VerticalOverflow,
                                measuredH: measuredHeight,
                                text: para.TextWithPlaceholders);
                            PdfTranslatedParagraphRenderer.RenderParagraph(
                                gfx,
                                para,
                                targetFontName,
                                renderedCharsSink: chars => renderedCharsByParagraph[para] = chars.ToList());
                        }
                        finally
                        {
                            if (clipState != null) gfx.Restore(clipState);
                        }
                    }
                }

                // Restore inline numbered vector markers after all paragraph
                // masks and translated text have been painted. These markers
                // are source paths, not text, so the normal font stripper and
                // paragraph renderer cannot preserve them by themselves.
                if (vectorMarkers.Count > 0)
                    PdfVectorMarkerRenderer.Render(gfx, vectorMarkers, renderedCharsByParagraph);

                // Page-one title fonts are often shared with the protected
                // author block, so font stripping cannot safely remove the
                // source title glyphs from the original content stream.  Do a
                // final title-only scrub after all overlays have been drawn,
                // then redraw the translated title once.  This guarantees the
                // source wrapped title cannot remain visible over the CJK
                // translation while keeping the author block untouched.
                if (p == 0)
                    RedrawPageOneTitleAfterSourceScrub(gfx, paragraphs, targetFontName);
            }

            onProgress?.Invoke(95, 100, "正在儲存翻譯後的檔案...");
            finalDoc.Save(outputPath);
            finalDoc.Close();
            return layoutSummary;
    }

    private static void DrawFindingCalloutSurface(XGraphics gfx, PdfParagraph para, double pageHeight)
    {
        const double padding = 3.2;
        const double radius = 4.0;
        double x0 = Math.Min(para.OriginalX0, para.X0) - padding;
        // PdfPig's text bbox leaves a larger descender gap on multi-line
        // callouts (ASTER Finding 5) than on one-line boxes (Finding 6).
        // Scale the lower inset with the source height so the source border is
        // fully covered without making short boxes taller than their original.
        double bottomPadding = Math.Max(padding, para.Height * 0.10);
        double y0 = Math.Min(para.OriginalY0, para.Y0) - bottomPadding;
        double x1 = Math.Max(para.OriginalX1, para.X1) + padding;
        double y1 = Math.Max(para.OriginalY1, para.Y1) + padding;

        var fill = new XSolidBrush(XColor.FromArgb(229, 247, 253));
        var border = new XPen(XColors.Black, 0.75);
        gfx.DrawRoundedRectangle(
            border,
            fill,
            x0,
            pageHeight - y1,
            Math.Max(1, x1 - x0),
            Math.Max(1, y1 - y0),
            radius,
            radius);
    }

    private static void RedrawPageOneTitleAfterSourceScrub(
        XGraphics gfx,
        IReadOnlyList<PdfParagraph> paragraphs,
        string targetFontName)
    {
        var titleParagraphs = paragraphs
            .Where(para =>
                (para.IsPageTitle || para.SemanticRole == PdfParagraphSemanticRole.PageTitle) &&
                !string.IsNullOrWhiteSpace(para.TranslatedText))
            .ToList();
        if (titleParagraphs.Count == 0) return;

        const double padding = 2.5;
        double pageHeight = gfx.PageSize.Height;
        foreach (var para in titleParagraphs)
        {
            double x0 = Math.Min(para.OriginalX0, para.X0) - padding;
            double y0 = Math.Min(para.OriginalY0, para.Y0) - padding;
            double x1 = Math.Max(para.OriginalX1, para.X1) + padding;
            double y1 = Math.Max(para.OriginalY1, para.Y1) + padding;
            if (x1 <= x0 || y1 <= y0) continue;

            gfx.DrawRectangle(
                XBrushes.White,
                x0,
                pageHeight - y1,
                x1 - x0,
                y1 - y0);
        }

        foreach (var para in titleParagraphs)
            PdfTranslatedParagraphRenderer.RenderParagraph(gfx, para, targetFontName);
    }
}

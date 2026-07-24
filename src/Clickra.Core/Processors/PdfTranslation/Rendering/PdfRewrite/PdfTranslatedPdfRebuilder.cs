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
        string targetFontName = GetTargetFontName(targetLang);

        for (int p = 0; p < totalPages; p++)
        {
            RebuildPage(
                finalDoc.Pages[p],
                pigDoc.GetPage(p + 1),
                p,
                pageParagraphs[p],
                targetFontName,
                layoutSummary,
                cancellationToken);
        }

        onProgress?.Invoke(95, 100, "正在儲存翻譯後的檔案...");
        finalDoc.Save(outputPath);
        finalDoc.Close();
        return layoutSummary;
    }

    private static void RebuildPage(
        PdfPage page,
        UglyToad.PdfPig.Content.Page pigPage,
        int pageIndex,
        List<PdfParagraph> paragraphs,
        string targetFontName,
        PdfTranslationLayoutSummary layoutSummary,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (paragraphs.Count == 0) return;

        MapAnnotationsToParagraphs(page, paragraphs);

        bool pageHasTable = paragraphs.Any(para => para.IsTable);
        double pageWidthPts = pigPage.Width;
        double pageHeightPts = pigPage.Height;
        var vectorMarkers = PdfVectorMarkerRenderer.Detect(pigPage, paragraphs);

        var (tableMaskRegions, diagramMaskRegions, effectiveGrayMaskRegions) = BuildPageMaskRegions(
            pigPage, pageIndex, paragraphs, pageHasTable);

        var strippedBaseFonts = StripPageFonts(page, paragraphs, effectiveGrayMaskRegions, pageIndex, pageHeightPts);

        EnsurePageNormalExtGState(page);

        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        try
        {
            gfx.Internals.ContentStringBuilder?.Append(" /NormalState gs ");
        }
        catch { }

        ApplyLayoutPlanning(gfx, paragraphs, targetFontName, pageWidthPts, pageHeightPts, tableMaskRegions, diagramMaskRegions, layoutSummary);

        var pageOneTitlePara = pageIndex == 0 ? PageOneLayoutClassifier.FindTitleParagraph(paragraphs, pageHeightPts) : null;
        DrawParagraphMasks(new PageMaskDrawOptions(
            gfx, pageIndex, paragraphs, tableMaskRegions, diagramMaskRegions, effectiveGrayMaskRegions, pageOneTitlePara, targetFontName));

        if (vectorMarkers.Count > 0)
            PdfVectorMarkerRenderer.EraseSource(gfx, vectorMarkers);

        var renderedCharsByParagraph = DrawTranslatedOverlays(new OverlayDrawOptions(
            gfx, pageIndex, paragraphs, tableMaskRegions, diagramMaskRegions, effectiveGrayMaskRegions, pageHasTable, strippedBaseFonts, targetFontName));

        if (vectorMarkers.Count > 0)
            PdfVectorMarkerRenderer.Render(gfx, vectorMarkers, renderedCharsByParagraph);

        if (pageIndex == 0)
            RedrawPageOneTitleAfterSourceScrub(gfx, paragraphs, targetFontName);
    }

    private static (List<TableMaskRegion> TableMasks, List<TableMaskRegion> DiagramMasks, List<TableMaskRegion> GrayMasks) BuildPageMaskRegions(
        UglyToad.PdfPig.Content.Page pigPage,
        int pageIndex,
        List<PdfParagraph> paragraphs,
        bool pageHasTable)
    {
        double pageWidthPts = pigPage.Width;
        double pageHeightPts = pigPage.Height;
        var rawDiagramMaskRegions = PdfDiagramMaskBuilder.BuildProcessedDiagramMaskRegions(pigPage, paragraphs);

        Func<PdfParagraph, bool>? excludeAuthorFromTableMask = null;
        if (pageIndex == 0 &&
            PageOneLayoutClassifier.TryGetAuthorBand(paragraphs, pageHeightPts, out double authorTitleBottom, out double authorAbstractTop, out var authorTitlePara) &&
            authorTitlePara != null)
        {
            excludeAuthorFromTableMask = para =>
                PageOneLayoutClassifier.IsInAuthorBand(para, authorTitleBottom, authorAbstractTop, authorTitlePara);
        }

        var tableMaskRegions = (pageHasTable && pageIndex != 0)
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

        return (tableMaskRegions, diagramMaskRegions, effectiveGrayMaskRegions);
    }

    private static HashSet<string> StripPageFonts(
        PdfPage page,
        List<PdfParagraph> paragraphs,
        List<TableMaskRegion> effectiveGrayMaskRegions,
        int pageIndex,
        double pageHeightPts)
    {
        try
        {
            var translatableFonts = PdfFontStripper.CollectTranslatableFontBaseNames(paragraphs);
            var mustStripFonts = PdfFontStripper.CollectTranslatableFontBaseNames(paragraphs.Where(para =>
                !para.IsBypassed && !para.IsGrayPromptContent && !PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(para)));
            var protectedOnlyFonts = PdfFontStripper.CollectFontsUsedOnlyInProtectedRegions(
                paragraphs,
                effectiveGrayMaskRegions,
                pageIndex,
                pageHeightPts,
                new ProtectedNoStripPredicates
                {
                    IsGrayPromptCodeParagraph = PdfGrayPromptClassifier.IsGrayPromptCodeParagraph,
                    ParagraphCenterInsideAnyRegion = PdfGrayPromptGeometry.ParagraphCenterInsideAnyRegion,
                    IsParagraphInsideGrayShadedRegion = PdfGrayPromptGeometry.IsParagraphInsideGrayShadedRegion,
                    IsLikelyChartLabel = PdfChartLabelClassifier.IsLikelyChartLabel
                });
            protectedOnlyFonts.ExceptWith(mustStripFonts);
            if (pageIndex == 0)
            {
                protectedOnlyFonts.UnionWith(
                    PdfFontStripper.CollectFontsUsedByPageOneAuthorBlock(
                        paragraphs, pageHeightPts));
            }
            translatableFonts.ExceptWith(protectedOnlyFonts);
            return PdfFontStripper.StripTextFromPage(page, translatableFonts);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void EnsurePageNormalExtGState(PdfPage page)
    {
        try
        {
            var extGStatesProp = typeof(PdfResources).GetProperty("ExtGStates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (extGStatesProp?.GetValue(page.Resources) is PdfDictionary extGStates && !extGStates.Elements.ContainsKey("/NormalState"))
            {
                var normalState = new PdfDictionary();
                normalState.Elements["/BM"] = new PdfName("/Normal");
                normalState.Elements["/op"] = new PdfBoolean(false);
                normalState.Elements["/OP"] = new PdfBoolean(false);
                extGStates.Elements["/NormalState"] = normalState;
            }
        }
        catch { }
    }

    private static void ApplyLayoutPlanning(
        XGraphics gfx,
        List<PdfParagraph> paragraphs,
        string targetFontName,
        double pageWidthPts,
        double pageHeightPts,
        List<TableMaskRegion> tableMaskRegions,
        List<TableMaskRegion> diagramMaskRegions,
        PdfTranslationLayoutSummary layoutSummary)
    {
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
    }

    private static bool IsProtectedMaskRegion(
        PdfParagraph para,
        List<PdfParagraph> paragraphs,
        List<TableMaskRegion> diagramMaskRegions,
        double pageWidth)
    {
        return PdfOverlayMaskPlanner.ShouldProtectDiagramRegionFromParagraph(para, diagramMaskRegions, paragraphs, pageWidth) &&
            !PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) &&
            !PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para) &&
            !PdfParagraphSemanticClassifier.IsHeadingParagraph(para) &&
            !PdfParagraphSemanticClassifier.IsAppendixSectionHeading(para);
    }

    private static bool IsGrayPromptSuppressed(
        PdfParagraph para,
        List<TableMaskRegion> effectiveGrayMaskRegions)
    {
        return (para.IsGrayPromptContent || PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(para)) &&
            effectiveGrayMaskRegions.Count > 0 &&
            PdfOverlayMaskPlanner.ShouldSuppressOverlayForGrayGeometry(para, effectiveGrayMaskRegions);
    }

    private static bool ShouldSkipMaskForParagraph(
        PdfParagraph para,
        int pageIndex,
        List<PdfParagraph> paragraphs,
        double pageHeightPts,
        List<TableMaskRegion> tableMaskRegions,
        List<TableMaskRegion> diagramMaskRegions,
        List<TableMaskRegion> effectiveGrayMaskRegions,
        double pageWidth)
    {
        if (para.IsBypassed || para.IsTable || string.IsNullOrWhiteSpace(para.TranslatedText)) return true;
        if (pageIndex == 0 && PageOneLayoutClassifier.IsAuthorBlockParagraph(para, paragraphs, pageHeightPts)) return true;
        if (IsGrayPromptSuppressed(para, effectiveGrayMaskRegions)) return true;
        if (tableMaskRegions.Count > 0 && PdfTableMaskPlanner.ParagraphOverlapsAnyTableMask(para.X0, para.Y0, para.X1, para.Y1, tableMaskRegions)) return true;
        if (IsProtectedMaskRegion(para, paragraphs, diagramMaskRegions, pageWidth)) return true;
        return false;
    }

    private static void ComputeBaseMaskY(
        bool isFigureCaption,
        double renderedHeight,
        double maskY0,
        double maskY1,
        out double maskPdfY0,
        out double maskPdfY1)
    {
        const double maskPad = 1.5;
        double bboxHeight = maskY1 - maskY0;
        if (isFigureCaption)
        {
            const double captionMaskPad = 1.0;
            maskPdfY0 = maskY0 - maskPad;
            maskPdfY1 = maskY1 + captionMaskPad;
        }
        else
        {
            maskPdfY0 = maskY0 - maskPad;
            maskPdfY1 = maskY1 + maskPad + Math.Max(0.0, renderedHeight - bboxHeight);
        }
    }

    private static void ApplyRegionClamping(
        PdfParagraph para,
        bool isFigureCaption,
        List<TableMaskRegion> tableMaskRegions,
        List<TableMaskRegion> diagramMaskRegions,
        List<PdfParagraph> paragraphs,
        PdfParagraph? pageOneTitlePara,
        double pageWidth,
        ref double maskPdfX0,
        ref double maskPdfY0,
        ref double maskPdfX1,
        ref double maskPdfY1)
    {
        const double maskPad = 1.5;
        PdfMaskGeometry.ExpandMaskToColumnWidth(ref maskPdfX0, ref maskPdfX1, para, pageWidth);

        if (pageOneTitlePara != null && para == pageOneTitlePara)
        {
            maskPdfY1 = Math.Min(maskPdfY1, pageOneTitlePara.Y1 + maskPad);
            maskPdfY0 = Math.Max(maskPdfY0, pageOneTitlePara.Y0 - maskPad);
        }

        if (tableMaskRegions.Count > 0 && !isFigureCaption)
            maskPdfY0 = PdfTableMaskPlanner.ClampMaskBottomAboveTables(
                maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1, tableMaskRegions);

        if (diagramMaskRegions.Count > 0 && !isFigureCaption)
        {
            var clipRegions = PdfOverlayMaskPlanner.GetFigureClipRegions(paragraphs, diagramMaskRegions, pageWidth);
            maskPdfY1 = PdfOverlayMaskPlanner.ClampMaskTopBelowDiagrams(
                maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1, clipRegions, pageWidth);
        }
    }

    private static void ComputeMaskBounds(
        PdfParagraph para,
        bool isFigureCaption,
        double renderedHeight,
        List<TableMaskRegion> tableMaskRegions,
        List<TableMaskRegion> diagramMaskRegions,
        List<PdfParagraph> paragraphs,
        PdfParagraph? pageOneTitlePara,
        double pageWidth,
        out double maskPdfX0, out double maskPdfY0, out double maskPdfX1, out double maskPdfY1)
    {
        const double maskPad = 1.5;
        PdfMaskGeometry.GetParagraphPaintBounds(para, out double maskX0, out double maskY0, out double maskX1, out double maskY1);
        if (isFigureCaption)
        {
            maskX0 = Math.Min(para.OriginalX0, para.X0);
            maskY0 = Math.Min(para.OriginalY0, para.Y0);
            maskX1 = Math.Max(para.OriginalX1, para.X1);
            maskY1 = Math.Max(para.OriginalY1, para.Y1);
        }

        maskPdfX0 = maskX0 - maskPad;
        maskPdfX1 = maskX1 + maskPad;
        ComputeBaseMaskY(isFigureCaption, renderedHeight, maskY0, maskY1, out maskPdfY0, out maskPdfY1);
        ApplyRegionClamping(para, isFigureCaption, tableMaskRegions, diagramMaskRegions, paragraphs, pageOneTitlePara, pageWidth, ref maskPdfX0, ref maskPdfY0, ref maskPdfX1, ref maskPdfY1);
    }

    private static bool IsGrayGeometryIntersected(
        double maskPdfX0, double maskPdfY0, double maskPdfX1, double maskPdfY1,
        PdfParagraph para,
        List<TableMaskRegion> effectiveGrayMaskRegions)
    {
        return effectiveGrayMaskRegions.Count > 0 &&
            PdfGrayPromptGeometry.MaskRectIntersectsAnyGrayRegion(maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1, effectiveGrayMaskRegions) &&
            (para.IsGrayPromptContent || PdfGrayPromptClassifier.IsGrayPromptCodeParagraph(para)) &&
            PdfOverlayMaskPlanner.ShouldSuppressOverlayForGrayGeometry(para, effectiveGrayMaskRegions);
    }

    private readonly record struct PageMaskDrawOptions(
        XGraphics Gfx,
        int PageIndex,
        List<PdfParagraph> Paragraphs,
        List<TableMaskRegion> TableMaskRegions,
        List<TableMaskRegion> DiagramMaskRegions,
        List<TableMaskRegion> EffectiveGrayMaskRegions,
        PdfParagraph? PageOneTitlePara,
        string TargetFontName);

    private static void DrawParagraphMasks(PageMaskDrawOptions opts)
    {
        double pageHeightPts = opts.Gfx.PageSize.Height;
        double pageWidth = opts.Gfx.PageSize.Width;
        foreach (var para in opts.Paragraphs)
        {
            if (ShouldSkipMaskForParagraph(para, opts.PageIndex, opts.Paragraphs, pageHeightPts,
                    opts.TableMaskRegions, opts.DiagramMaskRegions, opts.EffectiveGrayMaskRegions, pageWidth))
                continue;

            double pageHeight = opts.Gfx.PageSize.Height;
            bool isFigureCaption = PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para);
            if (PdfParagraphRoleClassifier.IsFindingCallout(para))
            {
                DrawFindingCalloutSurface(opts.Gfx, para, pageHeight);
                continue;
            }

            double renderedHeight = PdfTranslatedParagraphRenderer.RenderParagraph(opts.Gfx, para, opts.TargetFontName, measureOnly: true);

            ComputeMaskBounds(para, isFigureCaption, renderedHeight,
                opts.TableMaskRegions, opts.DiagramMaskRegions, opts.Paragraphs, opts.PageOneTitlePara, pageWidth,
                out double maskPdfX0, out double maskPdfY0, out double maskPdfX1, out double maskPdfY1);

            if (IsGrayGeometryIntersected(maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1, para, opts.EffectiveGrayMaskRegions))
                continue;

            double paragraphX = maskPdfX0;
            double paragraphY = pageHeight - maskPdfY1;
            double paragraphWidth = maskPdfX1 - maskPdfX0;
            double paragraphHeight = maskPdfY1 - maskPdfY0;

            opts.Gfx.DrawRectangle(XBrushes.White, paragraphX, paragraphY, paragraphWidth, paragraphHeight);
        }
    }

    private readonly record struct OverlayDrawOptions(
        XGraphics Gfx,
        int PageIndex,
        List<PdfParagraph> Paragraphs,
        List<TableMaskRegion> TableMaskRegions,
        List<TableMaskRegion> DiagramMaskRegions,
        List<TableMaskRegion> EffectiveGrayMaskRegions,
        bool PageHasTable,
        HashSet<string> StrippedBaseFonts,
        string TargetFontName);

    private static Dictionary<PdfParagraph, List<RenderedChar>> DrawTranslatedOverlays(OverlayDrawOptions opts)
    {
        double pageHeightPts = opts.Gfx.PageSize.Height;
        double pageWidth = opts.Gfx.PageSize.Width;
        var renderedCharsByParagraph = new Dictionary<PdfParagraph, List<RenderedChar>>();
        foreach (var para in opts.Paragraphs)
        {
            if (ShouldSkipOverlayForParagraph(para, opts.PageIndex, opts.Paragraphs, pageHeightPts,
                    opts.TableMaskRegions, opts.DiagramMaskRegions, opts.EffectiveGrayMaskRegions,
                    opts.PageHasTable, pageWidth, out bool isBypassed))
                continue;

            if (isBypassed)
            {
                if (!PdfFontStripper.ParagraphUsesStrippedFont(para, opts.StrippedBaseFonts)) continue;
                PdfBypassedParagraphRenderer.Render(opts.Gfx, para, opts.TargetFontName);
                continue;
            }

            PdfParagraphRenderMetrics renderMetrics = default;
            double measuredHeight = PdfTranslatedParagraphRenderer.RenderParagraph(
                opts.Gfx, para, opts.TargetFontName, measureOnly: true,
                metricsSink: metrics => renderMetrics = metrics);

            ClickraDebug.LogRender(new RenderDebugInfo(
                opts.PageIndex + 1, para.Y0, para.Y1, para.X0, para.X1,
                false,
                renderMetrics.HorizontalOverflow,
                renderMetrics.VerticalOverflow,
                measuredHeight,
                para.TextWithPlaceholders));
            PdfTranslatedParagraphRenderer.RenderParagraph(
                opts.Gfx, para, opts.TargetFontName,
                renderedCharsSink: chars => renderedCharsByParagraph[para] = chars.ToList());
        }
        return renderedCharsByParagraph;
    }

    private static bool ShouldSkipOverlayForParagraph(
        PdfParagraph para,
        int pageIndex,
        List<PdfParagraph> paragraphs,
        double pageHeightPts,
        List<TableMaskRegion> tableMaskRegions,
        List<TableMaskRegion> diagramMaskRegions,
        List<TableMaskRegion> effectiveGrayMaskRegions,
        bool pageHasTable,
        double pageWidth,
        out bool isBypassed)
    {
        isBypassed = para.IsBypassed;
        if (isBypassed) return false; // caller handles bypassed separately

        if (string.IsNullOrWhiteSpace(para.TranslatedText)) return true;
        if (pageIndex == 0 && (para.IsPageTitle || para.SemanticRole == PdfParagraphSemanticRole.PageTitle || PageOneLayoutClassifier.IsAuthorBlockParagraph(para, paragraphs, pageHeightPts))) return true;
        if (IsGrayPromptSuppressed(para, effectiveGrayMaskRegions)) return true;
        if (pageHasTable && tableMaskRegions.Count > 0 && PdfTableMaskPlanner.ParagraphOverlapsAnyTableMask(para.X0, para.Y0, para.X1, para.Y1, tableMaskRegions)) return true;
        if (IsProtectedMaskRegion(para, paragraphs, diagramMaskRegions, pageWidth)) return true;
        return false;
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

    private static string GetTargetFontName(string targetLang)
    {
        if (targetLang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
            targetLang.Equals("ja", StringComparison.OrdinalIgnoreCase))
        {
            return "DFKai-SB";
        }
        if (targetLang.Equals("ko", StringComparison.OrdinalIgnoreCase))
        {
            return "Malgun Gothic";
        }
        if (targetLang.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            return "Arial";
        }
        return "DFKai-SB";
    }

    private static void MapAnnotationsToParagraphs(PdfPage page, List<PdfParagraph> paragraphs)
    {
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
                        .OrderBy(para.AllLetters.IndexOf)
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
                    if (searchText.All(c => !char.IsDigit(c)))
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
    }
}

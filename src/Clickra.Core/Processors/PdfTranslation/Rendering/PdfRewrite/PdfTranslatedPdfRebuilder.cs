using Clickra.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Annotations;
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
        string language = ClickraStorage.GetSetting("Language");
        onProgress?.Invoke(80, 100, Localization.T("pdf_progress_rebuilding", language));
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

        onProgress?.Invoke(95, 100, Localization.T("pdf_progress_saving", language));
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

        ApplyLayoutPlanning(new LayoutPlanningOptions(gfx, paragraphs, targetFontName, pageWidthPts, pageHeightPts, tableMaskRegions, diagramMaskRegions, layoutSummary));

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

    private sealed record LayoutPlanningOptions(
        XGraphics Gfx,
        List<PdfParagraph> Paragraphs,
        string TargetFontName,
        double PageWidthPts,
        double PageHeightPts,
        List<TableMaskRegion> TableMaskRegions,
        List<TableMaskRegion> DiagramMaskRegions,
        PdfTranslationLayoutSummary LayoutSummary);

    private static void ApplyLayoutPlanning(LayoutPlanningOptions opts)
    {
        var layoutPlan = PdfTranslationLayoutPlanner.BuildAndApply(
            opts.Gfx,
            opts.Paragraphs,
            opts.TargetFontName,
            opts.PageWidthPts,
            opts.PageHeightPts,
            opts.TableMaskRegions.Concat(opts.DiagramMaskRegions).ToList());
        opts.LayoutSummary.HeadingCount += layoutPlan.HeadingCount;
        opts.LayoutSummary.ShiftedParagraphCount += layoutPlan.ShiftedParagraphCount;
        opts.LayoutSummary.FixedCollisionCount += layoutPlan.FixedCollisionCount;
        opts.LayoutSummary.BottomOverflowCount += layoutPlan.BottomOverflowCount;
        opts.LayoutSummary.MaximumAlignmentAnchorShift = Math.Max(
            opts.LayoutSummary.MaximumAlignmentAnchorShift,
            layoutPlan.MaximumAlignmentAnchorShift);
        int previousBodyCount = opts.LayoutSummary.BodyParagraphCount;
        int pageBodyCount = layoutPlan.Snapshots.Count(s =>
            s.Role == PdfParagraphSemanticRole.Body &&
            !string.IsNullOrWhiteSpace(s.Paragraph.TranslatedText) &&
            s.Paragraph.TranslatedText.Any(FontUtilities.IsCjkCharacter));
        if (pageBodyCount > 0)
        {
            opts.LayoutSummary.MinimumBodyFontRatio = previousBodyCount == 0
                ? layoutPlan.MinimumBodyFontRatio
                : Math.Min(opts.LayoutSummary.MinimumBodyFontRatio, layoutPlan.MinimumBodyFontRatio);
            opts.LayoutSummary.MaximumBodyFontRatio = Math.Max(
                opts.LayoutSummary.MaximumBodyFontRatio,
                layoutPlan.MaximumBodyFontRatio);
            opts.LayoutSummary.BodyParagraphCount += pageBodyCount;
        }
        opts.LayoutSummary.MaximumBodyLineSpacingMultiplier = Math.Max(
            opts.LayoutSummary.MaximumBodyLineSpacingMultiplier,
            layoutPlan.MaximumBodyLineSpacingMultiplier);
        opts.LayoutSummary.MaximumInterParagraphGap = Math.Max(
            opts.LayoutSummary.MaximumInterParagraphGap,
            layoutPlan.MaximumInterParagraphGap);
        opts.LayoutSummary.MaximumFlowRegionResidualWhitespace = Math.Max(
            opts.LayoutSummary.MaximumFlowRegionResidualWhitespace,
            layoutPlan.MaximumFlowRegionResidualWhitespace);
        if (layoutPlan.Snapshots.Count > 0)
        {
            opts.LayoutSummary.MinimumHeadingFontRatio = Math.Min(
                opts.LayoutSummary.MinimumHeadingFontRatio,
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

    private sealed record ParagraphMaskQuery(
        PdfParagraph Para,
        int PageIndex,
        List<PdfParagraph> Paragraphs,
        double PageHeightPts,
        List<TableMaskRegion> TableMaskRegions,
        List<TableMaskRegion> DiagramMaskRegions,
        List<TableMaskRegion> EffectiveGrayMaskRegions,
        double PageWidth);

    private static bool ShouldSkipMaskForParagraph(ParagraphMaskQuery query)
    {
        if (query.Para.IsBypassed || query.Para.IsTable || string.IsNullOrWhiteSpace(query.Para.TranslatedText)) return true;
        if (query.PageIndex == 0 && PageOneLayoutClassifier.IsAuthorBlockParagraph(query.Para, query.Paragraphs, query.PageHeightPts)) return true;
        if (IsGrayPromptSuppressed(query.Para, query.EffectiveGrayMaskRegions)) return true;
        if (query.TableMaskRegions.Count > 0 && PdfTableMaskPlanner.ParagraphOverlapsAnyTableMask(query.Para.X0, query.Para.Y0, query.Para.X1, query.Para.Y1, query.TableMaskRegions)) return true;
        if (IsProtectedMaskRegion(query.Para, query.Paragraphs, query.DiagramMaskRegions, query.PageWidth)) return true;
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

    private sealed record MaskBoundsContext(
        PdfParagraph Para,
        bool IsFigureCaption,
        double RenderedHeight,
        List<TableMaskRegion> TableMaskRegions,
        List<TableMaskRegion> DiagramMaskRegions,
        List<PdfParagraph> Paragraphs,
        PdfParagraph? PageOneTitlePara,
        double PageWidth);

    private static void ApplyRegionClamping(
        MaskBoundsContext ctx,
        ref double maskPdfX0,
        ref double maskPdfY0,
        ref double maskPdfX1,
        ref double maskPdfY1)
    {
        const double maskPad = 1.5;
        PdfMaskGeometry.ExpandMaskToColumnWidth(ref maskPdfX0, ref maskPdfX1, ctx.Para, ctx.PageWidth);

        if (ctx.PageOneTitlePara != null && ctx.Para == ctx.PageOneTitlePara)
        {
            maskPdfY1 = Math.Min(maskPdfY1, ctx.PageOneTitlePara.Y1 + maskPad);
            maskPdfY0 = Math.Max(maskPdfY0, ctx.PageOneTitlePara.Y0 - maskPad);
        }

        if (ctx.TableMaskRegions.Count > 0 && !ctx.IsFigureCaption)
            maskPdfY0 = PdfTableMaskPlanner.ClampMaskBottomAboveTables(
                maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1, ctx.TableMaskRegions);

        if (ctx.DiagramMaskRegions.Count > 0 && !ctx.IsFigureCaption)
        {
            var clipRegions = PdfOverlayMaskPlanner.GetFigureClipRegions(ctx.Paragraphs, ctx.DiagramMaskRegions, ctx.PageWidth);
            maskPdfY1 = PdfOverlayMaskPlanner.ClampMaskTopBelowDiagrams(
                maskPdfX0, maskPdfY0, maskPdfX1, maskPdfY1, clipRegions, ctx.PageWidth);
        }
    }

    private static void ComputeMaskBounds(
        MaskBoundsContext ctx,
        out double maskPdfX0, out double maskPdfY0, out double maskPdfX1, out double maskPdfY1)
    {
        const double maskPad = 1.5;
        PdfMaskGeometry.GetParagraphPaintBounds(ctx.Para, out double maskX0, out double maskY0, out double maskX1, out double maskY1);
        if (ctx.IsFigureCaption)
        {
            maskX0 = Math.Min(ctx.Para.OriginalX0, ctx.Para.X0);
            maskY0 = Math.Min(ctx.Para.OriginalY0, ctx.Para.Y0);
            maskX1 = Math.Max(ctx.Para.OriginalX1, ctx.Para.X1);
            maskY1 = Math.Max(ctx.Para.OriginalY1, ctx.Para.Y1);
        }

        maskPdfX0 = maskX0 - maskPad;
        maskPdfX1 = maskX1 + maskPad;
        ComputeBaseMaskY(ctx.IsFigureCaption, ctx.RenderedHeight, maskY0, maskY1, out maskPdfY0, out maskPdfY1);
        ApplyRegionClamping(ctx, ref maskPdfX0, ref maskPdfY0, ref maskPdfX1, ref maskPdfY1);
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
            var query = new ParagraphMaskQuery(para, opts.PageIndex, opts.Paragraphs, pageHeightPts, opts.TableMaskRegions, opts.DiagramMaskRegions, opts.EffectiveGrayMaskRegions, pageWidth);
            if (ShouldSkipMaskForParagraph(query))
                continue;

            double pageHeight = opts.Gfx.PageSize.Height;
            bool isFigureCaption = PdfParagraphRoleClassifier.IsFigureTableCaptionParagraph(para);
            if (PdfParagraphRoleClassifier.IsFindingCallout(para))
            {
                DrawFindingCalloutSurface(opts.Gfx, para, pageHeight);
                continue;
            }

            double renderedHeight = PdfTranslatedParagraphRenderer.RenderParagraph(opts.Gfx, para, opts.TargetFontName, measureOnly: true);

            var boundsCtx = new MaskBoundsContext(para, isFigureCaption, renderedHeight, opts.TableMaskRegions, opts.DiagramMaskRegions, opts.Paragraphs, opts.PageOneTitlePara, pageWidth);
            ComputeMaskBounds(boundsCtx, out double maskPdfX0, out double maskPdfY0, out double maskPdfX1, out double maskPdfY1);

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
            var query = new OverlayQueryOptions(para, opts.PageIndex, opts.Paragraphs, pageHeightPts,
                opts.TableMaskRegions, opts.DiagramMaskRegions, opts.EffectiveGrayMaskRegions,
                opts.PageHasTable, pageWidth);
            if (ShouldSkipOverlayForParagraph(query, out bool isBypassed))
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

    private sealed record OverlayQueryOptions(
        PdfParagraph Para,
        int PageIndex,
        List<PdfParagraph> Paragraphs,
        double PageHeightPts,
        List<TableMaskRegion> TableMaskRegions,
        List<TableMaskRegion> DiagramMaskRegions,
        List<TableMaskRegion> EffectiveGrayMaskRegions,
        bool PageHasTable,
        double PageWidth);

    private static bool ShouldSkipOverlayForParagraph(OverlayQueryOptions query, out bool isBypassed)
    {
        isBypassed = query.Para.IsBypassed;
        if (isBypassed) return false; // caller handles bypassed separately

        if (string.IsNullOrWhiteSpace(query.Para.TranslatedText)) return true;
        if (query.PageIndex == 0 && (query.Para.IsPageTitle || query.Para.SemanticRole == PdfParagraphSemanticRole.PageTitle || PageOneLayoutClassifier.IsAuthorBlockParagraph(query.Para, query.Paragraphs, query.PageHeightPts))) return true;
        if (IsGrayPromptSuppressed(query.Para, query.EffectiveGrayMaskRegions)) return true;
        if (query.PageHasTable && query.TableMaskRegions.Count > 0 && PdfTableMaskPlanner.ParagraphOverlapsAnyTableMask(query.Para.X0, query.Para.Y0, query.Para.X1, query.Para.Y1, query.TableMaskRegions)) return true;
        if (IsProtectedMaskRegion(query.Para, query.Paragraphs, query.DiagramMaskRegions, query.PageWidth)) return true;
        return false;
    }

    private static void DrawFindingCalloutSurface(XGraphics gfx, PdfParagraph para, double pageHeight)
    {
        const double padding = 3.2;
        double x0 = Math.Min(para.OriginalX0, para.X0) - padding;
        double bottomPadding = Math.Max(padding, para.Height * 0.10);
        double y0 = Math.Min(para.OriginalY0, para.Y0) - bottomPadding;
        double x1 = Math.Max(para.OriginalX1, para.X1) + padding;
        double y1 = Math.Max(para.OriginalY1, para.Y1) + padding;

        var fill = new XSolidBrush(XColor.FromArgb(229, 247, 253));
        double width = x1 - x0;
        double height = y1 - y0;
        double yGfx = pageHeight - y1;
        gfx.DrawRectangle(fill, x0, yGfx, width, height);

        var borderPen = new XPen(XColor.FromArgb(1, 87, 155), 1.5);
        gfx.DrawLine(borderPen, x0, yGfx, x0, yGfx + height);
    }

    private static string MapTargetFontName(string targetLang)
    {
        if (targetLang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
            targetLang.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase))
        {
            return "SimSun";
        }
        if (targetLang.Equals("ja", StringComparison.OrdinalIgnoreCase))
        {
            return "MS Mincho";
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
        return MapTargetFontName(targetLang);
    }

    private static void MapAnnotationsToParagraphs(PdfPage page, List<PdfParagraph> paragraphs)
    {
        try
        {
            for (int i = 0; i < page.Annotations.Count; i++)
            {
                MapSingleAnnotationToParagraphs(page.Annotations[i], paragraphs);
            }
        }
        catch { }
    }

    private static void MapSingleAnnotationToParagraphs(PdfAnnotation annot, List<PdfParagraph> paragraphs)
    {
        var rect = annot.Rectangle;
        var paraOverlaps = FindOverlappingLettersForAnnotation(rect, paragraphs);
        if (paraOverlaps.Count == 0) return;

        double annotCenterX = (rect.X1 + rect.X2) / 2.0;
        double annotCenterY = (rect.Y1 + rect.Y2) / 2.0;
        var bestPair = paraOverlaps
            .OrderByDescending(kv => PdfAnnotationTextMatcher.ScoreAnnotationParagraph(kv.Key, kv.Value, annotCenterX, annotCenterY))
            .First();
        var bestPara = bestPair.Key;
        var overlappingLetters = bestPair.Value;

        string searchText = ExtractAnnotationSearchText(bestPara, overlappingLetters, rect);
        if (string.IsNullOrEmpty(searchText)) return;

        AttachAnnotationInfo(new AnnotationMappingContext(bestPara, annot, rect, overlappingLetters, annotCenterX, annotCenterY), searchText);
    }

    private static Dictionary<PdfParagraph, List<PdfLetter>> FindOverlappingLettersForAnnotation(
        PdfRectangle rect,
        List<PdfParagraph> paragraphs)
    {
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
        return paraOverlaps;
    }

    private static string ExtractAnnotationSearchText(
        PdfParagraph bestPara,
        List<PdfLetter> overlappingLetters,
        PdfRectangle rect)
    {
        string searchText = string.Join("", overlappingLetters.Select(l => l.Value)).Trim();
        searchText = PdfAnnotationTextMatcher.NormalizeAnnotationSearchText(searchText);
        if (searchText.All(c => !char.IsDigit(c)))
        {
            var nearbyDigit = FindNearbyDigitLetter(bestPara, rect);
            if (nearbyDigit != null)
                searchText = nearbyDigit.Value;
        }
        return searchText;
    }

    private static PdfLetter? FindNearbyDigitLetter(PdfParagraph para, PdfRectangle rect)
    {
        double centerX = (rect.X1 + rect.X2) / 2.0;
        double centerY = (rect.Y1 + rect.Y2) / 2.0;
        return para.AllLetters
            .Where(letter => letter.Value.Length == 1 && char.IsDigit(letter.Value[0]))
            .OrderBy(letter =>
            {
                double dx = ((letter.Left + letter.Right) / 2.0) - centerX;
                double dy = ((letter.Bottom + letter.Top) / 2.0) - centerY;
                return dx * dx + dy * dy;
            })
            .FirstOrDefault();
    }

    private sealed record AnnotationMappingContext(
        PdfParagraph BestPara,
        PdfAnnotation Annotation,
        PdfRectangle Rect,
        List<PdfLetter> OverlappingLetters,
        double CenterX,
        double CenterY);

    private static void AttachAnnotationInfo(AnnotationMappingContext ctx, string searchText)
    {
        int occurrenceIdx = PdfAnnotationOccurrenceMatcher.GetOccurrenceIndex(ctx.BestPara.AllLetters, ctx.OverlappingLetters, searchText);
        int firstLetterIdx = ctx.BestPara.AllLetters.IndexOf(ctx.OverlappingLetters[0]);
        int lastLetterIdx = ctx.BestPara.AllLetters.IndexOf(ctx.OverlappingLetters[^1]);
        int figureOccurrenceIdx = PdfAnnotationOccurrenceMatcher.GetFigureReferenceIndex(ctx.BestPara.AllLetters, firstLetterIdx);
        double relCenterX = ctx.BestPara.Width > 0 ? (ctx.CenterX - ctx.BestPara.X0) / ctx.BestPara.Width : 0.5;
        double relCenterY = ctx.BestPara.Height > 0 ? (ctx.CenterY - ctx.BestPara.Y0) / ctx.BestPara.Height : 0.5;
        double relWidth = ctx.BestPara.Width > 0 ? (ctx.Rect.X2 - ctx.Rect.X1) / ctx.BestPara.Width : 0.05;
        ctx.BestPara.Annotations.Add(new ParagraphAnnotationInfo
        {
            PdfAnnotation = ctx.Annotation,
            Text = searchText,
            OccurrenceIndex = occurrenceIdx,
            FigureOccurrenceIndex = figureOccurrenceIdx,
            FirstLetterIndex = firstLetterIdx,
            LastLetterIndex = lastLetterIdx,
            TotalLetterCount = ctx.BestPara.AllLetters.Count,
            RelCenterX = relCenterX,
            RelCenterY = relCenterY,
            RelWidth = relWidth
        });
    }
}

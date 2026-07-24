using System.Text;
using System.Text.RegularExpressions;
using Clickra.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace Clickra.Core.Processors;

internal static class PdfTranslatedParagraphRenderer
{
    public static double RenderParagraph(
            XGraphics gfx,
            PdfParagraph para,
            string targetFontName,
            bool measureOnly = false,
            Action<PdfParagraphRenderMetrics>? metricsSink = null,
            Action<IReadOnlyList<RenderedChar>>? renderedCharsSink = null)
        {
            double pageHeight = gfx.PageSize.Height;
            double paragraphX = para.X0;
            double paragraphY = pageHeight - para.Y1;
            double paragraphWidth = para.Width;
            double paragraphHeight = para.Height;

            string text = (para.TranslatedText ?? "").Replace('∗', '*');
            text = text.Replace("\u200B", "").Replace("\u200C", "").Replace("\u200D", "").Replace("\uFEFF", "");
            // Providers can preserve control characters from PDF font encodings.
            // Remove them before tokenization; otherwise PdfSharp writes NUL glyphs
            // into the rebuilt content stream and the result renders as tofu boxes.
            text = FontUtilities.NormalizeMathValue(text);
            text = FormulaLiteralCleaner.RemoveDuplicateFormulaLiterals(text, para.Formulas);
            var tokens = PdfParagraphLayoutEngine.TokenizeTranslatedText(text);

            IsHeadingRole(para, out bool isPageTitle, out bool isHeading);
            double fontSize = CalculateFontSize(para, isHeading, out double sourceHeadingFontSize, out double sourceBodyFontFloor);

            DetermineFontNameAndStyle(para, targetFontName, text, out string fontNameForPara, out XFontStyleEx fontStyle);
            XFont mainFont = new(fontNameForPara, fontSize, fontStyle);
            XBrush brush = XBrushes.Black;
            bool inlineBold = para.IsBold;

            XFont GetInlineFont(bool bold)
            {
                if (FontUtilities.IsCjkTranslationFont(fontNameForPara))
                    return mainFont;

                XFontStyleEx style = ResolveFontStyle(bold, para.IsItalic);
                return new XFont(fontNameForPara, fontSize, style);
            }

            bool UseSyntheticBold(bool bold) =>
                !para.IsBypassed && !para.IsCode &&
                FontUtilities.IsCjkTranslationFont(fontNameForPara) && bold;

            bool isRotated = false;
            double layoutWidth = ComputeLayoutWidth(
                gfx, para, isHeading, isPageTitle, paragraphX, paragraphWidth);
            XGraphicsState? state = ApplyRotationTransform(
                gfx, para, paragraphWidth, ref layoutWidth, ref isRotated);

            var lineSpacingResult = CalculateLineSpacingMultiplier(
                para, text, isHeading, isRotated, targetFontName, sourceHeadingFontSize);
            double lineSpacingMultiplier = lineSpacingResult.Multiplier;
            bool allowNaturalBodyHeight = lineSpacingResult.AllowNaturalBodyHeight;
            bool flowableBody = lineSpacingResult.FlowableBody;
            
            if (!isHeading && para.LayoutLineSpacingMultiplierOverride > 0)
                lineSpacingMultiplier = para.LayoutLineSpacingMultiplierOverride;
            double lineHeight = fontSize * lineSpacingMultiplier;
            double limitHeight = isRotated ? para.Width : paragraphHeight;
            var fontState = new FontMetricsState(fontSize, mainFont, lineHeight, lineSpacingMultiplier);
            var shrinkResult = RunFontShrinkLoop(
                new FontShrinkInput(tokens, para.Formulas, gfx, fontNameForPara, fontStyle,
                    layoutWidth, limitHeight, isHeading, allowNaturalBodyHeight, flowableBody, sourceBodyFontFloor,
                    para.AverageFontSize),
                fontState);

            fontSize = shrinkResult.FontSize;
            mainFont = shrinkResult.MainFont;
            lineHeight = shrinkResult.LineHeight;
            var rows = shrinkResult.Rows;
            double maxRowWidth = shrinkResult.MaxRowWidth;
            double renderedHeight = rows.Count * lineHeight;

            ComputeOverflow(new OverflowInput(layoutWidth, limitHeight, paragraphWidth, isHeading, allowNaturalBodyHeight, flowableBody,
                    maxRowWidth, renderedHeight), out bool horizontalOverflow, out bool verticalOverflow, out double effectiveLimitHeight);
            metricsSink?.Invoke(new PdfParagraphRenderMetrics(
                layoutWidth,
                maxRowWidth,
                renderedHeight,
                effectiveLimitHeight,
                horizontalOverflow,
                verticalOverflow,
                fontSize,
                sourceHeadingFontSize,
                rows.Count,
                lineSpacingMultiplier));

            if (measureOnly)
            {
                if (state != null) gfx.Restore(state);
                return renderedHeight;
            }

            double currentY = isRotated ? fontSize : (paragraphY + fontSize);
            var renderedChars = new List<RenderedChar>();
            XGraphicsState? headingScaleState = ApplyHeadingTitleScaleTransform(gfx, isRotated, isPageTitle, maxRowWidth, para);
            RenderParagraphRows(new ParagraphRowRenderOptions(
                gfx, rows, para, text, mainFont, brush, GetInlineFont, UseSyntheticBold,
                paragraphX, paragraphWidth, layoutWidth, lineHeight, fontSize,
                isHeading, isPageTitle, isRotated, currentY, inlineBold, renderedChars));

            if (headingScaleState != null)
                gfx.Restore(headingScaleState);

            if (state != null)
                gfx.Restore(state);

            renderedCharsSink?.Invoke(renderedChars);

            // Align annotations
            if (!isRotated)
                AlignAnnotations(para, renderedChars);

            return renderedHeight;
        }

        private static void IsHeadingRole(PdfParagraph para, out bool isPageTitle, out bool isHeading)
        {
            isPageTitle = para.IsPageTitle || para.SemanticRole == PdfParagraphSemanticRole.PageTitle;
            isHeading = isPageTitle ||
                para.SemanticRole is PdfParagraphSemanticRole.PageTitle or
                    PdfParagraphSemanticRole.AbstractHeading or
                    PdfParagraphSemanticRole.SectionHeading or
                    PdfParagraphSemanticRole.SubsectionHeading ||
                PdfParagraphSemanticClassifier.IsHeadingParagraph(para);
        }

        private static bool IsStandardProseParagraph(PdfParagraph para, bool isRotated)
        {
            return !isRotated && !para.IsBypassed && !para.IsTable && !para.IsDiagram && !para.IsCode && !para.IsGrayPromptContent;
        }

        private static bool CheckAllowNaturalBodyHeight(
            PdfParagraph para,
            bool isRotated,
            bool isStandardProse,
            double sourceHeadingFontSize)
        {
            if (isRotated || !isStandardProse) return false;
            bool bodyProse = PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) ||
                             PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para);
            bool ordinarySingleLine = para.Width > 100;
            double sourceLineBox = Math.Max(para.SourceLineHeight, sourceHeadingFontSize);
            return (bodyProse || ordinarySingleLine) &&
                para.Height <= Math.Max(sourceLineBox * 1.5, 8.0) &&
                (para.Width > 100 ||
                 Regex.IsMatch(para.TextWithPlaceholders.Trim(), @"^[a-z][A-Za-z\s,'\-]{2,}[.!?]?$", RegexOptions.None, TimeSpan.FromSeconds(1)));
        }

        private readonly record struct LineSpacingResult(double Multiplier, bool AllowNaturalBodyHeight, bool FlowableBody);
        private static LineSpacingResult CalculateLineSpacingMultiplier(
            PdfParagraph para,
            string text,
            bool isHeading,
            bool isRotated,
            string targetFontName,
            double sourceHeadingFontSize)
        {
            double lineSpacingMultiplier = 1.35;
            if (isHeading)
            {
                lineSpacingMultiplier = 1.0;
            }
            else if (targetFontName.Contains("Arial", StringComparison.OrdinalIgnoreCase))
            {
                lineSpacingMultiplier = 1.2;
            }
            else if (ReferenceSectionDetector.IsReferenceParagraph(para))
            {
                lineSpacingMultiplier = 1.15;
            }

            bool isStandardProse = IsStandardProseParagraph(para, isRotated);
            bool flowableBody = isStandardProse &&
                (para.SemanticRole == PdfParagraphSemanticRole.Body ||
                 PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) ||
                 PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para)) &&
                text.Any(FontUtilities.IsCjkCharacter);
            bool allowNaturalBodyHeight = CheckAllowNaturalBodyHeight(para, isRotated, isStandardProse, sourceHeadingFontSize);

            bool ordinarySingleLine = isStandardProse && para.Width > 100;
            bool sourceGlyphBoxUnderreports = isStandardProse &&
                sourceHeadingFontSize > 0 && para.SourceLineHeight > 0 &&
                para.SourceLineHeight < sourceHeadingFontSize * 0.95;

            if ((ordinarySingleLine && para.Height < sourceHeadingFontSize) || sourceGlyphBoxUnderreports)
                lineSpacingMultiplier = 1.0;

            return new LineSpacingResult(lineSpacingMultiplier, allowNaturalBodyHeight, flowableBody);
        }

        private static string ResolveFallbackFontName(char c, string mainFontName)
        {
            if (c >= 0x0080 && c <= 0x024F)
                return mainFontName.Contains("Courier") ? "Courier New" : "Arial";
            return "Segoe UI Symbol";
        }

        private static double ComputeLayoutWidth(
            XGraphics gfx,
            PdfParagraph para,
            bool isHeading,
            bool isPageTitle,
            double paragraphX,
            double paragraphWidth)
        {
            double layoutWidth = Math.Max(paragraphWidth, 24.0);
            if (!isHeading) return layoutWidth;

            double pageCenter = gfx.PageSize.Width / 2.0;
            double maxBoundary = isPageTitle
                ? gfx.PageSize.Width + 30.0
                : gfx.PageSize.Width - 54.0;

            if (para.OriginalX1 <= pageCenter + 10.0)
                maxBoundary = pageCenter - 10.0;

            double remainingWidth = maxBoundary - paragraphX;
            if (remainingWidth > layoutWidth)
                layoutWidth = remainingWidth;

            if (isPageTitle)
                layoutWidth = Math.Max(layoutWidth, gfx.PageSize.Width * 1.5);

            return layoutWidth;
        }

        private static XGraphicsState? ApplyRotationTransform(
            XGraphics gfx,
            PdfParagraph para,
            double paragraphWidth,
            ref double layoutWidth,
            ref bool isRotated)
        {
            string dirStr = para.TextDirection?.ToString() ?? "";
            double pageHeight = gfx.PageSize.Height;
            if (dirStr == "Rotate270")
            {
                var state = gfx.Save();
                gfx.TranslateTransform(para.X0, pageHeight - para.Y0);
                gfx.RotateTransform(-90);
                layoutWidth = para.Height;
                isRotated = true;
                return state;
            }
            if (dirStr == "Rotate90")
            {
                var state = gfx.Save();
                gfx.TranslateTransform(para.X1, pageHeight - para.Y1);
                gfx.RotateTransform(90);
                layoutWidth = para.Height;
                isRotated = true;
                return state;
            }
            if (dirStr == "Rotate180")
            {
                var state = gfx.Save();
                gfx.TranslateTransform(para.X1, pageHeight - para.Y0);
                gfx.RotateTransform(180);
                layoutWidth = paragraphWidth;
                isRotated = true;
                return state;
            }
            return null;
        }

        private static XGraphicsState? ApplyHeadingTitleScaleTransform(
            XGraphics gfx, bool isRotated, bool isPageTitle, double maxRowWidth, PdfParagraph para)
        {
            if (isRotated || !isPageTitle || maxRowWidth <= 0) return null;

            double availableTitleWidth = gfx.PageSize.Width - 72.0;
            double horizontalScale = Math.Min(1.0, availableTitleWidth / maxRowWidth);
            if (horizontalScale >= 0.999) return null;

            double anchor = (para.X0 + para.X1) / 2.0;
            var headingScaleState = gfx.Save();
            gfx.TranslateTransform(anchor, 0);
            gfx.ScaleTransform(horizontalScale, 1.0);
            gfx.TranslateTransform(-anchor, 0);
            return headingScaleState;
        }

        private sealed class FontMetricsState(double fontSize, XFont mainFont, double lineHeight, double lineSpacingMultiplier)
        {
            public double FontSize { get; set; } = fontSize;
            public XFont MainFont { get; set; } = mainFont;
            public double LineHeight { get; set; } = lineHeight;
            public double LineSpacingMultiplier { get; } = lineSpacingMultiplier;
        }

        private sealed record FontShrinkResult(
            double FontSize,
            XFont MainFont,
            double LineHeight,
            List<PdfLayoutRow> Rows,
            double RenderedHeight,
            double MaxRowWidth);

        private readonly record struct FontShrinkInput(
            List<string> Tokens,
            List<MathFormula> Formulas,
            XGraphics Gfx,
            string FontName,
            XFontStyleEx FontStyle,
            double LayoutWidth,
            double LimitHeight,
            bool IsHeading,
            bool AllowNaturalBodyHeight,
            bool FlowableBody,
            double SourceBodyFontFloor,
            double AverageFontSize);

        private static FontShrinkResult RunFontShrinkLoop(FontShrinkInput input, FontMetricsState state)
        {
            List<PdfLayoutRow> rows = new();
            double renderedHeight = 0;
            double maxRowWidth = 0;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                rows = PdfParagraphLayoutEngine.LayoutParagraph(
                    input.Tokens, state.MainFont, input.Formulas, input.LayoutWidth, state.FontSize, input.AverageFontSize, input.Gfx);
                renderedHeight = rows.Count * state.LineHeight;
                maxRowWidth = rows.Count == 0
                    ? 0
                    : rows.Max(row => row.Elements.Sum(element => element.Width));

                if (ShouldStopFontShrinking(input, maxRowWidth, renderedHeight)) break;

                if (!TryCalculateNextFontSize(input, state.FontSize, maxRowWidth, renderedHeight, out double nextFontSize))
                    break;

                state.FontSize = nextFontSize;
                state.MainFont = new XFont(input.FontName, state.FontSize, input.FontStyle);
                state.LineHeight = state.FontSize * state.LineSpacingMultiplier;
            }

            return new FontShrinkResult(state.FontSize, state.MainFont, state.LineHeight, rows, renderedHeight, maxRowWidth);
        }

        private static bool ShouldStopFontShrinking(FontShrinkInput input, double maxRowWidth, double renderedHeight)
        {
            bool fitsWidth = maxRowWidth <= input.LayoutWidth + 0.5;
            bool fitsHeight = input.IsHeading || input.AllowNaturalBodyHeight || input.FlowableBody ||
                renderedHeight <= input.LimitHeight + 0.5;
            return (fitsWidth && fitsHeight) || input.IsHeading;
        }

        private static bool TryCalculateNextFontSize(
            FontShrinkInput input, double currentFontSize, double maxRowWidth, double renderedHeight, out double nextFontSize)
        {
            double scale = 0.94;
            bool fitsWidth = maxRowWidth <= input.LayoutWidth + 0.5;
            bool fitsHeight = input.IsHeading || input.AllowNaturalBodyHeight || input.FlowableBody ||
                renderedHeight <= input.LimitHeight + 0.5;

            if (!fitsWidth && maxRowWidth > 0)
                scale = Math.Min(scale, input.LayoutWidth / maxRowWidth);
            if (!fitsHeight && renderedHeight > 0)
                scale = Math.Min(scale, input.LimitHeight / renderedHeight);

            scale = Math.Clamp(scale, 0.80, 0.94);
            double minimumFontSize = Math.Max(input.SourceBodyFontFloor, currentFontSize * 0.80);
            nextFontSize = currentFontSize * scale;
            if (input.FlowableBody)
                nextFontSize = Math.Max(minimumFontSize, nextFontSize);

            return nextFontSize < currentFontSize - 0.01;
        }

        private readonly record struct OverflowInput(
            double LayoutWidth, double LimitHeight, double ParagraphWidth,
            bool IsHeading, bool AllowNaturalBodyHeight, bool FlowableBody,
            double MaxRowWidth, double RenderedHeight);
        private static void ComputeOverflow(
            OverflowInput input,
            out bool horizontalOverflow, out bool verticalOverflow, out double effectiveLimitHeight)
        {
            horizontalOverflow = input.MaxRowWidth > input.LayoutWidth + 0.5;
            effectiveLimitHeight = input.IsHeading || input.AllowNaturalBodyHeight || input.FlowableBody
                ? Math.Max(input.LimitHeight, input.RenderedHeight)
                : input.LimitHeight;
            verticalOverflow = input.RenderedHeight > effectiveLimitHeight + 0.5;
            if (input.ParagraphWidth < 5.0)
            {
                horizontalOverflow = false;
                verticalOverflow = false;
            }
        }
        private static void AlignAnnotations(PdfParagraph para, List<RenderedChar> renderedChars)
        {
            if (para.Annotations.Count == 0 || renderedChars.Count == 0) return;
            foreach (var annotInfo in para.Annotations)
            {
                try
                {
                    var matched = PdfAnnotationTextMatcher.FindAnnotationCharacters(
                        renderedChars,
                        annotInfo.Text,
                        annotInfo.OccurrenceIndex,
                        annotInfo.RelCenterX,
                        annotInfo.RelCenterY,
                        annotInfo.RelWidth,
                        para.X0,
                        para.Y0,
                        para.Width,
                        para.Height,
                        annotInfo.FigureOccurrenceIndex);
                    if (matched?.Count > 0)
                    {
                        double paddingX = 1.0;
                        double paddingY = 1.5;
                        annotInfo.PdfAnnotation.Rectangle = new PdfRectangle(
                            new XPoint(matched.Min(rc => rc.Left) - paddingX, matched.Min(rc => rc.Bottom) - paddingY),
                            new XPoint(matched.Max(rc => rc.Right) + paddingX, matched.Max(rc => rc.Top) + paddingY));
                    }
                    // else: keep original annotation rect (avoid bad spatial fallback)
                }
                catch { }
            }
        }

        private readonly record struct FormulaRenderContext(
            XGraphics Gfx,
            PdfParagraph Para,
            XBrush Brush,
            double FontSize,
            double PageHeight,
            List<RenderedChar> RenderedChars);

        private readonly record struct TextRenderContext(
            XGraphics Gfx,
            XFont MainFont,
            XBrush Brush,
            Func<bool, XFont> GetInlineFont,
            Func<bool, bool> UseSyntheticBold,
            double FontSize,
            double CurrentY,
            double PageHeight,
            List<RenderedChar> RenderedChars);
        private readonly record struct ParagraphRowRenderOptions(
            XGraphics Gfx,
            List<PdfLayoutRow> Rows,
            PdfParagraph Para,
            string Text,
            XFont MainFont,
            XBrush Brush,
            Func<bool, XFont> GetInlineFont,
            Func<bool, bool> UseSyntheticBold,
            double ParagraphX,
            double ParagraphWidth,
            double LayoutWidth,
            double LineHeight,
            double FontSize,
            bool IsHeading,
            bool IsPageTitle,
            bool IsRotated,
            double InitialY,
            bool InlineBold,
            List<RenderedChar> RenderedChars);

        private static void RenderParagraphRows(ParagraphRowRenderOptions opts)
        {
            double currentY = opts.InitialY;
            double pageHeight = opts.Gfx.PageSize.Height;
            bool inlineBold = opts.InlineBold;
            foreach (var row in opts.Rows)
            {
                double rowWidth = row.Elements.Sum(e => e.Width);
                double startX = ComputeRowStartX(opts, rowWidth);

                if (opts.IsPageTitle)
                    ClickraDebug.LogTitleRow((opts.Para.X0 + opts.Para.X1) / 2.0, startX, rowWidth, opts.Text);

                RenderRowElements(opts, row, startX, currentY, pageHeight, ref inlineBold);
                currentY += opts.LineHeight;
            }
        }

        private static double ComputeRowStartX(ParagraphRowRenderOptions opts, double rowWidth)
        {
            if (opts.IsRotated)
            {
                if (opts.Para.Alignment == PdfParagraph.TextAlignment.Center) return (opts.LayoutWidth - rowWidth) / 2;
                if (opts.Para.Alignment == PdfParagraph.TextAlignment.Right) return opts.LayoutWidth - rowWidth;
                return 0;
            }

            PdfParagraph.TextAlignment alignment = opts.IsHeading
                ? InferHeadingAlignment(opts.Para, opts.Gfx.PageSize.Width)
                : opts.Para.Alignment;

            if (alignment == PdfParagraph.TextAlignment.Center)
            {
                double anchorCenter = CalculateAnchorCenter(opts.Para, opts.IsPageTitle, opts.IsHeading, opts.ParagraphX, opts.ParagraphWidth);
                return anchorCenter - rowWidth / 2.0;
            }
            if (alignment == PdfParagraph.TextAlignment.Right)
            {
                double anchorRight = opts.IsHeading ? opts.Para.OriginalX1 : opts.ParagraphX + opts.ParagraphWidth;
                return anchorRight - rowWidth;
            }
            return opts.ParagraphX;
        }

        private static void RenderRowElements(
            ParagraphRowRenderOptions opts,
            PdfLayoutRow row,
            double currentX,
            double currentY,
            double pageHeight,
            ref bool inlineBold)
        {
            int idx = 0;
            while (idx < row.Elements.Count)
            {
                var element = row.Elements[idx];
                if (element.IsStyleMarker)
                {
                    inlineBold = element.StyleBold;
                    idx++;
                }
                else if (element.IsFormula)
                {
                    if (element.FormulaId >= 0 && element.FormulaId < opts.Para.Formulas.Count)
                    {
                        var ctx = new FormulaRenderContext(opts.Gfx, opts.Para, opts.Brush, opts.FontSize, pageHeight, opts.RenderedChars);
                        RenderFormulaElement(ctx, opts.Para.Formulas[element.FormulaId], currentX, currentY);
                    }
                    currentX += element.Width;
                    idx++;
                }
                else
                {
                    var textCtx = new TextRenderContext(opts.Gfx, opts.MainFont, opts.Brush, opts.GetInlineFont,
                        opts.UseSyntheticBold, opts.FontSize, currentY, pageHeight, opts.RenderedChars);
                    currentX = RenderTextRun(textCtx, row, opts.Para, currentX, inlineBold, ref idx);
                }
            }
        }

        private static void RenderFormulaElement(
            FormulaRenderContext ctx,
            MathFormula formula,
            double currentX,
            double currentY)
        {
            double scale = ctx.Para.AverageFontSize > 0 ? ctx.FontSize / ctx.Para.AverageFontSize : 1.0;

            if (FontUtilities.ShouldMergeFormula(formula, ctx.Para.AverageFontSize))
                RenderFormulaMerged(ctx, formula, scale, currentX, currentY);
            else
                RenderFormulaLetters(ctx, formula, scale, currentX, currentY);
        }

        private static void RenderFormulaMerged(
            FormulaRenderContext ctx,
            MathFormula formula,
            double formulaScale,
            double currentX,
            double currentY)
        {
            string mergedText = string.Join("", formula.Letters.Select(l => l.Value));
            double fSize = formula.Letters[0].FontSize * formulaScale;
            string fontToUse = formula.Letters.FirstOrDefault(l => FontUtilities.IsMonospaceFont(l.FontName))?.FontName
                ?? formula.Letters[0].FontName;
            XFont mathFont = FontUtilities.GetMathFont(fontToUse, fSize);
            double avgY = formula.Letters.Average(l => l.RelativeY);
            double my = currentY - avgY * formulaScale - (ctx.FontSize * 0.15);
            string normText = FontUtilities.NormalizeRenderValue(mergedText);
            ctx.Gfx.DrawString(normText, mathFont, ctx.Brush, currentX, my);
            double offset = 0;
            foreach (char ch in normText)
            {
                double mChW = ctx.Gfx.MeasureString(ch.ToString(), mathFont).Width;
                ctx.RenderedChars.Add(new RenderedChar
                {
                    Character = ch,
                    Left = currentX + offset,
                    Right = currentX + offset + mChW,
                    Bottom = ctx.PageHeight - my - fSize * 0.15,
                    Top = ctx.PageHeight - my + fSize * 0.85
                });
                offset += mChW;
            }
        }

        private static void RenderFormulaLetters(
            FormulaRenderContext ctx,
            MathFormula formula,
            double formulaScale,
            double currentX,
            double currentY)
        {
            foreach (var ml in formula.Letters)
            {
                double fSize = ml.FontSize * formulaScale;
                XFont mathFont = FontUtilities.GetMathFont(ml.FontName, fSize);
                double mx = currentX + ml.RelativeX * formulaScale;
                double my = currentY - ml.RelativeY * formulaScale - (ctx.FontSize * 0.15);
                string drawVal = FontUtilities.NormalizeRenderValue(ml.Value);
                if (drawVal.Length == 1 && FontUtilities.IsMathOrGreekCharacter(drawVal[0]))
                    mathFont = new XFont("Segoe UI Symbol", fSize, mathFont.Style);
                ctx.Gfx.DrawString(drawVal, mathFont, ctx.Brush, mx, my);
                double offset = 0;
                foreach (char ch in drawVal)
                {
                    double mlChW = ctx.Gfx.MeasureString(ch.ToString(), mathFont).Width;
                    ctx.RenderedChars.Add(new RenderedChar
                    {
                        Character = ch,
                        Left = mx + offset,
                        Right = mx + offset + mlChW,
                        Bottom = ctx.PageHeight - my - fSize * 0.15,
                        Top = ctx.PageHeight - my + fSize * 0.85
                    });
                    offset += mlChW;
                }
            }
        }

        private static void RenderFallbackFormulaText(
            TextRenderContext ctx,
            PdfLayoutElement element,
            double currentX,
            bool inlineBold)
        {
            string normText = FontUtilities.NormalizeRenderValue(element.Text);
            DrawText(ctx.Gfx, normText, ctx.GetInlineFont(inlineBold), ctx.Brush, currentX, ctx.CurrentY, ctx.UseSyntheticBold(inlineBold));
            double offset = 0;
            foreach (char ch in normText)
            {
                double tChW = ctx.Gfx.MeasureString(ch.ToString(), ctx.MainFont).Width;
                ctx.RenderedChars.Add(new RenderedChar
                {
                    Character = ch,
                    Left = currentX + offset,
                    Right = currentX + offset + tChW,
                    Bottom = ctx.PageHeight - ctx.CurrentY - ctx.FontSize * 0.15,
                    Top = ctx.PageHeight - ctx.CurrentY + ctx.FontSize * 0.85
                });
                offset += tChW;
            }
        }

        private static double RenderTextRun(
            TextRenderContext ctx,
            PdfLayoutRow row,
            PdfParagraph para,
            double currentX,
            bool inlineBold,
            ref int idx)
        {
            var sbMerged = new StringBuilder();
            double textStartX = currentX;
            while (idx < row.Elements.Count && !row.Elements[idx].IsFormula && !row.Elements[idx].IsStyleMarker)
            {
                var elem = row.Elements[idx];
                if (elem.Text.Length == 1 && FontUtilities.IsLatinExtendedOrSymbol(elem.Text[0]))
                {
                    if (sbMerged.Length > 0)
                    {
                        FlushTextBuffer(ctx, sbMerged, textStartX, inlineBold);
                        sbMerged.Clear();
                    }
                    string fallbackFontName = ResolveFallbackFontName(elem.Text[0], ctx.MainFont.FontFamily.Name);
                    XFont fallbackFont = new(fallbackFontName, ctx.MainFont.Size, ResolveFontStyle(inlineBold, para.IsItalic));
                    string normChar = FontUtilities.NormalizeRenderValue(elem.Text);
                    ctx.Gfx.DrawString(normChar, fallbackFont, ctx.Brush, currentX, ctx.CurrentY);
                    double fChW = ctx.Gfx.MeasureString(normChar, fallbackFont).Width;
                    ctx.RenderedChars.Add(new RenderedChar
                    {
                        Character = normChar[0],
                        Left = currentX,
                        Right = currentX + fChW,
                        Bottom = ctx.PageHeight - ctx.CurrentY - ctx.FontSize * 0.15,
                        Top = ctx.PageHeight - ctx.CurrentY + ctx.FontSize * 0.85
                    });
                    textStartX = currentX + elem.Width;
                }
                else
                {
                    sbMerged.Append(elem.Text);
                }
                currentX += elem.Width;
                idx++;
            }
            if (sbMerged.Length > 0)
            {
                FlushTextBuffer(ctx, sbMerged, textStartX, inlineBold);
            }
            return currentX;
        }

        private static void FlushTextBuffer(
            TextRenderContext ctx,
            StringBuilder sbMerged,
            double textStartX,
            bool inlineBold)
        {
            string normText = FontUtilities.NormalizeRenderValue(sbMerged.ToString());
            DrawText(ctx.Gfx, normText, ctx.GetInlineFont(inlineBold), ctx.Brush, textStartX, ctx.CurrentY, ctx.UseSyntheticBold(inlineBold));
            double offset = 0;
            foreach (char ch in normText)
            {
                double tChW = ctx.Gfx.MeasureString(ch.ToString(), ctx.MainFont).Width;
                ctx.RenderedChars.Add(new RenderedChar
                {
                    Character = ch,
                    Left = textStartX + offset,
                    Right = textStartX + offset + tChW,
                    Bottom = ctx.PageHeight - ctx.CurrentY - ctx.FontSize * 0.15,
                    Top = ctx.PageHeight - ctx.CurrentY + ctx.FontSize * 0.85
                });
                offset += tChW;
            }
        }

        private static void DrawText(
            XGraphics gfx,
            string text,
            XFont font,
            XBrush brush,
            double x,
            double y,
            bool syntheticBold)
        {
            gfx.DrawString(text, font, brush, x, y);
            // A 0.18pt offset gives KaiU a stable visual bold weight while
            // preserving every CJK glyph. The source paragraph remains one
            // logical run; the duplicate stroke is only a drawing operation.
            if (syntheticBold)
                gfx.DrawString(text, font, brush, x + 0.18, y);
        }


        private static PdfParagraph.TextAlignment InferHeadingAlignment(PdfParagraph para, double pageWidth)
        {
            double pageCenter = pageWidth / 2.0;
            double currentVisualCenter = (para.X0 + para.X1) / 2.0;
            bool isPageTitle = para.IsPageTitle ||
                para.SemanticRole == PdfParagraphSemanticRole.PageTitle;
            if (isPageTitle && Math.Abs(currentVisualCenter - pageCenter) <= 24.0)
                return PdfParagraph.TextAlignment.Center;

            if (para.Alignment != PdfParagraph.TextAlignment.Left)
                return para.Alignment;

            double sourceCenter = (para.OriginalX0 + para.OriginalX1) / 2.0;
            if (Math.Abs(sourceCenter - pageCenter) <= 18.0)
                return PdfParagraph.TextAlignment.Center;

            // A heading that spans almost an entire column is left aligned even
            // when its geometric center happens to equal the column center.
            // This is common for "A. Research Questions: ..." lines: treating
            // the column midpoint as a centered anchor shifts the translated
            // (shorter) line into the middle of the column.
            if (!isPageTitle && para.Width >= pageWidth * 0.34)
                return PdfParagraph.TextAlignment.Left;

            // Two-column papers commonly place centered subsection headings at
            // roughly 29.5% and 70.5% of the page width.  The source geometry is
            // the reliable anchor; the extracted line often has no side gaps and
            // is therefore misclassified as left-aligned.
            double columnOffset = pageWidth * 0.205;
            double columnCenter = sourceCenter < pageCenter
                ? pageCenter - columnOffset
                : pageCenter + columnOffset;
            return Math.Abs(sourceCenter - columnCenter) <= 18.0
                ? PdfParagraph.TextAlignment.Center
                : PdfParagraph.TextAlignment.Left;
        }

        private static XFontStyleEx ResolveFontStyle(bool bold, bool italic)
        {
            if (bold)
                return italic ? XFontStyleEx.BoldItalic : XFontStyleEx.Bold;
            return italic ? XFontStyleEx.Italic : XFontStyleEx.Regular;
        }

        private static double GetSourceHeadingFontSize(PdfParagraph para)
        {
            if (para.SourceVisualFontSize > 0)
                return para.SourceVisualFontSize;
            return para.AllLetters.Count == 0
                ? para.AverageFontSize
                : para.AllLetters.Max(letter => letter.FontSize);
        }
        private static double CalculateAnchorCenter(PdfParagraph para, bool isPageTitle, bool isHeading, double paragraphX, double paragraphWidth)
        {
            if (isPageTitle)
                return (para.X0 + para.X1) / 2.0;
            if (isHeading)
                return (para.OriginalX0 + para.OriginalX1) / 2.0;
            return paragraphX + paragraphWidth / 2.0;
        }

        private static double CalculateFontSize(PdfParagraph para, bool isHeading, double sourceHeadingFontSize, double sourceBodyFontFloor)
        {
            double fontSize = isHeading
                ? Math.Max(para.AverageFontSize, sourceHeadingFontSize)
                : Math.Max(para.AverageFontSize, sourceBodyFontFloor);
            if (!isHeading && para.LayoutFontSizeOverride > 0)
                fontSize = Math.Max(para.LayoutFontSizeOverride, sourceBodyFontFloor);
            return fontSize;
        }

        private static double CalculateFontSize(
            PdfParagraph para,
            bool isHeading,
            out double sourceHeadingFontSize,
            out double sourceBodyFontFloor)
        {
            sourceHeadingFontSize = GetSourceHeadingFontSize(para);
            sourceBodyFontFloor = sourceHeadingFontSize > 0
                ? sourceHeadingFontSize * 0.80
                : para.AverageFontSize;
            return CalculateFontSize(para, isHeading, sourceHeadingFontSize, sourceBodyFontFloor);
        }

        private static void DetermineFontNameAndStyle(PdfParagraph para, string targetFontName, string text, out string fontNameForPara, out XFontStyleEx fontStyle)
        {
            fontNameForPara = targetFontName;
            if (para.IsCode)
            {
                fontNameForPara = "Courier New";
            }
            else if (para.IsBypassed)
            {
                fontNameForPara = ResolveBypassedFontName(para, targetFontName, text);
            }

            fontStyle = XFontStyleEx.Regular;
            if (!para.IsBypassed && !para.IsCode && FontUtilities.IsCjkTranslationFont(fontNameForPara))
            {
                fontStyle = XFontStyleEx.Regular;
            }
            else if (para.IsBold)
            {
                fontStyle = para.IsItalic ? XFontStyleEx.BoldItalic : XFontStyleEx.Bold;
            }
            else if (para.IsItalic)
            {
                fontStyle = XFontStyleEx.Italic;
            }
        }

        private static string ResolveBypassedFontName(PdfParagraph para, string targetFontName, string text)
        {
            if (text.Any(FontUtilities.IsCjkCharacter))
                return targetFontName;

            if (para.AllLetters.Count == 0)
                return "Times New Roman";

            string fn = para.AllLetters[0].FontName.ToLowerInvariant();
            if (fn.Contains("times") || fn.Contains("serif") || fn.Contains("liberation"))
                return "Times New Roman";
            if (fn.Contains("arial") || fn.Contains("helvetica") || fn.Contains("sans"))
                return "Arial";
            if (fn.Contains("courier") || fn.Contains("mono") || fn.Contains("consolas"))
                return "Courier New";

            return "Times New Roman";
        }
    }

internal readonly record struct PdfParagraphRenderMetrics(
    double LayoutWidth,
    double MaxRowWidth,
    double RenderedHeight,
    double HeightLimit,
    bool HorizontalOverflow,
    bool VerticalOverflow,
    double EffectiveFontSize,
    double SourceFontSize,
    int LineCount,
    double LineSpacingMultiplier);

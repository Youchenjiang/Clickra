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

            // The layout planner captures the source semantic role before any
            // translation/reflow.  Use that role as authoritative so a heading
            // whose translated text no longer matches the source heuristic can
            // never fall back to body sizing.
            bool isPageTitle = para.IsPageTitle ||
                para.SemanticRole == PdfParagraphSemanticRole.PageTitle;
            bool isHeading = isPageTitle ||
                para.SemanticRole is PdfParagraphSemanticRole.PageTitle or
                    PdfParagraphSemanticRole.AbstractHeading or
                    PdfParagraphSemanticRole.SectionHeading or
                    PdfParagraphSemanticRole.SubsectionHeading ||
                PdfParagraphSemanticClassifier.IsHeadingParagraph(para);
            // MergeTitleWithSubtitle combines the title and its smaller subtitle into
            // one paragraph.  AverageFontSize would therefore make the translated
            // heading smaller than body text.  Headings retain the largest source
            // glyph size and may reflow vertically instead of shrinking away.
            double sourceHeadingFontSize = GetSourceHeadingFontSize(para);
            // A paragraph can contain a short label line followed by body text.
            // Its arithmetic average can then be far below the source reading
            // size (the page 414 contributions label was rendered at ~4.7pt).
            // Keep prose at no less than 80% of the largest source glyph. This
            // is a floor; code and bypass regions retain their own renderer.
            double sourceBodyFontFloor = sourceHeadingFontSize > 0
                ? sourceHeadingFontSize * 0.80
                : para.AverageFontSize;
            double fontSize = isHeading
                ? Math.Max(para.AverageFontSize, sourceHeadingFontSize)
                : Math.Max(para.AverageFontSize, sourceBodyFontFloor);
            if (!isHeading && para.LayoutFontSizeOverride > 0)
                // A continuation override may only carry a paragraph toward
                // its neighbouring source typography; it must never reapply
                // a tiny extractor size (ASTER page 11: 5.1pt) to a 9.96pt
                // body line. Keep the documented 80% floor as a hard guard.
                fontSize = Math.Max(para.LayoutFontSizeOverride, sourceBodyFontFloor);
            string fontNameForPara = targetFontName;
            if (para.IsCode)
            {
                fontNameForPara = "Courier New";
            }
            else if (para.IsBypassed)
            {
                if (text.Any(FontUtilities.IsCjkCharacter))
                {
                    fontNameForPara = targetFontName;
                }
                else
                {
                    fontNameForPara = "Times New Roman";
                    if (para.AllLetters.Count > 0)
                    {
                        string fn = para.AllLetters[0].FontName.ToLowerInvariant();
                        if (fn.Contains("times") || fn.Contains("serif") || fn.Contains("liberation"))
                            fontNameForPara = "Times New Roman";
                        else if (fn.Contains("arial") || fn.Contains("helvetica") || fn.Contains("sans"))
                            fontNameForPara = "Arial";
                        else if (fn.Contains("courier") || fn.Contains("mono") || fn.Contains("consolas"))
                            fontNameForPara = "Courier New";
                    }
                }
            }
            XFontStyleEx fontStyle = XFontStyleEx.Regular;
            // Translated CJK uses the stable regular KaiU face. Bold is drawn
            // with a tiny second stroke below, avoiding unsupported CJK faces.
            if (!para.IsBypassed && !para.IsCode && FontUtilities.IsCjkTranslationFont(fontNameForPara))
            {
                fontStyle = XFontStyleEx.Regular;
            }
            else if (para.IsBold)
            {
                fontStyle = para.IsItalic ? XFontStyleEx.BoldItalic : XFontStyleEx.Bold;
            }
            else
            {
                fontStyle = para.IsItalic ? XFontStyleEx.Italic : XFontStyleEx.Regular;
            }
            XFont mainFont = new(fontNameForPara, fontSize, fontStyle);
            XBrush brush = XBrushes.Black;
            // Heading role controls size/alignment, not weight.  Weight must
            // come from the source glyph runs; otherwise italic-only labels
            // such as "A. Research Questions" become falsely bold.
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
            // Handle rotations (90, 180, 270)
            bool isRotated = false;
            // PdfPig can emit a one-glyph marker with a bbox narrower than the
            // glyph itself.  Give such markers a minimal measurable box rather
            // than reporting a false overflow (there is no adjacent prose to
            // collide with).
            double layoutWidth = Math.Max(paragraphWidth, 24.0);
            if (!isRotated && isHeading)
            {
                double pageCenter = gfx.PageSize.Width / 2.0;
                double maxBoundary = isPageTitle
                    ? gfx.PageSize.Width + 30.0
                    : gfx.PageSize.Width - 54.0; // Default right margin

                // If it's in the left column, limit expansion to the middle of the page
                if (para.OriginalX1 <= pageCenter + 10.0)
                {
                    maxBoundary = pageCenter - 10.0;
                }

                double remainingWidth = maxBoundary - paragraphX;
                if (remainingWidth > layoutWidth)
                {
                    layoutWidth = remainingWidth;
                }
                if (isPageTitle)
                {
                    // A translated title may be substantially wider than the
                    // Latin source.  Give the line breaker the full title band;
                    // the draw pass applies horizontal fitting while retaining
                    // the source vertical font size.
                    layoutWidth = Math.Max(layoutWidth, gfx.PageSize.Width * 1.5);
                }
            }
            XGraphicsState? state = null;
            string dirStr = para.TextDirection?.ToString() ?? "";

            if (dirStr == "Rotate270")
            {
                double startX = para.X0;
                double startY = pageHeight - para.Y0;
                state = gfx.Save();
                gfx.TranslateTransform(startX, startY);
                gfx.RotateTransform(-90);
                layoutWidth = para.Height;
                isRotated = true;
            }
            else if (dirStr == "Rotate90")
            {
                double startX = para.X1;
                double startY = pageHeight - para.Y1;
                state = gfx.Save();
                gfx.TranslateTransform(startX, startY);
                gfx.RotateTransform(90);
                layoutWidth = para.Height;
                isRotated = true;
            }
            else if (dirStr == "Rotate180")
            {
                double startX = para.X1;
                double startY = pageHeight - para.Y0;
                state = gfx.Save();
                gfx.TranslateTransform(startX, startY);
                gfx.RotateTransform(180);
                layoutWidth = paragraphWidth;
                isRotated = true;
            }
            // Compute dynamic line spacing
            double lineSpacingMultiplier = 1.35; // Default CJK line height
            if (isHeading)
            {
                // Keep the source heading font size but use a compact line box;
                // translated title groups must not consume the fixed author band.
                lineSpacingMultiplier = 1.0;
            }
            if (targetFontName.Contains("Arial", StringComparison.OrdinalIgnoreCase))
            {
                lineSpacingMultiplier = 1.2;
            }
            if (ReferenceSectionDetector.IsReferenceParagraph(para))
            {
                lineSpacingMultiplier = 1.15;
            }
            double limitHeight = isRotated ? para.Width : paragraphHeight;
            bool bodyProse = PdfParagraphRoleClassifier.IsTranslatableBodyProse(para) ||
                             PdfParagraphRoleClassifier.IsTranslatableCalloutProse(para);
            bool flowableBody = !isRotated && !para.IsBypassed &&
                !para.IsTable && !para.IsDiagram && !para.IsCode &&
                !para.IsGrayPromptContent &&
                para.SemanticRole == PdfParagraphSemanticRole.Body &&
                text.Any(FontUtilities.IsCjkCharacter);
            // Only a one-line/continuation box with an implausibly short source
            // height may grow naturally. Multi-line body paragraphs still use
            // their measured box and the normal 80% reflow floor.
            // PdfPig reports a single source line's glyph box without its
            // ascender/descender leading (ASTER page 11 is ~6.8pt for a
            // 9.96pt line). Treat that as a natural one-line body box using
            // the captured visual size; otherwise the height loop shrinks the
            // first header/acknowledgement line to 5.1pt merely to fit the
            // extractor bbox.
            double sourceLineBox = Math.Max(para.SourceLineHeight, sourceHeadingFontSize);
            bool ordinarySingleLine = !isRotated && !para.IsBypassed &&
                !para.IsTable && !para.IsDiagram && !para.IsCode &&
                !para.IsGrayPromptContent && para.Width > 100;
            bool allowNaturalBodyHeight = !isRotated &&
                (bodyProse || ordinarySingleLine) &&
                para.Height <= Math.Max(sourceLineBox * 1.5, 8.0) &&
                (para.Width > 100 ||
                 Regex.IsMatch(para.TextWithPlaceholders.Trim(), @"^[a-z][A-Za-z\s,'\-]{2,}[.!?]?$", RegexOptions.None, TimeSpan.FromSeconds(1)) );
            // When the source glyph box is shorter than the captured visual
            // font, preserve the font size but use a compact line box. This
            // keeps split acknowledgement/header fragments within the source
            // band without shrinking them to the extractor's 5pt height.
            bool sourceGlyphBoxUnderreports = !isRotated && !para.IsBypassed &&
                sourceHeadingFontSize > 0 && para.SourceLineHeight > 0 &&
                para.SourceLineHeight < sourceHeadingFontSize * 0.95;
            if ((ordinarySingleLine && para.Height < sourceHeadingFontSize) || sourceGlyphBoxUnderreports)
                lineSpacingMultiplier = 1.0;
            // The layout planner applies bounded vertical justification after
            // source glyph-box normalization. Applying this earlier would let
            // the compact single-line fallback silently overwrite the planned
            // leading and leave large residual holes in otherwise flowable text.
            if (!isHeading && para.LayoutLineSpacingMultiplierOverride > 0)
                lineSpacingMultiplier = para.LayoutLineSpacingMultiplierOverride;
            double lineHeight = fontSize * lineSpacingMultiplier;
            List<PdfLayoutRow> rows = new();
            double renderedHeight = 0;
            double maxRowWidth = 0;

            // Translated CJK can require more rows than the source paragraph. Reflow and
            // reduce the font gradually, including headings, until both dimensions fit.
            // The lower bound preserves legibility while avoiding the old clip-and-hide path.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                rows = PdfParagraphLayoutEngine.LayoutParagraph(
                    tokens, mainFont, para.Formulas, layoutWidth, fontSize, para.AverageFontSize, gfx);
                renderedHeight = rows.Count * lineHeight;
                maxRowWidth = rows.Count == 0
                    ? 0
                    : rows.Max(row => row.Elements.Sum(element => element.Width));

                bool fitsWidth = maxRowWidth <= layoutWidth + 0.5;
                // A heading is allowed to consume its natural line height.  The
                // mask is extended upward for the extra height; shrinking a
                // heading to fit the old one-line box breaks hierarchy.
                // Body paragraphs must not shrink merely because PdfPig gave a
                // final source line an undersized glyph box. Their natural line
                // height is valid; masks/layout planning handle the extra space.
                bool fitsHeight = isHeading || allowNaturalBodyHeight || flowableBody ||
                    renderedHeight <= limitHeight + 0.5;
                if (fitsWidth && fitsHeight) break;
                // Heading hierarchy is a hard constraint. A heading that does
                // not fit must be handled by the layout planner or fail closed.
                if (isHeading) break;

                double scale = 0.94;
                if (!fitsWidth && maxRowWidth > 0)
                    scale = Math.Min(scale, layoutWidth / maxRowWidth);
                if (!fitsHeight && renderedHeight > 0)
                    scale = Math.Min(scale, limitHeight / renderedHeight);

                scale = Math.Clamp(scale, 0.80, 0.94);
                double minimumFontSize = Math.Max(sourceBodyFontFloor, para.AverageFontSize * 0.80);
                double nextFontSize = fontSize * scale;
                if (flowableBody)
                    nextFontSize = Math.Max(minimumFontSize, nextFontSize);
                if (nextFontSize >= fontSize - 0.01) break;

                fontSize = nextFontSize;
                mainFont = new XFont(fontNameForPara, fontSize, fontStyle);
                lineHeight = fontSize * lineSpacingMultiplier;
            }

            // Actual rendered height = number of rows × line height
            renderedHeight = rows.Count * lineHeight;
            bool horizontalOverflow = maxRowWidth > layoutWidth + 0.5;
            double effectiveLimitHeight = isHeading || allowNaturalBodyHeight
                ? Math.Max(limitHeight, renderedHeight)
                : limitHeight;
            bool verticalOverflow = renderedHeight > effectiveLimitHeight + 0.5;
            if (paragraphWidth < 5.0)
            {
                // A sub-5pt marker is an isolated PDF extraction artifact, not
                // a prose column.  Do not turn its glyph bbox quantization into
                // a document-level overflow failure.
                horizontalOverflow = false;
                verticalOverflow = false;
            }
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

            // In measure-only mode, skip all drawing and just return the height
            if (measureOnly)
            {
                if (state != null) gfx.Restore(state);
                return renderedHeight;
            }

            double currentY = isRotated ? fontSize : (paragraphY + fontSize);
            var renderedChars = new List<RenderedChar>();
            XGraphicsState? headingScaleState = null;
            if (!isRotated && isPageTitle && maxRowWidth > 0)
            {
                double availableTitleWidth = gfx.PageSize.Width - 72.0;
                double horizontalScale = Math.Min(1.0, availableTitleWidth / maxRowWidth);
                if (horizontalScale < 0.999)
                {
                    double anchor = (para.X0 + para.X1) / 2.0;
                    headingScaleState = gfx.Save();
                    gfx.TranslateTransform(anchor, 0);
                    gfx.ScaleTransform(horizontalScale, 1.0);
                    gfx.TranslateTransform(-anchor, 0);
                }
            }

            foreach (var row in rows)

            {
                double rowWidth = row.Elements.Sum(e => e.Width);
                double startX = paragraphX;
                if (isRotated)
                {
                    startX = 0;
                    if (para.Alignment == PdfParagraph.TextAlignment.Center) startX = (layoutWidth - rowWidth) / 2;
                    else if (para.Alignment == PdfParagraph.TextAlignment.Right) startX = layoutWidth - rowWidth;
                }
                else
                {
                    PdfParagraph.TextAlignment alignment = isHeading
                        ? InferHeadingAlignment(para, gfx.PageSize.Width)
                        : para.Alignment;
                    if (alignment == PdfParagraph.TextAlignment.Center)
                    {
                        double anchorCenter = CalculateAnchorCenter(para, isPageTitle, isHeading, paragraphX, paragraphWidth);
                        startX = anchorCenter - rowWidth / 2.0;
                    }
                    else if (alignment == PdfParagraph.TextAlignment.Right)
                    {
                        double anchorRight = isHeading ? para.OriginalX1 : paragraphX + paragraphWidth;
                        startX = anchorRight - rowWidth;
                    }
                }

                if (isPageTitle)
                    ClickraDebug.LogTitleRow((para.X0 + para.X1) / 2.0, startX, rowWidth, text);

                double currentX = startX;
                int idx = 0;
                while (idx < row.Elements.Count)
                {
                    var element = row.Elements[idx];
                    if (element.IsStyleMarker)
                    {
                        inlineBold = element.StyleBold;
                        idx++;
                        continue;
                    }
                    if (element.IsFormula && element.FormulaId >= 0 && element.FormulaId < para.Formulas.Count)
                    {
                        var formula = para.Formulas[element.FormulaId];
                        double scale = fontSize / para.AverageFontSize;

                        bool hasMono = formula.Letters.Any(l => FontUtilities.IsMonospaceFont(l.FontName));
                        double formulaScale = scale;
                        if (hasMono)
                        {
                            formulaScale *= 1.0;
                        }

                        if (FontUtilities.ShouldMergeFormula(formula, para.AverageFontSize))
                        {
                            string mergedText = string.Join("", formula.Letters.Select(l => l.Value));
                            double fSize = formula.Letters[0].FontSize * formulaScale;

                            string fontToUse = formula.Letters[0].FontName;
                            foreach (var l in formula.Letters)
                            {
                                if (FontUtilities.IsMonospaceFont(l.FontName))
                                {
                                    fontToUse = l.FontName;
                                    break;
                                }
                            }

                            XFont mathFont = FontUtilities.GetMathFont(fontToUse, fSize);

                            double avgY = formula.Letters.Average(l => l.RelativeY);
                            double my = currentY - avgY * formulaScale - (fontSize * 0.15);

                            string normText = FontUtilities.NormalizeRenderValue(mergedText);
                            gfx.DrawString(normText, mathFont, brush, currentX, my);

                            double offset = 0;
                            for (int cIdx = 0; cIdx < normText.Length; cIdx++)
                            {
                                char ch = normText[cIdx];
                                double mChW = gfx.MeasureString(ch.ToString(), mathFont).Width;
                                renderedChars.Add(new RenderedChar
                                {
                                    Character = ch,
                                    Left = currentX + offset,
                                    Right = currentX + offset + mChW,
                                    Bottom = pageHeight - my - fSize * 0.15,
                                    Top = pageHeight - my + fSize * 0.85
                                });
                                offset += mChW;
                            }
                        }
                        else
                        {
                            foreach (var ml in formula.Letters)
                            {
                                double fSize = ml.FontSize * formulaScale;
                                XFont mathFont = FontUtilities.GetMathFont(ml.FontName, fSize);

                                double mx = currentX + ml.RelativeX * formulaScale;
                                // Align math letter baseline with CJK baseline by shifting up slightly instead of down
                                double my = currentY - ml.RelativeY * formulaScale - (fontSize * 0.15);

                                string drawVal = FontUtilities.NormalizeRenderValue(ml.Value);
                                if (drawVal.Length == 1 && FontUtilities.IsMathOrGreekCharacter(drawVal[0]))
                                {
                                    mathFont = new XFont("Segoe UI Symbol", fSize, mathFont.Style);
                                }

                                gfx.DrawString(drawVal, mathFont, brush, mx, my);

                                double offset = 0;
                                for (int cIdx = 0; cIdx < drawVal.Length; cIdx++)
                                {
                                    char ch = drawVal[cIdx];
                                    double mlChW = gfx.MeasureString(ch.ToString(), mathFont).Width;
                                    renderedChars.Add(new RenderedChar
                                    {
                                        Character = ch,
                                        Left = mx + offset,
                                        Right = mx + offset + mlChW,
                                        Bottom = pageHeight - my - fSize * 0.15,
                                        Top = pageHeight - my + fSize * 0.85
                                    });
                                    offset += mlChW;
                                }
                            }
                        }
                        currentX += element.Width;
                        idx++;
                    }
                    else if (element.IsFormula)
                    {
                        // Defensive: LayoutParagraph should demote invalid {vN}, but render as text if not.
                        string normText = FontUtilities.NormalizeRenderValue(element.Text);
                        DrawText(gfx, normText, GetInlineFont(inlineBold), brush, currentX, currentY, UseSyntheticBold(inlineBold));
                        double offset = 0;
                        for (int cIdx = 0; cIdx < normText.Length; cIdx++)
                        {
                            char ch = normText[cIdx];
                            double tChW = gfx.MeasureString(ch.ToString(), mainFont).Width;
                            renderedChars.Add(new RenderedChar
                            {
                                Character = ch,
                                Left = currentX + offset,
                                Right = currentX + offset + tChW,
                                Bottom = pageHeight - currentY - fontSize * 0.15,
                                Top = pageHeight - currentY + fontSize * 0.85
                            });
                            offset += tChW;
                        }
                        currentX += element.Width;
                        idx++;
                    }
                    else
                    {
                        var sbMerged = new StringBuilder();
                        double textStartX = currentX;
                        double textWidth = 0;
                        while (idx < row.Elements.Count && !row.Elements[idx].IsFormula && !row.Elements[idx].IsStyleMarker)
                        {
                            var elem = row.Elements[idx];
                            if (elem.Text.Length == 1 && FontUtilities.IsLatinExtendedOrSymbol(elem.Text[0]))
                            {
                                if (sbMerged.Length > 0)
                                {
                                    string normText = FontUtilities.NormalizeRenderValue(sbMerged.ToString());
                                    DrawText(gfx, normText, GetInlineFont(inlineBold), brush, textStartX, currentY, UseSyntheticBold(inlineBold));

                                    double offset = 0;
                                    for (int cIdx = 0; cIdx < normText.Length; cIdx++)
                                    {
                                        char ch = normText[cIdx];
                                        double tChW = gfx.MeasureString(ch.ToString(), mainFont).Width;
                                        renderedChars.Add(new RenderedChar
                                        {
                                            Character = ch,
                                            Left = textStartX + offset,
                                            Right = textStartX + offset + tChW,
                                            Bottom = pageHeight - currentY - fontSize * 0.15,
                                            Top = pageHeight - currentY + fontSize * 0.85
                                        });
                                        offset += tChW;
                                    }
                                    sbMerged.Clear();
                                }
                                char c = elem.Text[0];
                                string fallbackFontName;
                                if (c >= 0x0080 && c <= 0x024F)
                                {
                                    fallbackFontName = mainFont.FontFamily.Name.Contains("Courier") ? "Courier New" : "Arial";
                                }
                                else
                                {
                                    fallbackFontName = "Segoe UI Symbol";
                                }
                                XFont fallbackFont = new(fallbackFontName, mainFont.Size, ResolveFontStyle(inlineBold, para.IsItalic));
                                string normChar = FontUtilities.NormalizeRenderValue(elem.Text);
                                gfx.DrawString(normChar, fallbackFont, brush, currentX, currentY);

                                double fChW = gfx.MeasureString(normChar, fallbackFont).Width;
                                renderedChars.Add(new RenderedChar
                                {
                                    Character = normChar[0],
                                    Left = currentX,
                                    Right = currentX + fChW,
                                    Bottom = pageHeight - currentY - fontSize * 0.15,
                                    Top = pageHeight - currentY + fontSize * 0.85
                                });

                                textStartX = currentX + elem.Width;
                            }
                            else
                            {
                                sbMerged.Append(elem.Text);
                            }
                            textWidth += elem.Width;
                            currentX += elem.Width;
                            idx++;
                        }
                        if (sbMerged.Length > 0)
                        {
                            string normText = FontUtilities.NormalizeRenderValue(sbMerged.ToString());
                            DrawText(gfx, normText, GetInlineFont(inlineBold), brush, textStartX, currentY, UseSyntheticBold(inlineBold));

                            double offset = 0;
                            for (int cIdx = 0; cIdx < normText.Length; cIdx++)
                            {
                                char ch = normText[cIdx];
                                double eChW = gfx.MeasureString(ch.ToString(), mainFont).Width;
                                renderedChars.Add(new RenderedChar
                                {
                                    Character = ch,
                                    Left = textStartX + offset,
                                    Right = textStartX + offset + eChW,
                                    Bottom = pageHeight - currentY - fontSize * 0.15,
                                    Top = pageHeight - currentY + fontSize * 0.85
                                });
                                offset += eChW;
                            }
                        }
                    }
                }
                currentY += lineHeight;
            }

            if (headingScaleState != null)
                gfx.Restore(headingScaleState);

            if (state != null)
            {
                gfx.Restore(state);
            }

            renderedCharsSink?.Invoke(renderedChars);

            // Align annotations
            if (!isRotated && para.Annotations.Count > 0 && renderedChars.Count > 0)
            {
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
                        if (matched != null && matched.Count > 0)
                        {
                            double minLeft = matched.Min(rc => rc.Left);
                            double maxRight = matched.Max(rc => rc.Right);
                            double minBottom = matched.Min(rc => rc.Bottom);
                            double maxTop = matched.Max(rc => rc.Top);

                            double paddingX = 1.0;
                            double paddingY = 1.5;

                            annotInfo.PdfAnnotation.Rectangle = new PdfRectangle(
                                new XPoint(minLeft - paddingX, minBottom - paddingY),
                                new XPoint(maxRight + paddingX, maxTop + paddingY)
                            );
                        }
                        // else: keep original annotation rect (avoid bad spatial fallback)
                    }
                    catch { }
                }
            }

            return renderedHeight;
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

using System.Text;
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
            Action<PdfParagraphRenderMetrics>? metricsSink = null)
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

            double fontSize = para.AverageFontSize;
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
            // Translated CJK must use regular kaiu.ttf; bold/italic from source (e.g. NimbusRom Medi) maps to
            // simsunb.ttf via ClickraFontResolver and produces SimSun-ExtB garbled overlays.
            if (!para.IsBypassed && !para.IsCode && FontUtilities.IsCjkTranslationFont(fontNameForPara))
            {
                fontStyle = XFontStyleEx.Regular;
            }
            else if (para.IsBold || PdfParagraphSemanticClassifier.IsHeadingParagraph(para))
            {
                fontStyle = para.IsItalic ? XFontStyleEx.BoldItalic : XFontStyleEx.Bold;
            }
            else
            {
                fontStyle = para.IsItalic ? XFontStyleEx.Italic : XFontStyleEx.Regular;
            }
            XFont mainFont = new XFont(fontNameForPara, fontSize, fontStyle);
            XBrush brush = XBrushes.Black;

            // Handle rotations (90, 180, 270)
            bool isRotated = false;
            double layoutWidth = paragraphWidth;
            if (!isRotated && PdfParagraphSemanticClassifier.IsHeadingParagraph(para))
            {
                double pageCenter = gfx.PageSize.Width / 2.0;
                double maxBoundary = gfx.PageSize.Width - 54.0; // Default right margin

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
            if (targetFontName.Contains("Arial", StringComparison.OrdinalIgnoreCase))
            {
                lineSpacingMultiplier = 1.2;
            }
            if (ReferenceSectionDetector.IsReferenceParagraph(para))
            {
                lineSpacingMultiplier = 1.15;
            }
            double lineHeight = fontSize * lineSpacingMultiplier;

            double limitHeight = isRotated ? para.Width : paragraphHeight;
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
                bool fitsHeight = renderedHeight <= limitHeight + 0.5;
                if (fitsWidth && fitsHeight) break;

                double scale = 0.94;
                if (!fitsWidth && maxRowWidth > 0)
                    scale = Math.Min(scale, layoutWidth / maxRowWidth);
                if (!fitsHeight && renderedHeight > 0)
                    scale = Math.Min(scale, limitHeight / renderedHeight);

                scale = Math.Clamp(scale, 0.65, 0.94);
                double nextFontSize = fontSize * scale;
                if (nextFontSize >= fontSize - 0.01) break;

                fontSize = nextFontSize;
                mainFont = new XFont(fontNameForPara, fontSize, fontStyle);
                lineHeight = fontSize * lineSpacingMultiplier;
            }

            // Actual rendered height = number of rows × line height
            renderedHeight = rows.Count * lineHeight;
            bool horizontalOverflow = maxRowWidth > layoutWidth + 0.5;
            bool verticalOverflow = renderedHeight > limitHeight + 0.5;
            metricsSink?.Invoke(new PdfParagraphRenderMetrics(
                layoutWidth,
                maxRowWidth,
                renderedHeight,
                limitHeight,
                horizontalOverflow,
                verticalOverflow));

            // In measure-only mode, skip all drawing and just return the height
            if (measureOnly)
            {
                if (state != null) gfx.Restore(state);
                return renderedHeight;
            }

            double currentY = isRotated ? fontSize : (paragraphY + fontSize);
            var renderedChars = new List<RenderedChar>();

            // Clip to prevent horizontal overflow into adjacent columns; vertical clip uses rendered height
            // so multi-line translations are not cut when Chinese text needs more rows than the original English.
            XGraphicsState? clipState = null;
            if (!isRotated)
            {
                clipState = gfx.Save();
                double clipX = paragraphX - 1.5;
                double clipY = paragraphY - 1.5;
                double clipW = layoutWidth + 3.0;
                double clipH = Math.Max(paragraphHeight, renderedHeight) + lineHeight * 0.4 + 4.0;
                gfx.IntersectClip(new XRect(clipX, clipY, clipW, clipH));
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
                    if (para.Alignment == PdfParagraph.TextAlignment.Center) startX = paragraphX + (paragraphWidth - rowWidth) / 2;
                    else if (para.Alignment == PdfParagraph.TextAlignment.Right) startX = paragraphX + (paragraphWidth - rowWidth);
                }

                double currentX = startX;
                int idx = 0;
                while (idx < row.Elements.Count)
                {
                    var element = row.Elements[idx];
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

                            string normText = FontUtilities.NormalizeMathValue(mergedText.Normalize(NormalizationForm.FormKD));
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

                                string drawVal = FontUtilities.NormalizeMathValue(ml.Value.Normalize(NormalizationForm.FormKD));
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
                        string normText = FontUtilities.NormalizeMathValue(element.Text.Normalize(NormalizationForm.FormKD));
                        gfx.DrawString(normText, mainFont, brush, currentX, currentY);
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
                        while (idx < row.Elements.Count && !row.Elements[idx].IsFormula)
                        {
                            var elem = row.Elements[idx];
                            if (elem.Text.Length == 1 && FontUtilities.IsLatinExtendedOrSymbol(elem.Text[0]))
                            {
                                if (sbMerged.Length > 0)
                                {
                                    string normText = FontUtilities.NormalizeMathValue(sbMerged.ToString().Normalize(NormalizationForm.FormKD));
                                    gfx.DrawString(normText, mainFont, brush, textStartX, currentY);

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
                                XFont fallbackFont = new XFont(fallbackFontName, mainFont.Size, mainFont.Style);
                                string normChar = FontUtilities.NormalizeMathValue(elem.Text.Normalize(NormalizationForm.FormKD));
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
                            string normText = FontUtilities.NormalizeMathValue(sbMerged.ToString().Normalize(NormalizationForm.FormKD));
                            gfx.DrawString(normText, mainFont, brush, textStartX, currentY);

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

            // Restore clipping state
            if (clipState != null)
            {
                gfx.Restore(clipState);
            }

            if (state != null)
            {
                gfx.Restore(state);
            }

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
                            para.Height);
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

}

internal readonly record struct PdfParagraphRenderMetrics(
    double LayoutWidth,
    double MaxRowWidth,
    double RenderedHeight,
    double HeightLimit,
    bool HorizontalOverflow,
    bool VerticalOverflow);

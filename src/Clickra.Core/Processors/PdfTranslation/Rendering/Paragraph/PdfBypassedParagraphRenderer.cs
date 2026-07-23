using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Clickra.Core.Models;
using PdfSharp.Drawing;

namespace Clickra.Core.Processors
{
    internal static class PdfBypassedParagraphRenderer
    {
        public static void Render(XGraphics gfx, PdfParagraph para, string targetFontName)
        {
            double pageHeight = gfx.PageSize.Height;
            XBrush brush = XBrushes.Black;
            double tableFontSize = para.AverageFontSize > 0 ? para.AverageFontSize : 10;
            var formulaLetterKeys = BuildFormulaLetterKeys(para);
            string direction = para.TextDirection?.ToString() ?? "Rotate0";
            double paragraphRotation = direction switch
            {
                "Rotate90" => 90,
                "Rotate180" => 180,
                "Rotate270" => -90,
                _ => 0
            };

            foreach (var letter in para.AllLetters)
            {
                if (string.IsNullOrEmpty(letter.Value) || string.IsNullOrWhiteSpace(letter.Value)) continue;
                if (formulaLetterKeys.Contains(FormulaLetterKey(letter))) continue;

                double fontSize = para.IsTable ? tableFontSize : letter.FontSize;
                XFont font = letter.Value.Any(FontUtilities.IsCjkCharacter)
                    ? new XFont(targetFontName, fontSize, XFontStyleEx.Regular)
                    : FontUtilities.GetMathFont(letter.FontName, fontSize);

                string drawVal = FontUtilities.NormalizeRenderValue(letter.Value);
                if (drawVal.Length == 1 &&
                    (FontUtilities.IsMathOrGreekCharacter(drawVal[0]) || drawVal[0] == '*' || drawVal[0] == '†' || drawVal[0] == '‡'))
                {
                    font = new XFont("Segoe UI Symbol", fontSize, font.Style);
                }

                double x = letter.X;
                double y = pageHeight - letter.Y;
                double rotation = Math.Abs(letter.Rotation) > 0.1 ? letter.Rotation : paragraphRotation;
                if (rotation == 0)
                {
                    gfx.DrawString(drawVal, font, brush, x, y);
                    DrawSyntheticBold(gfx, drawVal, font, brush, x, y, letter.IsBold, letter.Value.Any(FontUtilities.IsCjkCharacter));
                }
                else
                {
                    // PdfPig reports rotated table labels as individual glyph
                    // positions. Draw each glyph around its source baseline;
                    // otherwise Java SE/EE labels become upright stacked text
                    // and appear to move outside their table cells.
                    var state = gfx.Save();
                    gfx.TranslateTransform(x, y);
                    gfx.RotateTransform(rotation);
                    gfx.DrawString(drawVal, font, brush, 0, 0);
                    DrawSyntheticBold(gfx, drawVal, font, brush, 0, 0, letter.IsBold, letter.Value.Any(FontUtilities.IsCjkCharacter));
                    gfx.Restore(state);
                }
            }

            // AllLetters is the authoritative source-positioned glyph stream
            // for bypassed paragraphs and already includes formula glyphs.
            // Repainting Formulas as well duplicates equations when embedded
            // font control characters prevent exact formula-letter matching.
            if (para.AllLetters.Count == 0)
            {
                foreach (var formula in para.Formulas)
                {
                    RenderBypassedFormula(gfx, para, formula, pageHeight, brush);
                }
            }
        }

        private static void DrawSyntheticBold(
            XGraphics gfx,
            string text,
            XFont font,
            XBrush brush,
            double x,
            double y,
            bool sourceBold,
            bool cjk)
        {
            // Latin/math source faces use an actual bold XFont. CJK fallback
            // faces are regular-only; duplicate only those glyphs to preserve
            // the source weight without changing their metrics.
            if (!sourceBold || !cjk) return;
            gfx.DrawString(text, font, brush, x + 0.18, y);
        }

        private static string FormulaLetterKey(PdfLetter letter)
        {
            return $"{letter.X:F2}|{letter.Y:F2}|{letter.Value}";
        }

        internal static int FindFormulaSubsequence(IReadOnlyList<PdfLetter> allLetters, IReadOnlyList<MathLetter> formulaLetters)
        {
            int formulaLength = formulaLetters.Count;
            if (formulaLength == 0) return -1;

            for (int i = 0; i <= allLetters.Count - formulaLength; i++)
            {
                bool match = true;
                for (int j = 0; j < formulaLength; j++)
                {
                    if (allLetters[i + j].Value != formulaLetters[j].Value)
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private static HashSet<string> BuildFormulaLetterKeys(PdfParagraph para)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (para.Formulas.Count == 0 || para.AllLetters.Count == 0) return keys;

            foreach (var formula in para.Formulas)
            {
                int startIdx = FindFormulaSubsequence(para.AllLetters, formula.Letters);
                if (startIdx >= 0)
                {
                    int formulaLength = formula.Letters.Count;
                    for (int j = 0; j < formulaLength; j++)
                    {
                        keys.Add(FormulaLetterKey(para.AllLetters[startIdx + j]));
                    }
                }
            }
            return keys;
        }

        private static void RenderBypassedFormula(
            XGraphics gfx, PdfParagraph para, MathFormula formula, double pageHeight, XBrush brush)
        {
            int formulaLength = formula.Letters.Count;
            if (formulaLength == 0) return;
            int startIdx = FindFormulaSubsequence(para.AllLetters, formula.Letters);

            if (startIdx < 0)
            {
                foreach (var ml in formula.Letters)
                {
                    double fSize = ml.FontSize;
                    XFont mathFont = FontUtilities.GetMathFont(ml.FontName, fSize);
                    string drawVal = FontUtilities.NormalizeRenderValue(ml.Value);
                    if (drawVal.Length == 1 &&
                        (FontUtilities.IsMathOrGreekCharacter(drawVal[0]) || drawVal[0] == '*' || drawVal[0] == '†' || drawVal[0] == '‡'))
                    {
                        mathFont = new XFont("Segoe UI Symbol", fSize, mathFont.Style);
                    }
                    double x = ml.X;
                    double y = ml.Y;
                    gfx.DrawString(drawVal, mathFont, brush, x, pageHeight - y);
                }
                return;
            }

            for (int j = 0; j < formula.Letters.Count; j++)
            {
                var ml = formula.Letters[j];
                var letter = para.AllLetters[startIdx + j];
                double fSize = ml.FontSize;
                XFont mathFont = FontUtilities.GetMathFont(ml.FontName, fSize);
                string drawVal = FontUtilities.NormalizeRenderValue(ml.Value);
                if (drawVal.Length == 1 &&
                    (FontUtilities.IsMathOrGreekCharacter(drawVal[0]) || drawVal[0] == '*' || drawVal[0] == '†' || drawVal[0] == '‡'))
                {
                    mathFont = new XFont("Segoe UI Symbol", fSize, mathFont.Style);
                }
                double x = letter.X;
                double y = pageHeight - letter.Y;
                gfx.DrawString(drawVal, mathFont, brush, x, y);
            }
        }
    }
}

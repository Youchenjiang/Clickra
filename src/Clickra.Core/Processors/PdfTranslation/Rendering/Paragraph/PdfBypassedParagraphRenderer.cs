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

            foreach (var letter in para.AllLetters)
            {
                if (string.IsNullOrEmpty(letter.Value) || string.IsNullOrWhiteSpace(letter.Value)) continue;
                if (formulaLetterKeys.Contains(FormulaLetterKey(letter))) continue;

                double fontSize = para.IsTable ? tableFontSize : letter.FontSize;
                XFont font;
                if (letter.Value.Any(FontUtilities.IsCjkCharacter))
                {
                    font = new XFont(targetFontName, fontSize, XFontStyleEx.Regular);
                }
                else
                {
                    font = FontUtilities.GetMathFont(letter.FontName, fontSize);
                }

                string drawVal = FontUtilities.NormalizeMathValue(letter.Value.Normalize(NormalizationForm.FormKD));
                if (drawVal.Length == 1 &&
                    (FontUtilities.IsMathOrGreekCharacter(drawVal[0]) || drawVal[0] == '*' || drawVal[0] == '†' || drawVal[0] == '‡'))
                {
                    font = new XFont("Segoe UI Symbol", fontSize, font.Style);
                }

                double x = letter.X;
                double y = pageHeight - letter.Y;
                gfx.DrawString(drawVal, font, brush, x, y);
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

        private static string FormulaLetterKey(PdfLetter letter)
        {
            return $"{letter.X:F2}|{letter.Y:F2}|{letter.Value}";
        }

        private static HashSet<string> BuildFormulaLetterKeys(PdfParagraph para)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (para.Formulas.Count == 0 || para.AllLetters.Count == 0) return keys;

            foreach (var formula in para.Formulas)
            {
                string needle = string.Concat(formula.Letters.Select(l => l.Value));
                if (needle.Length == 0) continue;

                for (int i = 0; i <= para.AllLetters.Count - needle.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < needle.Length; j++)
                    {
                        if (para.AllLetters[i + j].Value != formula.Letters[j].Value)
                        {
                            match = false;
                            break;
                        }
                    }
                    if (!match) continue;
                    for (int j = 0; j < needle.Length; j++)
                    {
                        keys.Add(FormulaLetterKey(para.AllLetters[i + j]));
                    }
                    break;
                }
            }
            return keys;
        }

        private static void RenderBypassedFormula(
            XGraphics gfx, PdfParagraph para, MathFormula formula, double pageHeight, XBrush brush)
        {
            if (formula.Letters.Count == 0) return;
            string needle = string.Concat(formula.Letters.Select(l => l.Value));
            int startIdx = -1;
            for (int i = 0; i <= para.AllLetters.Count - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (para.AllLetters[i + j].Value != formula.Letters[j].Value)
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    startIdx = i;
                    break;
                }
            }

            if (startIdx < 0)
            {
                foreach (var ml in formula.Letters)
                {
                    double fSize = ml.FontSize;
                    XFont mathFont = FontUtilities.GetMathFont(ml.FontName, fSize);
                    string drawVal = FontUtilities.NormalizeMathValue(ml.Value.Normalize(NormalizationForm.FormKD));
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
                string drawVal = FontUtilities.NormalizeMathValue(ml.Value.Normalize(NormalizationForm.FormKD));
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

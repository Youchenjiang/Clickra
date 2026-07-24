using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Clickra.Core.Models;
using PdfSharp.Drawing;

namespace Clickra.Core.Processors
{
    internal static class PdfParagraphLayoutEngine
    {
        public static List<string> TokenizeTranslatedText(string text)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            int i = 0;
            int len = text.Length;
            while (i < len)
            {
                if (TryTokenizeSpecialTag(text, len, list, sb, ref i))
                    continue;

                char c = text[i];
                if (c == '\n' || c == '\r')
                {
                    if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); }
                    list.Add("\n");
                    if (c == '\r' && i + 1 < len && text[i + 1] == '\n') i++;
                    i++;
                    continue;
                }

                if (FontUtilities.IsCjkCharacter(c) || FontUtilities.IsLatinExtendedOrSymbol(c) || c == ' ')
                {
                    if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); }
                    list.Add(c.ToString());
                    i++;
                    continue;
                }

                sb.Append(c);
                i++;
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list;
        }

        private static bool TryTokenizeSpecialTag(string text, int len, List<string> list, StringBuilder sb, ref int i)
        {
            if (TryTokenizeBoldTag(text, list, sb, ref i)) return true;
            return TryTokenizeVariableTag(text, len, list, sb, ref i);
        }

        private static bool TryTokenizeBoldTag(string text, List<string> list, StringBuilder sb, ref int i)
        {
            if (text.AsSpan(i).StartsWith("{b}"))
            {
                if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); }
                list.Add("{b}");
                i += 3;
                return true;
            }
            if (text.AsSpan(i).StartsWith("{/b}"))
            {
                if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); }
                list.Add("{/b}");
                i += 4;
                return true;
            }
            return false;
        }

        private static bool TryTokenizeVariableTag(string text, int len, List<string> list, StringBuilder sb, ref int i)
        {
            if (text[i] == '{' && i + 2 < len && text[i + 1] == 'v')
            {
                int j = text.IndexOf('}', i);
                if (j != -1)
                {
                    if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); }
                    list.Add(text.Substring(i, j - i + 1));
                    i = j + 1;
                    return true;
                }
            }
            return false;
        }
            return false;
        }

        public static List<PdfLayoutRow> LayoutParagraph(List<string> tokens, XFont font, List<MathFormula> formulas, double maxWidth, double fontSize, double averageFontSize, XGraphics gfx)
        {
            var rows = new List<PdfLayoutRow>();
            var currentRow = new PdfLayoutRow();
            double currentX = 0;

            foreach (var token in tokens)
            {
                if (token == "\n")
                {
                    rows.Add(currentRow);
                    currentRow = new PdfLayoutRow();
                    currentX = 0;
                    continue;
                }

                if (token is "{b}" or "{/b}")
                {
                    currentRow.Elements.Add(new PdfLayoutElement
                    {
                        Text = token,
                        IsStyleMarker = true,
                        StyleBold = token == "{b}",
                        Width = 0
                    });
                    continue;
                }

                var measured = MeasureTokenWidth(new TokenMeasureOptions(token, font, formulas, fontSize, averageFontSize, gfx));
                bool isFormula = measured.IsFormula;
                int formulaId = measured.FormulaId;
                double width = measured.Width;

                if (width > maxWidth && !isFormula && token.Length > 1 && token != " " &&
                    TrySplitOverlongToken(token, font, maxWidth, gfx, ref currentRow, ref rows, ref currentX))
                {
                    continue;
                }

                if (currentX + width > maxWidth && currentRow.Elements.Count > 0)
                {
                    rows.Add(currentRow);
                    currentRow = new PdfLayoutRow();
                    currentX = 0;
                    if (token == " ") continue;
                }

                currentRow.Elements.Add(new PdfLayoutElement
                {
                    Text = token,
                    IsFormula = isFormula,
                    FormulaId = formulaId,
                    Width = width
                });
                currentX += width;
            }

            if (currentRow.Elements.Count > 0) rows.Add(currentRow);

            return rows;
        }

        private readonly record struct TokenMeasureOptions(
            string Token,
            XFont Font,
            List<MathFormula> Formulas,
            double FontSize,
            double AverageFontSize,
            XGraphics Gfx);

        private readonly record struct MeasuredTokenInfo(
            bool IsFormula,
            int FormulaId,
            double Width);

        private static MeasuredTokenInfo MeasureTokenWidth(TokenMeasureOptions opts)
        {
            bool isFormula = opts.Token.StartsWith("{v") && opts.Token.EndsWith('}');
            if (isFormula)
            {
                if (int.TryParse(opts.Token.Substring(2, opts.Token.Length - 3), out int formulaId) && formulaId >= 0 && formulaId < opts.Formulas.Count)
                {
                    var formula = opts.Formulas[formulaId];
                    double formulaScale = opts.FontSize / opts.AverageFontSize;
                    return new MeasuredTokenInfo(true, formulaId, formula.Width * formulaScale);
                }
                return new MeasuredTokenInfo(false, -1, opts.Gfx.MeasureString(FontUtilities.NormalizeMathValue(opts.Token), opts.Font).Width);
            }

            if (opts.Token == " ")
            {
                return new MeasuredTokenInfo(false, -1, opts.Gfx.MeasureString(" ", opts.Font).Width);
            }

            if (opts.Token.Length == 1 && FontUtilities.IsLatinExtendedOrSymbol(opts.Token[0]))
            {
                char c = opts.Token[0];
                string fontName = (c >= 0x0080 && c <= 0x024F)
                    ? (opts.Font.FontFamily.Name.Contains("Courier") ? "Courier New" : "Arial")
                    : "Segoe UI Symbol";
                XFont fallbackFont = new(fontName, opts.Font.Size, opts.Font.Style);
                return new MeasuredTokenInfo(false, -1, opts.Gfx.MeasureString(FontUtilities.NormalizeMathValue(opts.Token), fallbackFont).Width);
            }

            return new MeasuredTokenInfo(false, -1, opts.Gfx.MeasureString(FontUtilities.NormalizeMathValue(opts.Token), opts.Font).Width);
        }

        private static bool TrySplitOverlongToken(
            string token,
            XFont font,
            double maxWidth,
            XGraphics gfx,
            ref PdfLayoutRow currentRow,
            ref List<PdfLayoutRow> rows,
            ref double currentX)
        {
            char[] breakChars = ['/', '-', '.', '_', '='];
            var subTokens = new List<string>();
            var sb2 = new StringBuilder();
            foreach (char ch in token)
            {
                if (Array.IndexOf(breakChars, ch) >= 0)
                {
                    sb2.Append(ch);
                    subTokens.Add(sb2.ToString());
                    sb2.Clear();
                }
                else
                {
                    sb2.Append(ch);
                }
            }
            if (sb2.Length > 0) subTokens.Add(sb2.ToString());

            if (subTokens.Count > 1)
            {
                foreach (var sub in subTokens)
                {
                    double subWidth = gfx.MeasureString(FontUtilities.NormalizeMathValue(sub), font).Width;
                    if (currentX + subWidth > maxWidth && currentRow.Elements.Count > 0)
                    {
                        rows.Add(currentRow);
                        currentRow = new PdfLayoutRow();
                        currentX = 0;
                    }
                    currentRow.Elements.Add(new PdfLayoutElement { Text = sub, IsFormula = false, FormulaId = -1, Width = subWidth });
                    currentX += subWidth;
                }
                return true;
            }
            return false;
        }
    }
}

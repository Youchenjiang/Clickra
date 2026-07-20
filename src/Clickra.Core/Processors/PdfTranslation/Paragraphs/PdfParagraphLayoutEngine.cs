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
                if (text.AsSpan(i).StartsWith("{b}"))
                {
                    if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); }
                    list.Add("{b}");
                    i += 3;
                    continue;
                }
                if (text.AsSpan(i).StartsWith("{/b}"))
                {
                    if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); }
                    list.Add("{/b}");
                    i += 4;
                    continue;
                }
                if (text[i] == '{' && i + 2 < len && text[i + 1] == 'v')
                {
                    int j = i;
                    while (j < len && text[j] != '}') j++;
                    if (j < len && text[j] == '}')
                    {
                        if (sb.Length > 0)
                        {
                            list.Add(sb.ToString());
                            sb.Clear();
                        }
                        list.Add(text.Substring(i, j - i + 1));
                        i = j + 1;
                        continue;
                    }
                }

                char c = text[i];
                if (c == '\n' || c == '\r')
                {
                    if (sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Clear();
                    }
                    list.Add("\n");
                    if (c == '\r' && i + 1 < len && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    i++;
                    continue;
                }

                if (FontUtilities.IsCjkCharacter(c) || FontUtilities.IsLatinExtendedOrSymbol(c))
                {
                    if (sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Clear();
                    }
                    list.Add(c.ToString());
                    i++;
                    continue;
                }

                if (c == ' ')
                {
                    if (sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Clear();
                    }
                    list.Add(" ");
                    i++;
                    continue;
                }

                sb.Append(c);
                i++;
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list;
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

                bool isFormula = token.StartsWith("{v") && token.EndsWith("}");
                double width = 0;
                int formulaId = -1;

                if (isFormula)
                {
                    if (int.TryParse(token.Substring(2, token.Length - 3), out formulaId) && formulaId >= 0 && formulaId < formulas.Count)
                    {
                        var formula = formulas[formulaId];
                        double formulaScale = fontSize / averageFontSize;
                        bool hasMono = formula.Letters.Any(l => FontUtilities.IsMonospaceFont(l.FontName));
                        if (hasMono)
                        {
                            formulaScale *= 1.0;
                        }
                        width = formula.Width * formulaScale;
                    }
                    else
                    {
                        // Placeholder {vN} without a matching formula (e.g. CCS/footnote body text) — render as text.
                        isFormula = false;
                        formulaId = -1;
                        width = gfx.MeasureString(FontUtilities.NormalizeMathValue(token), font).Width;
                    }
                }
                else
                {
                    if (token == " ")
                    {
                        width = gfx.MeasureString(" ", font).Width;
                    }
                    else if (token.Length == 1 && FontUtilities.IsLatinExtendedOrSymbol(token[0]))
                    {
                        char c = token[0];
                        string fontName;
                        if (c >= 0x0080 && c <= 0x024F)
                        {
                            fontName = font.FontFamily.Name.Contains("Courier") ? "Courier New" : "Arial";
                        }
                        else
                        {
                            fontName = "Segoe UI Symbol";
                        }
                        XFont fallbackFont = new XFont(fontName, font.Size, font.Style);
                        width = gfx.MeasureString(FontUtilities.NormalizeMathValue(token), fallbackFont).Width;
                    }
                    else
                    {
                        width = gfx.MeasureString(FontUtilities.NormalizeMathValue(token), font).Width;
                    }
                }

                // If single token is wider than maxWidth, split at URL-friendly breakpoints
                if (width > maxWidth && !isFormula && token.Length > 1 && token != " ")
                {
                    // Try to split the token at URL/path-friendly characters
                    var breakChars = new char[] { '/', '-', '.', '_', '=' };
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
                        continue;
                    }
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

            if (currentRow.Elements.Count > 0)
            {
                rows.Add(currentRow);
            }

            return rows;
        }
    }
}

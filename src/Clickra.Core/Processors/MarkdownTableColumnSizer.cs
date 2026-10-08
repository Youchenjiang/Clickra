using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.Tables;

namespace Clickra.Core.Processors;

/// <summary>
/// Resolves renderer-neutral Markdown table column proportions from intrinsic
/// cell content. This mirrors browser/Word auto-layout much more closely than
/// equal columns while keeping PDF and DOCX on one deterministic contract.
/// </summary>
internal static class MarkdownTableColumnSizer
{
    /// <summary>Computes normalized table column width fractions from cell content for both PDF and DOCX layout.</summary>
    public static IReadOnlyList<double> ResolveFractions(Table table, int columnCount)
    {
        if (columnCount <= 0) return Array.Empty<double>();

        double[] scores = Enumerable.Repeat(5d, columnCount).ToArray();
        foreach (TableRow row in table.OfType<TableRow>())
        {
            for (int column = 0; column < Math.Min(columnCount, row.Count); column++)
            {
                if (row[column] is not TableCell cell) continue;
                scores[column] = Math.Max(scores[column], 5 + MeasureCellUnits(cell));
            }
        }

        double total = scores.Sum();
        return scores.Select(score => score / total).ToArray();
    }

    private static double MeasureCellUnits(TableCell cell)
    {
        double longest = 0;
        foreach (Block child in cell)
        {
            if (child is LeafBlock leaf && leaf.Inline is not null)
                longest = Math.Max(longest, MeasureInlineUnits(leaf.Inline));
            else if (child is ContainerBlock nested)
                longest = Math.Max(longest, MeasureContainerUnits(nested));
        }
        return longest;
    }

    private static double MeasureContainerUnits(ContainerBlock block)
    {
        double longest = 0;
        foreach (Block child in block)
        {
            if (child is LeafBlock leaf && leaf.Inline is not null)
                longest = Math.Max(longest, MeasureInlineUnits(leaf.Inline));
            else if (child is ContainerBlock nested)
                longest = Math.Max(longest, MeasureContainerUnits(nested));
        }
        return longest;
    }

    private static double MeasureInlineUnits(ContainerInline container)
    {
        double units = 0;
        for (Inline? inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    units += MeasureTextUnits(literal.Content.ToString());
                    break;
                case CodeInline code:
                    units += MeasureTextUnits(code.Content);
                    break;
                case LineBreakInline:
                    break;
                case ContainerInline nested:
                    units += MeasureInlineUnits(nested);
                    break;
            }
        }
        return units;
    }

    private static double MeasureTextUnits(string text)
    {
        double units = 0;
        foreach (char ch in text)
        {
            if (char.IsWhiteSpace(ch)) units += 0.32;
            else if (IsWide(ch)) units += 1.15;
            else if (char.IsLetterOrDigit(ch)) units += 0.56;
            else units += 0.5;
        }
        return units;
    }

    private static bool IsWide(char ch) =>
        ch is >= '\u2e80' and <= '\u303f' or
            >= '\u3040' and <= '\u30ff' or
            >= '\u31c0' and <= '\u31ef' or
            >= '\u3400' and <= '\u9fff' or
            >= '\uac00' and <= '\ud7af' or
            >= '\ufe10' and <= '\ufe1f' or
            >= '\ufe30' and <= '\ufe4f' or
            >= '\uff00' and <= '\uffef';
}

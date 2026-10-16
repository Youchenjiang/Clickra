using System;
using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Modules.PdfTranslation.Model;
using UglyToad.PdfPig.DocumentLayoutAnalysis;

namespace Clickra.Core.Modules.PdfTranslation.Pipeline;

internal static class PdfParagraphFactory
{
    public static PdfParagraph Create(IReadOnlyList<TextLine> lines)
    {
        var para = new PdfParagraph();
        if (lines == null || lines.Count == 0)
        {
            return para;
        }

        var analysis = PdfParagraphAnalyzer.Analyze(lines);
        var layout = PdfParagraphLayoutAnalyzer.Analyze(lines, analysis.TextWithPlaceholders);

        para.TextWithPlaceholders = analysis.TextWithPlaceholders;
        para.TranslationTextWithStyles = analysis.TranslationTextWithStyles;
        para.AverageFontSize = analysis.AverageFontSize;
        para.IsBold = analysis.IsBold;
        para.IsItalic = analysis.IsItalic;
        para.IsOnlyMath = analysis.IsOnlyMath;
        para.IsCode = analysis.IsCode;
        para.brk = analysis.HasLineBreak;
        para.Formulas = analysis.Formulas;
        para.AllLetters = analysis.AllLetters;
        para.SourceVisualFontSize = para.AllLetters.Count == 0 ? para.AverageFontSize : para.AllLetters.Max(l => l.FontSize);
        para.SourceLineHeight = para.AllLetters.Count == 0 ? 0 : para.AllLetters.Max(l => l.Top - l.Bottom);

        para.X0 = layout.X0;
        para.Y0 = layout.Y0;
        para.X1 = layout.X1;
        para.Y1 = layout.Y1;
        para.SetOriginalBounds(layout.X0, layout.Y0, layout.X1, layout.Y1);
        para.TextDirection = layout.TextDirection;
        para.Alignment = layout.Alignment;

        return para;
    }
}

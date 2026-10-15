using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Modules.PdfTranslation.Model;
using UglyToad.PdfPig.DocumentLayoutAnalysis;

namespace Clickra.Core.Modules.PdfTranslation.Pipeline;

internal static class PdfPageReadingOrder
{
    public static bool IsLineInLeftColumn(TextLine line, double pageWidth)
    {
        double center = pageWidth / 2.0;
        double lineCenter = (line.BoundingBox.Left + line.BoundingBox.Right) / 2.0;
        return lineCenter < center;
    }

    public static List<PdfParagraph> GetPageReadingOrder(List<PdfParagraph> pageList, double pageWidth)
    {
        double center = pageWidth / 2.0;
        var left = pageList.Where(p => p.X0 + p.Width / 2 < center).OrderByDescending(p => p.Y1).ToList();
        var right = pageList.Where(p => p.X0 + p.Width / 2 >= center).OrderByDescending(p => p.Y1).ToList();
        var result = new List<PdfParagraph>(left.Count + right.Count);
        result.AddRange(left);
        result.AddRange(right);
        return result;
    }
}

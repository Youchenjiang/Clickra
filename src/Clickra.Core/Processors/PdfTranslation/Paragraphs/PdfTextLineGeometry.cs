using System.Linq;
using UglyToad.PdfPig.DocumentLayoutAnalysis;

namespace Clickra.Core.Processors
{
    internal static class PdfTextLineGeometry
    {
        public static bool HasColumnGap(TextLine line, double minGap = 20.0)
        {
            if (line == null || line.Words.Count <= 1) return false;
            var sortedWords = line.Words.OrderBy(w => w.BoundingBox.Left).ToList();
            for (int i = 0; i < sortedWords.Count - 1; i++)
            {
                double gap = sortedWords[i + 1].BoundingBox.Left - sortedWords[i].BoundingBox.Right;
                if (gap >= minGap) return true;
            }
            return false;
        }
    }
}

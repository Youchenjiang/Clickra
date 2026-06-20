using Clickra.Core.Models;
using UglyToad.PdfPig.DocumentLayoutAnalysis;

namespace Clickra.Core.Processors
{
    internal static class PdfTextLineMerger
    {
        public static IReadOnlyList<TextLine> MergeHorizontalLines(IReadOnlyList<TextLine> initialLines)
        {
            if (initialLines == null || initialLines.Count <= 1) return initialLines ?? new List<TextLine>();

            var groups = new List<List<TextLine>>();
            foreach (var line in initialLines.OrderByDescending(l => l.BoundingBox.Centroid.Y))
            {
                bool added = false;
                foreach (var g in groups)
                {
                    double avgY = g.Average(l => l.BoundingBox.Centroid.Y);
                    if (Math.Abs(line.BoundingBox.Centroid.Y - avgY) < 3.5)
                    {
                        g.Add(line);
                        added = true;
                        break;
                    }
                }
                if (!added)
                {
                    groups.Add(new List<TextLine> { line });
                }
            }

            var result = new List<TextLine>();
            foreach (var g in groups)
            {
                if (g.Count == 1)
                {
                    result.Add(g[0]);
                }
                else
                {
                    var sortedGroup = g.OrderBy(l => l.BoundingBox.Left).ToList();
                    var allWords = sortedGroup.SelectMany(l => l.Words).OrderBy(w => w.BoundingBox.Left).ToList();
                    var mergedLine = new TextLine(allWords, " ");
                    result.Add(mergedLine);
                }
            }

            return result.OrderByDescending(l => l.BoundingBox.Centroid.Y).ToList();
        }
    }
}

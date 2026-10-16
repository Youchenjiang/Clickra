using System;
using System.Collections.Generic;

namespace Clickra.Core.Modules.PdfSplit.Application;

/// <summary>
/// Application gateway for constructing page-range specifications from visual split parameters.
/// Decouples presentation models from processing implementation details.
/// </summary>
public static class PdfSplitSpecBuilder
{
    /// <summary>
    /// Builds a page-range specification string ("1-3; 5; 7-9") from a split mode and its
    /// parameters. Mode 0 uses <paramref name="customSegments"/> verbatim, mode 1 splits
    /// every page ("all"), and mode 2 groups pages into fixed-size chunks.
    /// </summary>
    /// <param name="mode">Split mode: 0 = custom segments, 1 = split every page, 2 = fixed pages per segment.</param>
    /// <param name="nPages">Pages per segment when <paramref name="mode"/> is 2.</param>
    /// <param name="totalPages">Total number of pages in the source document.</param>
    /// <param name="customSegments">User-defined segments (1-based inclusive ranges) for mode 0.</param>
    /// <returns>A ";"-separated range spec, or "all" when every page is split individually.</returns>
    public static string BuildSegmentSpec(int mode, int nPages, int totalPages, IReadOnlyList<(int Start, int End)> customSegments)
    {
        if (mode == 1) return "all";

        if (mode == 2)
        {
            int n = Math.Max(1, nPages);
            var segs = new List<string>();
            for (int start = 1; start <= totalPages; start += n)
            {
                int end = Math.Min(totalPages, start + n - 1);
                segs.Add(start == end ? $"{start}" : $"{start}-{end}");
            }
            return string.Join("; ", segs);
        }

        if (customSegments.Count == 0) return "all";

        var specs = new List<string>();
        foreach (var seg in customSegments)
        {
            if (seg.Start == seg.End) specs.Add($"{seg.Start}");
            else specs.Add($"{seg.Start}-{seg.End}");
        }
        return string.Join("; ", specs);
    }
}

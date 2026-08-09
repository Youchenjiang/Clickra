using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Processors;

/// <summary>
/// Splits a single PDF file into multiple output documents based on a page-range
/// specification, or writes one output file per page when the spec is "all" or "each".
/// </summary>
public class PdfSplitProcessor : SingleFileProcessorBase
{
    /// <summary>Returns the file-name suffix appended to split output files.</summary>
    protected override string GetOutputSuffix() => "_split.pdf";

    /// <summary>
    /// Reads the input PDF, parses the "pages" option and writes the resulting
    /// segment documents, reporting progress through <paramref name="onProgress"/>.
    /// </summary>
    /// <param name="fullPath">Path of the input PDF.</param>
    /// <param name="targetOutputPath">Path of the single-segment output document; a base
    /// name for multi-segment or split-each outputs.</param>
    /// <param name="fileIndex">Index of this file among all processed files (0-based).</param>
    /// <param name="totalFiles">Total number of files being processed.</param>
    /// <param name="options">Processor options; the "pages" key holds the page-range spec.</param>
    /// <param name="onProgress">Optional progress callback receiving (progress, max, message).</param>
    /// <param name="cancellationToken">Cancellation token to abort the split mid-way.</param>
    protected override void ProcessSingleFile(string fullPath, string targetOutputPath, int fileIndex, int totalFiles, Dictionary<string, object>? options, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
    {
        string pagesSpec = "all";
        if (options?.TryGetValue("pages", out var pObj) == true && pObj is string pStr && !string.IsNullOrWhiteSpace(pStr))
        {
            pagesSpec = pStr.Trim();
        }

        int progressBase = ProgressCalculator.GetProgressBase(fileIndex);
        int totalProgressMax = ProgressCalculator.GetProgressMax(totalFiles);

        onProgress?.Invoke(progressBase + 10, totalProgressMax, "正在讀取 PDF 檔案...");

        using var inDoc = PdfReader.Open(fullPath, PdfDocumentOpenMode.Import);
        int pageCount = inDoc.PageCount;
        if (pageCount == 0)
        {
            throw new InvalidOperationException("PDF 檔案不包含任何頁面。");
        }

        string baseDir = Path.GetDirectoryName(targetOutputPath) ?? ClickraStorage.GetOutputDir(fullPath);
        string baseFileName = Path.GetFileNameWithoutExtension(fullPath);

        bool splitEach = pagesSpec.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                        pagesSpec.Equals("each", StringComparison.OrdinalIgnoreCase);

        if (splitEach)
        {
            for (int i = 0; i < pageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int progress = progressBase + 10 + (int)((i + 1) * 85.0 / pageCount);
                onProgress?.Invoke(progress, totalProgressMax, $"正在拆分第 {i + 1}/{pageCount} 頁...");

                using var outDoc = new PdfDocument();
                outDoc.AddPage(inDoc.Pages[i]);
                string outPath = Path.Combine(baseDir, $"{baseFileName}_page_{(i + 1):D3}.pdf");
                outDoc.Save(outPath);
            }
        }
        else
        {
            string[] segments = pagesSpec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length > 1)
            {
                for (int s = 0; s < segments.Length; s++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string segSpec = segments[s];
                    List<int> targetPages = ParsePageRange(segSpec, pageCount);
                    if (targetPages.Count == 0) continue;

                    int progress = progressBase + 10 + (int)((s + 1) * 85.0 / segments.Length);
                    onProgress?.Invoke(progress, totalProgressMax, $"正在提取區段 {s + 1}/{segments.Length} ({targetPages[0]}-{targetPages[^1]}頁)...");

                    using var outDoc = new PdfDocument();
                    foreach (int pageNum in targetPages)
                    {
                        outDoc.AddPage(inDoc.Pages[pageNum - 1]);
                    }

                    string outPath = Path.Combine(baseDir, $"{baseFileName}_{targetPages[0]}-{targetPages[^1]}.pdf");
                    outDoc.Save(outPath);
                }
            }
            else
            {
                List<int> targetPages = ParsePageRange(pagesSpec, pageCount);
                if (targetPages.Count == 0)
                {
                    throw new ArgumentException($"指定的分頁範圍「{pagesSpec}」無效或超出頁數範圍 (1-{pageCount})。");
                }

                onProgress?.Invoke(progressBase + 40, totalProgressMax, $"正在提取指定頁面 ({targetPages.Count} 頁)...");

                using var outDoc = new PdfDocument();
                foreach (int pageNum in targetPages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    outDoc.AddPage(inDoc.Pages[pageNum - 1]);
                }

                onProgress?.Invoke(progressBase + 90, totalProgressMax, "正在儲存檔案...");
                outDoc.Save(targetOutputPath);
            }
        }

        onProgress?.Invoke(progressBase + 100, totalProgressMax, "PDF 分割完成！");
    }

    /// <summary>
    /// Returns the number of pages in the PDF at <paramref name="fullPath"/>, or 0 when the
    /// file cannot be opened.
    /// </summary>
    /// <param name="fullPath">Path of the PDF file.</param>
    /// <returns>The page count, or 0 on failure.</returns>
    public static int GetPageCount(string fullPath)
    {
        try
        {
            using var inDoc = PdfReader.Open(fullPath, PdfDocumentOpenMode.Import);
            return inDoc.PageCount;
        }
        catch
        {
            return 0;
        }
    }

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

    /// <summary>
    /// Parses a page-range specification such as "1-3", "1, 5, 8-10" or "12-15" into a sorted
    /// list of 1-based page numbers, clamped to [1, <paramref name="totalPages"/>].
    /// </summary>
    /// <param name="spec">Comma-separated single pages and inclusive ranges.</param>
    /// <param name="totalPages">Total number of pages in the source document.</param>
    /// <returns>Sorted, de-duplicated page numbers; empty when nothing is in range.</returns>
    public static List<int> ParsePageRange(string spec, int totalPages)
    {
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(spec)) return result.ToList();

        string[] parts = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                string[] range = part.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (range.Length == 2 && int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end))
                {
                    int min = Math.Max(1, Math.Min(start, end));
                    int max = Math.Min(totalPages, Math.Max(start, end));
                    for (int p = min; p <= max; p++)
                    {
                        result.Add(p);
                    }
                }
            }
            else if (int.TryParse(part, out int singlePage) && singlePage >= 1 && singlePage <= totalPages)
            {
                result.Add(singlePage);
            }
        }

        return result.OrderBy(x => x).ToList();
    }
}

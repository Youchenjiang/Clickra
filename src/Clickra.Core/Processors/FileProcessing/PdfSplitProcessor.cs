using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Processors
{
    public class PdfSplitProcessor : SingleFileProcessorBase
    {
        protected override string GetOutputSuffix() => "_split.pdf";

        protected override void ProcessSingleFile(string fullPath, string targetOutputPath, int fileIndex, int totalFiles, Dictionary<string, object>? options, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
        {
            string pagesSpec = "all";
            if (options != null && options.TryGetValue("pages", out var pObj) && pObj is string pStr && !string.IsNullOrWhiteSpace(pStr))
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

            onProgress?.Invoke(progressBase + 100, totalProgressMax, "PDF 分割完成！");
        }

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
                else if (int.TryParse(part, out int singlePage))
                {
                    if (singlePage >= 1 && singlePage <= totalPages)
                    {
                        result.Add(singlePage);
                    }
                }
            }

            return result.OrderBy(x => x).ToList();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Processors
{
    public class PdfMergeProcessor : IFileProcessor
    {
        public void Process(List<string> files, string? outputPath, Dictionary<string, object>? options = null, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("Output path is required for PDF merge.");

            int total = files.Count;
            using var outDoc = new PdfDocument();
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { ClickraStorage.SetActiveRecordIndex(i); } catch { }
                var f = files[i];
                onProgress?.Invoke((i * 100) + 50, total * 100, $"正在合併: {Path.GetFileName(f)} ({i + 1}/{total})...");
                using var inDoc = PdfReader.Open(f, PdfDocumentOpenMode.Import);
                for (int j = 0; j < inDoc.PageCount; j++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    outDoc.AddPage(inDoc.Pages[j]);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(total * 100, total * 100, "合併完成，正在儲存檔案...");
            outDoc.Save(outputPath);
        }
    }
}

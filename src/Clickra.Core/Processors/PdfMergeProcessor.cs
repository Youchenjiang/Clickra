using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Processors
{
    public class PdfMergeProcessor : MultiFileProcessorBase
    {
        private PdfDocument? _outDoc;
        private string? _outputPath;

        public void Process(List<string> files, string? outputPath, Dictionary<string, object>? options = null, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("Output path is required for PDF merge.");
            _outputPath = outputPath;
            _outDoc = new PdfDocument();
            try
            {
                base.Process(files, outputPath, options, onProgress, cancellationToken);
            }
            finally
            {
                _outDoc?.Dispose();
            }
        }

        protected override void ProcessFile(string filePath, int fileIndex, int totalFiles, Dictionary<string, object>? options, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
        {
            onProgress?.Invoke((fileIndex * 100) + 50, totalFiles * 100, $"正在合併: {Path.GetFileName(filePath)} ({fileIndex + 1}/{totalFiles})...");
            using var inDoc = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            for (int j = 0; j < inDoc.PageCount; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _outDoc!.AddPage(inDoc.Pages[j]);
            }
        }

        protected override void OnAllFilesProcessed(string? outputPath, int totalFiles, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(totalFiles * 100, totalFiles * 100, "合併完成，正在儲存檔案...");
            _outDoc!.Save(outputPath);
        }
    }
}

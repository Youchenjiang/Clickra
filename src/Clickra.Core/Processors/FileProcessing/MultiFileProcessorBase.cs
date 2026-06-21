using System;
using System.Collections.Generic;
using System.Threading;

namespace Clickra.Core.Processors
{
    public abstract class MultiFileProcessorBase : IFileProcessor
    {
        public void Process(List<string> files, string? outputPath, Dictionary<string, object>? options = null, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            if (files == null || files.Count == 0) return;

            int total = files.Count;
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { ClickraStorage.SetActiveRecordIndex(i); } catch { }

                ProcessFile(files[i], i, total, options, onProgress, cancellationToken);
            }

            OnAllFilesProcessed(outputPath, total, onProgress, cancellationToken);
        }

        protected abstract void ProcessFile(string filePath, int fileIndex, int totalFiles, Dictionary<string, object>? options, Action<int, int, string>? onProgress, CancellationToken cancellationToken);

        protected virtual void OnAllFilesProcessed(string? outputPath, int totalFiles, Action<int, int, string>? onProgress, CancellationToken cancellationToken) { }
    }
}

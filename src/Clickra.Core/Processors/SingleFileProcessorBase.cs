using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Clickra.Core.Processors
{
    public abstract class SingleFileProcessorBase : IFileProcessor
    {
        public void Process(List<string> files, string? outputPath, Dictionary<string, object>? options = null, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default)
        {
            if (files == null || files.Count == 0) return;
            int total = files.Count;
            
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { ClickraStorage.SetActiveRecordIndex(i); } catch { }
                
                var filePath = files[i];
                string fullPath = Path.GetFullPath(filePath);
                string outDir = ClickraStorage.GetOutputDir(fullPath);
                
                // If outputPath parameter is provided and this is the first (and only) file, use it.
                // Otherwise calculate a sensible default output path.
                string targetOutputPath = (files.Count == 1 && !string.IsNullOrEmpty(outputPath)) 
                    ? outputPath 
                    : Path.Combine(outDir, Path.GetFileNameWithoutExtension(filePath) + GetDefaultOutputExtension());

                ProcessSingleFile(fullPath, targetOutputPath, i, total, options, onProgress, cancellationToken);
            }
        }

        protected abstract string GetDefaultOutputExtension();

        protected abstract void ProcessSingleFile(string fullPath, string targetOutputPath, int fileIndex, int totalFiles, Dictionary<string, object>? options, Action<int, int, string>? onProgress, CancellationToken cancellationToken);
    }
}

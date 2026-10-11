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
            Action<int>? onFileStarting = ProcessorExecutionOptions.ReadFileStartingCallback(options);
            IReadOnlyList<string>? outputPaths = ProcessorExecutionOptions.ReadOutputPaths(options);
            
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { onFileStarting?.Invoke(i); } catch { }
                
                var filePath = files[i];
                string fullPath = Path.GetFullPath(filePath);
                if (!File.Exists(fullPath))
                {
                    onProgress?.Invoke(i + 1, total, $"跳過已不存在的檔案: {Path.GetFileName(filePath)}");
                    continue;
                }

                // If outputPath parameter is provided and this is the first (and only) file, use it.
                // Otherwise use a caller-planned output when available, then fall back to the source directory.
                string targetOutputPath = outputPaths is not null && i < outputPaths.Count
                    ? outputPaths[i]
                    : (files.Count == 1 && !string.IsNullOrEmpty(outputPath))
                        ? outputPath
                        : Path.Combine(
                            Path.GetDirectoryName(fullPath) ?? ".",
                            Path.GetFileNameWithoutExtension(filePath) + GetOutputSuffix());

                using var backupScope = FileBackupScope.Create(targetOutputPath);
                ProcessSingleFile(fullPath, targetOutputPath, i, total, options, onProgress, cancellationToken);
                backupScope.Commit();
            }
        }

        protected abstract string GetOutputSuffix();

        protected abstract void ProcessSingleFile(string fullPath, string targetOutputPath, int fileIndex, int totalFiles, Dictionary<string, object>? options, Action<int, int, string>? onProgress, CancellationToken cancellationToken);
    }
}

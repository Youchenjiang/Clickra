using System;
using System.Collections.Generic;
using System.Threading;

namespace Clickra.Core.Processors
{
    public interface IFileProcessor
    {
        void Process(List<string> files, string? outputPath, Dictionary<string, object>? options = null, Action<int, int, string>? onProgress = null, CancellationToken cancellationToken = default);
    }
}

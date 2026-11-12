using System;
using System.Threading;
using Clickra.Core.Modules.Doc.Shared.Application;

namespace Clickra.Core.Modules.Doc.Shared.Infrastructure;

/// <summary>
/// Defines an execution strategy for converting Office documents to PDF.
/// </summary>
internal interface IOfficeConversionStrategy
{
    string EngineName { get; }
    bool CanConvert(string appType);
    bool IsReady(string appType, OfficeConversionSettings settings);
    void ConvertToPdf(
        OfficeConversionRequest request,
        OfficeConversionSettings settings,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken);
}

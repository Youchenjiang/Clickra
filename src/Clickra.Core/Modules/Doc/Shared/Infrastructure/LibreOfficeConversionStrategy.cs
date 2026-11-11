using System;
using System.Threading;
using Clickra.Core.Modules.Doc.Shared.Application;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Doc.Shared.Infrastructure;

/// <summary>
/// Headless LibreOffice CLI conversion strategy supporting cross-platform PDF export.
/// </summary>
internal sealed class LibreOfficeConversionStrategy : IOfficeConversionStrategy
{
    public string EngineName => ClickraSettings.OfficeEngineLibreOffice;

    public bool CanConvert(string appType) => LibreOfficeHelper.CanConvert(appType);

    public bool IsReady(string appType, OfficeConversionSettings settings) =>
        !string.IsNullOrWhiteSpace(LibreOfficeHelper.GetResolvedExecutablePath(
            settings.LibreOfficeRemovalPendingRestart,
            settings.LibreOfficePath));

    public void ConvertToPdf(
        OfficeConversionRequest request,
        OfficeConversionSettings settings,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        LibreOfficeHelper.ExportToPdf(request, settings, onProgress, cancellationToken);
    }
}

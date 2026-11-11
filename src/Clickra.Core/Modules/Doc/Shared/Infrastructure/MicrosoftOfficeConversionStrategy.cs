using System;
using System.Threading;
using Clickra.Core.Modules.Doc.Shared.Application;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Doc.Shared.Infrastructure;

/// <summary>
/// Windows-only Microsoft Office COM interop conversion strategy.
/// </summary>
internal sealed class MicrosoftOfficeConversionStrategy : IOfficeConversionStrategy
{
    public string EngineName => ClickraSettings.OfficeEngineMicrosoft;

    public bool CanConvert(string appType) =>
        OperatingSystem.IsWindows() && (appType is "Word" or "PowerPoint" or "Excel");

    public bool IsReady(string appType, OfficeConversionSettings settings) =>
        OperatingSystem.IsWindows() && OfficeEngineDetector.IsMicrosoftOfficeReady(appType);

    public void ConvertToPdf(
        OfficeConversionRequest request,
        OfficeConversionSettings settings,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Microsoft Office COM conversion is only supported on Windows.");
        }

        PowerShellHelper.ExportMicrosoftOfficeToPdf(request, settings.Language, onProgress, cancellationToken);
    }
}

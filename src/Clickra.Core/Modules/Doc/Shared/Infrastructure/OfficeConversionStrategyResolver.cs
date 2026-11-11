using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Threading;
using Clickra.Core;
using Clickra.Core.Modules.Doc.Shared.Application;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Doc.Shared.Infrastructure;

/// <summary>
/// Resolves and executes Office to PDF conversion strategies with fallback support.
/// </summary>
internal sealed class OfficeConversionStrategyResolver
{
    private readonly IOfficeConversionStrategy _microsoftStrategy;
    private readonly IOfficeConversionStrategy _libreOfficeStrategy;

    public OfficeConversionStrategyResolver(
        IOfficeConversionStrategy? microsoftStrategy = null,
        IOfficeConversionStrategy? libreOfficeStrategy = null)
    {
        _microsoftStrategy = microsoftStrategy ?? new MicrosoftOfficeConversionStrategy();
        _libreOfficeStrategy = libreOfficeStrategy ?? new LibreOfficeConversionStrategy();
    }

    public static OfficeConversionStrategyResolver Default { get; } = new();

    public IOfficeConversionStrategy MicrosoftStrategy => _microsoftStrategy;
    public IOfficeConversionStrategy LibreOfficeStrategy => _libreOfficeStrategy;

    public void ExecuteConversion(
        OfficeConversionRequest request,
        OfficeConversionSettings settings,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        string appType = request.AppType;
        string engine = settings.Engine;

        if (engine.Equals("libreoffice", StringComparison.OrdinalIgnoreCase))
        {
            _libreOfficeStrategy.ConvertToPdf(request, settings, onProgress, cancellationToken);
            return;
        }

        if (!engine.Equals("microsoft", StringComparison.OrdinalIgnoreCase) &&
            _libreOfficeStrategy.CanConvert(appType) &&
            !_microsoftStrategy.IsReady(appType, settings))
        {
            if (!_libreOfficeStrategy.IsReady(appType, settings))
            {
                throw new InvalidOperationException(Localization.T("error_libreoffice_not_ready", settings.Language));
            }

            _libreOfficeStrategy.ConvertToPdf(request, settings, onProgress, cancellationToken);
            return;
        }

        try
        {
            _microsoftStrategy.ConvertToPdf(request, settings, onProgress, cancellationToken);
        }
        catch (Exception ex) when (
            IsRecoverableMicrosoftOfficeFailure(ex) &&
            ShouldFallBackToLibreOffice(engine, appType, cancellationToken))
        {
            ExecuteLibreOfficeFallback(ex, request, settings, onProgress, cancellationToken);
        }
    }

    private void ExecuteLibreOfficeFallback(
        Exception originalException,
        OfficeConversionRequest request,
        OfficeConversionSettings settings,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        if (!_libreOfficeStrategy.IsReady(request.AppType, settings))
        {
            ExceptionDispatchInfo.Capture(originalException).Throw();
        }

        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke(
            request.FileIndex * 100,
            request.TotalFiles * 100,
            string.Format(
                Localization.T("status_office_fallback_to_libreoffice", settings.Language),
                request.AppType,
                Path.GetFileName(request.FullPath)));
        cancellationToken.ThrowIfCancellationRequested();
        _libreOfficeStrategy.ConvertToPdf(request, settings, onProgress, cancellationToken);
    }

    public static bool ShouldFallBackToLibreOffice(
        string engine,
        string appType,
        CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        !engine.Equals("microsoft", StringComparison.OrdinalIgnoreCase) &&
        LibreOfficeHelper.CanConvert(appType);

    public static bool IsRecoverableMicrosoftOfficeFailure(Exception exception) =>
        exception is InvalidOperationException
            or NotSupportedException
            or TimeoutException
            or Win32Exception
            or IOException
            or UnauthorizedAccessException
            or SecurityException;
}

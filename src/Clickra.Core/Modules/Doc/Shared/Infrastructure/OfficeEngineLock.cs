using System;
using System.Threading;
using Clickra.Core;
using Clickra.Core.Modules.Doc.Shared.Application;

namespace Clickra.Core.Modules.Doc.Shared.Infrastructure;

/// <summary>
/// Cross-process named mutex lock for Office engines that cannot run concurrent conversions
/// safely (Microsoft PowerPoint COM and LibreOffice headless). Word and Excel launch
/// isolated processes and do not require locking.
/// </summary>
internal static class OfficeEngineLock
{
    internal const string PowerPointMutexName = "Local\\ClickraEngineLock_PowerPoint_v1";
    internal const string LibreOfficeMutexName = "Local\\ClickraEngineLock_LibreOffice_v1";

    internal static IDisposable AcquirePowerPoint(
        OfficeConversionRequest request,
        string language,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken) =>
        AcquireForRequest(PowerPointMutexName, "PowerPoint", request, language, onProgress, cancellationToken);

    internal static IDisposable AcquireLibreOffice(
        OfficeConversionRequest request,
        string language,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken) =>
        AcquireForRequest(LibreOfficeMutexName, "LibreOffice", request, language, onProgress, cancellationToken);

    internal static IDisposable AcquireForRequest(
        string mutexName,
        string engineDisplayName,
        OfficeConversionRequest request,
        string language,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken) =>
        Acquire(
            mutexName,
            () => onProgress?.Invoke(
                request.FileIndex * 100,
                request.TotalFiles * 100,
                Localization.Format("status_office_waiting_engine", language, ("engine", engineDisplayName))),
            cancellationToken);

    internal static IDisposable Acquire(
        string mutexName,
        Action? onWaiting = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mutex = new Mutex(false, mutexName);
        bool acquired = false;
        try
        {
            acquired = TryAcquireImmediate(mutex);
            if (!acquired)
            {
                onWaiting?.Invoke();
                acquired = WaitForMutexOrCancel(mutex, cancellationToken);
            }

            return new LockLease(mutex);
        }
        catch
        {
            if (acquired)
            {
                try { mutex.ReleaseMutex(); }
                catch { /* Best-effort release on failure */ }
            }
            mutex.Dispose();
            throw;
        }
    }

    private static bool TryAcquireImmediate(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static bool WaitForMutexOrCancel(Mutex mutex, CancellationToken cancellationToken)
    {
        WaitHandle[] handles = [mutex, cancellationToken.WaitHandle];
        try
        {
            int signaled = WaitHandle.WaitAny(handles);
            if (signaled == 1)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            return signaled == 0;
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private sealed class LockLease : IDisposable
    {
        private readonly Mutex _mutex;
        private int _disposed;

        public LockLease(Mutex mutex) => _mutex = mutex;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _mutex.ReleaseMutex(); }
            catch { /* Best-effort release */ }
            finally { _mutex.Dispose(); }
        }
    }
}

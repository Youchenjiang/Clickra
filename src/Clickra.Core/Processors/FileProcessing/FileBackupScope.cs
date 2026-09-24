using System;
using System.IO;

namespace Clickra.Core.Processors;

/// <summary>
/// Protects an existing output file while a processor replaces it.
/// Disposing without Commit restores the original bytes after failure or cancellation.
/// </summary>
public sealed class FileBackupScope : IDisposable
{
    private readonly string _targetPath;
    private readonly string? _backupPath;
    private bool _completed;
    private bool _disposed;

    private FileBackupScope(string targetPath, string? backupPath)
    {
        _targetPath = targetPath;
        _backupPath = backupPath;
    }

    public static FileBackupScope Create(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
        {
            return new FileBackupScope(targetPath, null);
        }

        string tempBackup = targetPath + $".clickra_bak_{Guid.NewGuid():N}";
        // An existing output must never be overwritten without a usable recovery copy.
        // Let copy failures abort processing instead of silently creating an unprotected scope.
        File.Copy(targetPath, tempBackup, overwrite: true);
        return new FileBackupScope(targetPath, tempBackup);
    }

    public void Commit()
    {
        if (_completed) return;

        // The replacement itself is already complete. Mark the transaction complete before
        // cleanup so a cleanup failure cannot make Dispose restore stale bytes over good output.
        _completed = true;
        if (_backupPath != null && File.Exists(_backupPath))
        {
            try
            {
                File.Delete(_backupPath);
            }
            catch (Exception ex)
            {
                throw new IOException(
                    $"Output replacement succeeded, but the recovery backup could not be removed: {_backupPath}",
                    ex);
            }
        }
    }

    public void Rollback()
    {
        if (_completed) return;

        if (_backupPath == null || !File.Exists(_backupPath))
        {
            _completed = true;
            return;
        }

        try
        {
            File.Copy(_backupPath, _targetPath, overwrite: true);
        }
        catch (Exception ex)
        {
            // Keep the recovery copy in place so the original bytes remain recoverable.
            throw new IOException(
                $"Failed to restore the original output. Recovery backup retained at: {_backupPath}",
                ex);
        }

        // The original bytes are back in place. Do not retry restoration if only cleanup fails.
        _completed = true;
        try
        {
            File.Delete(_backupPath);
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Original output was restored, but the recovery backup could not be removed: {_backupPath}",
                ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            if (!_completed && _backupPath != null)
            {
                Rollback();
            }
        }
        finally
        {
            _disposed = true;
        }
    }
}

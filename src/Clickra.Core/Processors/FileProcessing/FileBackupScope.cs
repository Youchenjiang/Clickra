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
    private bool _committed;
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
        try
        {
            File.Copy(targetPath, tempBackup, overwrite: true);
            return new FileBackupScope(targetPath, tempBackup);
        }
        catch
        {
            return new FileBackupScope(targetPath, null);
        }
    }

    public void Commit()
    {
        _committed = true;
        if (_backupPath != null && File.Exists(_backupPath))
        {
            try { File.Delete(_backupPath); } catch { }
        }
    }

    public void Rollback()
    {
        if (_backupPath != null && File.Exists(_backupPath))
        {
            try
            {
                File.Copy(_backupPath, _targetPath, overwrite: true);
                File.Delete(_backupPath);
            }
            catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_committed && _backupPath != null)
        {
            Rollback();
        }
    }
}

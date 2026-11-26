using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core.Application;
using Clickra.Core.Storage;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
namespace Clickra.Core.Modules.Pdf.Decrypt.Application;

using File = System.IO.File;

public enum PdfUnlockStatus
{
    NotEncrypted,
    Success,
    Skipped
}

public sealed class PdfUnlockResult : IDisposable
{
    private readonly string? _tempFilePath;
    private bool _disposed;

    public PdfUnlockResult(
        string effectivePath,
        PdfUnlockStatus status,
        bool wasEncrypted,
        bool hadOpenPassword,
        string? tempFilePath = null)
    {
        EffectivePath = effectivePath;
        Status = status;
        WasEncrypted = wasEncrypted;
        HadOpenPassword = hadOpenPassword;
        _tempFilePath = tempFilePath;
    }

    public string EffectivePath { get; }
    public PdfUnlockStatus Status { get; }
    public bool WasEncrypted { get; }
    public bool HadOpenPassword { get; }
    public bool IsSkipped => Status == PdfUnlockStatus.Skipped;
    public bool IsTempFile => !string.IsNullOrEmpty(_tempFilePath);

    public static PdfUnlockResult Unencrypted(string path) =>
        new(path, PdfUnlockStatus.NotEncrypted, wasEncrypted: false, hadOpenPassword: false);

    public static PdfUnlockResult Unlocked(string tempPath, bool hadOpenPassword) =>
        new(tempPath, PdfUnlockStatus.Success, wasEncrypted: true, hadOpenPassword: hadOpenPassword, tempFilePath: tempPath);

    public static PdfUnlockResult Skipped(string path, bool hadOpenPassword) =>
        new(path, PdfUnlockStatus.Skipped, wasEncrypted: true, hadOpenPassword: hadOpenPassword);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!string.IsNullOrEmpty(_tempFilePath) && File.Exists(_tempFilePath))
        {
            try
            {
                File.Delete(_tempFilePath);
            }
            catch
            {
                // Best-effort cleanup of unlocked temp files.
            }
        }
    }
}

public static class PdfUnlockStep
{
    public static async Task<PdfUnlockResult> UnlockAsync(
        int fileIndex,
        string inputPath,
        IConversionInteraction interaction,
        ICollection<string>? knownPasswords = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(inputPath) ||
            !inputPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
            !PdfEncryptionQuery.IsEncrypted(inputPath))
        {
            return PdfUnlockResult.Unencrypted(inputPath);
        }

        Directory.CreateDirectory(StorageRuntime.UnlockTempDirectory);
        string tempPath = Path.Combine(StorageRuntime.UnlockTempDirectory, $"{Guid.NewGuid():N}.pdf");

        try
        {
            // Case 2: Only usage restrictions (no open password needed)
            if (!PdfEncryptionQuery.NeedsOpenPassword(inputPath))
            {
                if (TryDecryptToTemp(inputPath, password: null, tempPath))
                {
                    return PdfUnlockResult.Unlocked(tempPath, hadOpenPassword: false);
                }
            }

            // Case 3: Needs open password
            // 3.1: Try known passwords in order
            if (knownPasswords != null)
            {
                foreach (string knownPassword in knownPasswords)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryDecryptToTemp(inputPath, knownPassword, tempPath))
                    {
                        return PdfUnlockResult.Unlocked(tempPath, hadOpenPassword: true);
                    }
                }
            }

            // 3.2 - 3.4: Prompt user
            bool isRetry = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? password = await interaction.RequestPasswordAsync(
                    fileIndex,
                    inputPath,
                    isRetry,
                    cancellationToken).ConfigureAwait(false);

                if (password is null)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                if (TryDecryptToTemp(inputPath, password, tempPath))
                {
                    if (knownPasswords != null && !knownPasswords.Contains(password))
                    {
                        knownPasswords.Add(password);
                    }
                    return PdfUnlockResult.Unlocked(tempPath, hadOpenPassword: true);
                }

                isRetry = true;
            }
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch { /* Best-effort cleanup on failure */ }
            }
            throw;
        }
    }

    public static void CleanResidualFiles(TimeSpan? olderThan = null)
    {
        try
        {
            string unlockDir = StorageRuntime.UnlockTempDirectory;
            if (!Directory.Exists(unlockDir)) return;
            TimeSpan maxAge = olderThan ?? TimeSpan.FromDays(1);
            DateTime cutoff = DateTime.UtcNow - maxAge;
            foreach (string file in Directory.GetFiles(unlockDir, "*.pdf"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                        File.Delete(file);
                }
                catch
                {
                    // Best effort
                }
            }
        }
        catch
        {
            // Best effort
        }
    }

    private static bool TryDecryptToTemp(string sourcePath, string? password, string tempPath)
    {
        try
        {
            using var inDoc = string.IsNullOrEmpty(password)
                ? PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import)
                : PdfReader.Open(sourcePath, password, PdfDocumentOpenMode.Import);

            using var outDoc = new PdfDocument();
            int pageCount = inDoc.PageCount;
            for (int i = 0; i < pageCount; i++)
            {
                outDoc.AddPage(inDoc.Pages[i]);
            }
            outDoc.Save(tempPath);
            return true;
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch { /* Best effort */ }
            }
            return false;
        }
    }
}

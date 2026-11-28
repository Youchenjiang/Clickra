using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        ConversionPasswordCache? passwordCache,
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
            if (!PdfEncryptionQuery.NeedsOpenPassword(inputPath) &&
                TryDecryptToTemp(inputPath, password: null, tempPath))
            {
                return PdfUnlockResult.Unlocked(tempPath, hadOpenPassword: false);
            }

            // Case 3: Needs open password
            if (TryUnlockWithKnownPasswords(inputPath, tempPath, passwordCache, cancellationToken))
            {
                return PdfUnlockResult.Unlocked(tempPath, hadOpenPassword: true);
            }

            return await PromptAndUnlockAsync(
                fileIndex,
                inputPath,
                tempPath,
                interaction,
                passwordCache,
                cancellationToken).ConfigureAwait(false);
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

    public static async Task<PdfUnlockResult> UnlockAsync(
        int fileIndex,
        string inputPath,
        IConversionInteraction interaction,
        ICollection<string>? knownPasswords = null,
        CancellationToken cancellationToken = default)
    {
        var cache = new ConversionPasswordCache();
        if (knownPasswords != null)
        {
            foreach (var p in knownPasswords) cache.Add(p);
        }
        var res = await UnlockAsync(fileIndex, inputPath, interaction, cache, cancellationToken).ConfigureAwait(false);
        if (knownPasswords != null)
        {
            foreach (var p in cache.GetSnapshot().Where(p => !knownPasswords.Contains(p)))
            {
                knownPasswords.Add(p);
            }
        }
        return res;
    }

    private static bool TryUnlockWithKnownPasswords(
        string inputPath,
        string tempPath,
        ConversionPasswordCache? passwordCache,
        CancellationToken cancellationToken)
    {
        if (passwordCache == null) return false;
        foreach (string knownPassword in passwordCache.GetSnapshot())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryDecryptToTemp(inputPath, knownPassword, tempPath))
            {
                return true;
            }
        }
        return false;
    }

    private static async Task<PdfUnlockResult> PromptAndUnlockAsync(
        int fileIndex,
        string inputPath,
        string tempPath,
        IConversionInteraction interaction,
        ConversionPasswordCache? passwordCache,
        CancellationToken cancellationToken)
    {
        bool isRetry = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PasswordPromptResult promptResult = await interaction.RequestPasswordResultAsync(
                fileIndex,
                inputPath,
                isRetry,
                cancellationToken).ConfigureAwait(false);

            if (promptResult.IsCancel)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (promptResult.IsSkip)
            {
                return PdfUnlockResult.Skipped(inputPath, hadOpenPassword: true);
            }

            string? password = promptResult.Password;
            if (!string.IsNullOrEmpty(password) && TryDecryptToTemp(inputPath, password, tempPath))
            {
                passwordCache?.Add(password);
                return PdfUnlockResult.Unlocked(tempPath, hadOpenPassword: true);
            }

            isRetry = true;
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
                catch (Exception ex)
                {
                    Diagnostics.ClickraLog.Warn("Cleanup", $"Failed to delete residual unlock file {Path.GetFileName(file)}", ex);
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
            outDoc.Info.Title = inDoc.Info.Title;
            outDoc.Info.Author = inDoc.Info.Author;
            outDoc.Info.Subject = inDoc.Info.Subject;
            outDoc.Info.Keywords = inDoc.Info.Keywords;
            outDoc.Info.Creator = inDoc.Info.Creator;
            outDoc.PageLayout = inDoc.PageLayout;
            outDoc.PageMode = inDoc.PageMode;

            int pageCount = inDoc.PageCount;
            for (int i = 0; i < pageCount; i++)
            {
                outDoc.AddPage(inDoc.Pages[i]);
            }

            if (pageCount > 0)
            {
                CopyOutlines(inDoc, outDoc);
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

    private static void CopyOutlines(PdfDocument source, PdfDocument target)
    {
        if (source.Outlines.Count == 0) return;
        var pageIndexMap = new Dictionary<PdfPage, int>();
        for (int i = 0; i < source.PageCount; i++)
        {
            pageIndexMap[source.Pages[i]] = i;
        }
        CopyOutlineCollection(source.Outlines, target.Outlines, target, pageIndexMap);
    }

    private static void CopyOutlineCollection(
        PdfOutlineCollection sourceOutlines,
        PdfOutlineCollection targetOutlines,
        PdfDocument targetDoc,
        Dictionary<PdfPage, int> pageIndexMap)
    {
        foreach (PdfOutline src in sourceOutlines)
        {
            PdfPage destPage = src.DestinationPage is not null &&
                               pageIndexMap.TryGetValue(src.DestinationPage, out int idx) &&
                               idx < targetDoc.PageCount
                ? targetDoc.Pages[idx]
                : targetDoc.Pages[0];
            PdfOutline dst = targetOutlines.Add(src.Title ?? string.Empty, destPage, src.Opened);
            if (src.Outlines.Count > 0)
            {
                CopyOutlineCollection(src.Outlines, dst.Outlines, targetDoc, pageIndexMap);
            }
        }
    }
}

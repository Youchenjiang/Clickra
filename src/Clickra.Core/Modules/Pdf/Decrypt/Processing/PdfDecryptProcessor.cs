using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Clickra.Core.Processors;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Modules.Pdf.Decrypt.Processing;

public class PdfDecryptProcessor : SingleFileProcessorBase
{
    /// <summary>
    /// Checks whether the specified PDF file has encryption or password protection.
    /// Returns false if the file can be opened and has no security handler.
    /// </summary>
    public static bool IsEncrypted(string filePath)
    {
        if (!System.IO.File.Exists(filePath)) return false;
        try
        {
            using var doc = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            return doc.SecurityHandler != null && doc.SecurityHandler.Elements.Count > 0;
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True when the file cannot be opened without a password (an open password). A PDF that only restricts
    /// printing or copying opens without one, so it needs no password to decrypt.
    /// </summary>
    public static bool NeedsOpenPassword(string filePath)
    {
        if (!System.IO.File.Exists(filePath)) return false;
        try
        {
            using (PdfReader.Open(filePath, PdfDocumentOpenMode.Import))
            {
                return false;
            }
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    protected override string GetOutputSuffix() => "_decrypted.pdf";

    protected override void ProcessSingleFile(string fullPath, string targetOutputPath, int fileIndex, int totalFiles, Dictionary<string, object>? options, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
    {
        int progressBase = ProgressCalculator.GetProgressBase(fileIndex);
        int totalProgressMax = ProgressCalculator.GetProgressMax(totalFiles);
        string lang = ExecutionLocalizationOptions.ReadLanguage(options);

        onProgress?.Invoke(progressBase + 20, totalProgressMax, ExecutionLocalizationOptions.Text(lang, "core_progress_reading_pdf"));

        using var inDoc = OpenInputDocument(fullPath, options);

        if (inDoc.SecurityHandler == null || inDoc.SecurityHandler.Elements.Count == 0)
        {
            throw new InvalidOperationException(Localization.T("pdf_not_encrypted", lang));
        }

        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke(progressBase + 50, totalProgressMax, ExecutionLocalizationOptions.Text(lang, "core_decrypt_progress_removing"));

        using var outDoc = new PdfDocument();
        int pageCount = inDoc.PageCount;
        for (int i = 0; i < pageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(progressBase + 50 + (int)(i * 40.0 / pageCount), totalProgressMax, ExecutionLocalizationOptions.Text(lang, "core_progress_page", ("current", i + 1), ("total", pageCount)));
            outDoc.AddPage(inDoc.Pages[i]);
        }

        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke(progressBase + 95, totalProgressMax, ExecutionLocalizationOptions.Text(lang, "core_progress_saving"));
        outDoc.Save(targetOutputPath);
        onProgress?.Invoke(progressBase + 100, totalProgressMax, ExecutionLocalizationOptions.Text(lang, "core_decrypt_done"));
    }

    private static PdfDocument OpenInputDocument(string fullPath, Dictionary<string, object>? options)
    {
        string? password = options?.TryGetValue("password", out var pwdObj) == true ? pwdObj as string : null;
        return string.IsNullOrEmpty(password)
            ? PdfReader.Open(fullPath, PdfDocumentOpenMode.Import)
            : PdfReader.Open(fullPath, password, PdfDocumentOpenMode.Import);
    }
}

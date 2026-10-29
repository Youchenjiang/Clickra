using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Clickra.Core.Processors;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Modules.PdfDecrypt.Processing;

public class PdfDecryptProcessor : SingleFileProcessorBase
{
    /// <summary>
    /// Checks whether the specified PDF file has encryption or password protection.
    /// Returns false if the file can be opened and has no security handler.
    /// </summary>
    public static bool IsEncrypted(string filePath)
    {
        if (!File.Exists(filePath)) return false;
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

    protected override string GetOutputSuffix() => "_decrypted.pdf";

    protected override void ProcessSingleFile(string fullPath, string targetOutputPath, int fileIndex, int totalFiles, Dictionary<string, object>? options, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
    {
        string password = "";
        if (options?.TryGetValue("password", out var pwdObj) == true && pwdObj is string pwdStr)
        {
            password = pwdStr;
        }

        int progressBase = ProgressCalculator.GetProgressBase(fileIndex);
        int totalProgressMax = ProgressCalculator.GetProgressMax(totalFiles);

        onProgress?.Invoke(progressBase + 20, totalProgressMax, "正在讀取 PDF 檔案...");

        using var inDoc = string.IsNullOrEmpty(password)
            ? PdfReader.Open(fullPath, PdfDocumentOpenMode.Import)
            : PdfReader.Open(fullPath, password, PdfDocumentOpenMode.Import);

        if (inDoc.SecurityHandler == null || inDoc.SecurityHandler.Elements.Count == 0)
        {
            string lang = ExecutionLocalizationOptions.ReadLanguage(options);
            throw new InvalidOperationException(Localization.T("pdf_not_encrypted", lang));
        }

        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke(progressBase + 50, totalProgressMax, "正在去除密碼與限制...");

        using var outDoc = new PdfDocument();
        int pageCount = inDoc.PageCount;
        for (int i = 0; i < pageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(progressBase + 50 + (int)(i * 40.0 / pageCount), totalProgressMax, $"正在處理第 {i + 1}/{pageCount} 頁...");
            outDoc.AddPage(inDoc.Pages[i]);
        }

        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke(progressBase + 95, totalProgressMax, "正在儲存檔案...");
        outDoc.Save(targetOutputPath);
        onProgress?.Invoke(progressBase + 100, totalProgressMax, "密碼已成功移除！");
    }
}

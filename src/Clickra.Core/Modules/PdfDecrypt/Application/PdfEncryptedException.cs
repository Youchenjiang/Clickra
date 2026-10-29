using System;

namespace Clickra.Core.Modules.PdfDecrypt.Application;

/// <summary>
/// Domain exception thrown when a PDF document cannot be processed because it is encrypted or password-protected.
/// </summary>
public class PdfEncryptedException : InvalidOperationException
{
    public string ResourceKey { get; }
    public string? FilePath { get; }

    public PdfEncryptedException(string resourceKey, string? filePath = null, string? message = null, Exception? innerException = null)
        : base(message ?? resourceKey, innerException)
    {
        ResourceKey = resourceKey;
        FilePath = filePath;
    }
}

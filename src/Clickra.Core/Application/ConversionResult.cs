namespace Clickra.Core.Application;

/// <summary>Application-level outcome returned to presentation code.</summary>
public sealed record ConversionResult(
    ConversionResultStatus Status,
    IReadOnlyList<string> Outputs,
    string? Error,
    TimeSpan Duration,
    int CompletedFiles,
    string TaskId,
    ConversionFailureKind FailureKind = ConversionFailureKind.None,
    IReadOnlyList<SkippedFileInfo>? SkippedFiles = null)
{
    public IReadOnlyList<SkippedFileInfo> SkippedFiles { get; init; } = SkippedFiles ?? Array.Empty<SkippedFileInfo>();
}

public enum ConversionResultStatus
{
    Succeeded,
    Canceled,
    Parked,
    Failed
}

public enum ConversionFailureKind
{
    None,
    FileNotFound,
    DirectoryNotFound
}

public static class ConversionFailureClassifier
{
    public static ConversionFailureKind Classify(Exception exception) => exception switch
    {
        FileNotFoundException => ConversionFailureKind.FileNotFound,
        DirectoryNotFoundException => ConversionFailureKind.DirectoryNotFound,
        _ => ConversionFailureKind.None
    };
}

/// <summary>
/// Maps raw technical/library exceptions into user-facing localized messages for UI and history,
/// while full developer diagnostics remain in <see cref="Diagnostics.ClickraLog"/>.
/// </summary>
public static class ConversionUserErrorMapper
{
    public static string ToUserMessage(Exception exception, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Exception effective = exception is AggregateException agg && agg.InnerExceptions.Count > 0
            ? agg.Flatten().InnerExceptions[0]
            : exception;

        if (HasExceptionInChain<FileNotFoundException>(effective) ||
            HasExceptionInChain<DirectoryNotFoundException>(effective))
        {
            return Translate("error_file_not_found", language);
        }

        if (HasExceptionInChain<UnauthorizedAccessException>(effective))
        {
            return Translate("error_access_denied", language);
        }

        if (IsCorruptedPdfException(effective))
        {
            return Translate("error_pdf_corrupted", language);
        }

        if (HasExceptionInChain<IOException>(effective))
        {
            return ContainsTechnicalJargon(effective.Message)
                ? Translate("error_access_denied", language)
                : effective.Message.Trim();
        }

        if (IsRawTechnicalExceptionType(effective) || ContainsTechnicalJargon(effective.Message))
        {
            return Translate("error_unexpected_failure", language);
        }

        return string.IsNullOrWhiteSpace(effective.Message)
            ? Translate("error_unexpected_failure", language)
            : effective.Message.Trim();
    }

    public static string ToUserFacingMessage(string? rawMessage, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return string.Empty;

        string trimmed = rawMessage.Trim();

        if (IsCorruptedPdfMessage(trimmed))
            return Translate("error_pdf_corrupted", language);

        if (trimmed.Contains("Could not find file", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Could not find a part of the path", StringComparison.OrdinalIgnoreCase))
        {
            return Translate("error_file_not_found", language);
        }

        if (trimmed.Contains("Access to the path", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("being used by another process", StringComparison.OrdinalIgnoreCase))
        {
            return Translate("error_access_denied", language);
        }

        if (ContainsTechnicalJargon(trimmed))
            return Translate("error_unexpected_failure", language);

        return trimmed;
    }

    private static string Translate(string key, string? language) =>
        string.IsNullOrWhiteSpace(language) ? Localization.T(key) : Localization.T(key, language);

    private static bool HasExceptionInChain<T>(Exception ex) where T : Exception
    {
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is T)
                return true;
        }

        return false;
    }

    private static bool IsCorruptedPdfException(Exception ex)
    {
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            string typeName = cur.GetType().FullName ?? cur.GetType().Name;
            if (typeName.Contains("PdfReader", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("PdfSharp", StringComparison.OrdinalIgnoreCase) ||
                IsCorruptedPdfMessage(cur.Message))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCorruptedPdfMessage(string message) =>
        message.Contains("PDFsharp", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Unexpected token", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("cannot-open-pdf", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Position marked with", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("not a valid PDF", StringComparison.OrdinalIgnoreCase);

    private static bool IsRawTechnicalExceptionType(Exception ex)
    {
        string typeName = ex.GetType().Name;
        return typeName is nameof(NullReferenceException)
            or nameof(IndexOutOfRangeException)
            or nameof(ArgumentException)
            or nameof(ArgumentNullException)
            or nameof(ArgumentOutOfRangeException)
            or nameof(InvalidCastException)
            or nameof(FormatException)
            or nameof(OverflowException)
            or nameof(NotSupportedException);
    }

    private static bool ContainsTechnicalJargon(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("System.", StringComparison.Ordinal) ||
               message.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("   at ", StringComparison.Ordinal) ||
               message.Contains("〈LF〉", StringComparison.Ordinal) ||
               message.Contains("0x80", StringComparison.OrdinalIgnoreCase);
    }
}

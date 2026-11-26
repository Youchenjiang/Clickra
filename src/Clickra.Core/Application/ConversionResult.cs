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

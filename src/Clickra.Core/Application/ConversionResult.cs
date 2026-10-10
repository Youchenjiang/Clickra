namespace Clickra.Core.Application;

/// <summary>Application-level outcome returned to presentation code.</summary>
public sealed record ConversionResult(
    ConversionResultStatus Status,
    IReadOnlyList<string> Outputs,
    string? Error,
    TimeSpan Duration,
    int CompletedFiles,
    string TaskId);

public enum ConversionResultStatus
{
    Succeeded,
    Canceled,
    Parked,
    Failed
}

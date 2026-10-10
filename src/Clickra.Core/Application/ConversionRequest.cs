namespace Clickra.Core.Application;

/// <summary>Immutable application input for a conversion use case.</summary>
public sealed record ConversionRequest(
    string Command,
    IReadOnlyList<string> InputFiles,
    IReadOnlyDictionary<string, object>? Options = null,
    string? ExistingTaskId = null,
    string? OutputOverride = null);

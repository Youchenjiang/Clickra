namespace Clickra.Core.Application;

/// <summary>Validated and normalized conversion plan consumed by application execution.</summary>
public sealed record ConversionPlan(
    string Command,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs,
    IReadOnlySet<ConversionCapability> RequiredCapabilities,
    IReadOnlyDictionary<string, object> NormalizedOptions,
    int ResumeStartIndex = 0,
    string? ExistingTaskId = null,
    bool BestEffortTaskPersistence = false);

/// <summary>UI-independent interaction capabilities a conversion use case can require.</summary>
public enum ConversionCapability
{
    Password,
    SplitPages,
    MarkdownOptions
}

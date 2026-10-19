using Clickra.Core.Modules.HistoryTasks.Application;

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
    bool BestEffortTaskPersistence = false,
    bool TrackTaskLifecycle = true,
    ITaskLifecycleRepository? TaskLifecycleRepository = null)
{
    /// <summary>Backwards-compatible constructor for existing compiled binaries.</summary>
    public ConversionPlan(
        string command,
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs,
        IReadOnlySet<ConversionCapability> requiredCapabilities,
        IReadOnlyDictionary<string, object> normalizedOptions,
        int resumeStartIndex,
        string? existingTaskId,
        bool bestEffortTaskPersistence,
        bool trackTaskLifecycle)
        : this(
            command,
            inputs,
            outputs,
            requiredCapabilities,
            normalizedOptions,
            resumeStartIndex,
            existingTaskId,
            bestEffortTaskPersistence,
            trackTaskLifecycle,
            null)
    {
    }
}

/// <summary>UI-independent interaction capabilities a conversion use case can require.</summary>
public enum ConversionCapability
{
    Password,
    SplitPages,
    MarkdownOptions
}

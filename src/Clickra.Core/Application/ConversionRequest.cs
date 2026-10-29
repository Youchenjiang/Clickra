using Clickra.Core.Modules.HistoryTasks.Application;

namespace Clickra.Core.Application;

/// <summary>Immutable application input for a conversion use case.</summary>
public sealed record ConversionRequest(
    string Command,
    IReadOnlyList<string> InputFiles,
    IReadOnlyDictionary<string, object>? Options = null,
    string? ExistingTaskId = null,
    string? OutputOverride = null,
    bool BestEffortTaskPersistence = false,
    bool TrackTaskLifecycle = true,
    ITaskLifecycleRepository? TaskLifecycleRepository = null)
{
    public const string LanguageOptionKey = "ui_language";

    /// <summary>Backwards-compatible constructor for existing compiled binaries.</summary>
    public ConversionRequest(
        string command,
        IReadOnlyList<string> inputFiles,
        IReadOnlyDictionary<string, object>? options,
        string? existingTaskId,
        string? outputOverride,
        bool bestEffortTaskPersistence,
        bool trackTaskLifecycle)
        : this(
            command,
            inputFiles,
            options,
            existingTaskId,
            outputOverride,
            bestEffortTaskPersistence,
            trackTaskLifecycle,
            null)
    {
    }
}

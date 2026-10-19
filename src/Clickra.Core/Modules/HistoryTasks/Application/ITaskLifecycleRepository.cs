using System.Collections.Generic;

namespace Clickra.Core.Modules.HistoryTasks.Application;

/// <summary>Request parameters for completing a tracked task in the repository.</summary>
public sealed record TaskCompletionRequest
{
    public required string StartTime { get; init; }
    public required bool IsSuccess { get; init; }
    public required string ErrorMessage { get; init; }
    public string? EndTime { get; init; }
    public long ElapsedMs { get; init; }
    public string? InputPaths { get; init; }
    public string? OutputPath { get; init; }
}

/// <summary>
/// Contract for task lifecycle persistence and status coordination.
/// Decouples application execution from the static Storage implementation.
/// </summary>
public interface ITaskLifecycleRepository
{
    string Start(string command, int fileCount, string? inputPaths = null);
    void SetInProgress(string taskId);
    void SetIndex(string taskId, int index);
    void Complete(string taskId, string command, TaskCompletionRequest request);
    void Park(string taskId, string reason, int nextIndex);
    void Delete(string taskId);
}

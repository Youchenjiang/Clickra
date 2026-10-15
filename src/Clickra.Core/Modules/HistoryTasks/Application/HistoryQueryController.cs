namespace Clickra.Core.Modules.HistoryTasks.Application;

public enum HistoryQueryStatus
{
    Pending,
    InProgress,
    Parked,
    Success,
    Failed
}

public sealed record HistoryQueryRecord(
    string Id,
    string Time,
    string Command,
    int FileCount,
    HistoryQueryStatus Status,
    string ErrorMessage,
    string EndTime,
    long ElapsedMs,
    string InputPaths,
    string OutputPath,
    int CurrentIndex,
    int Pid,
    int? ParkedRetentionDays,
    string ParkedSince);

public readonly record struct ParkedRetentionQueryState(
    bool IsUnlimited,
    int RemainingDays,
    TimeSpan RemainingTime,
    bool IsExpiringSoon,
    bool HasExpired,
    bool IsTaskOverride);

/// <summary>Application query boundary for task/history persistence facts.</summary>
public static class HistoryQueryController
{
    public static IReadOnlyList<HistoryQueryRecord> GetActive() =>
        ClickraStorage.GetActiveTasks().Select(Snapshot).ToArray();

    public static IReadOnlyList<HistoryQueryRecord> GetParked() =>
        ClickraStorage.GetParkedTasks().Select(Snapshot).ToArray();

    public static IReadOnlyList<HistoryQueryRecord> GetCompleted(int limit) =>
        ClickraStorage.GetHistory(limit).Select(Snapshot).ToArray();

    public static ParkedRetentionQueryState GetParkedRetention(string taskId)
    {
        ClickraStorage.ParkedRetentionInfo state = ClickraStorage.GetParkedRetentionInfo(taskId);
        return new ParkedRetentionQueryState(
            state.IsUnlimited,
            state.RemainingDays,
            state.RemainingTime,
            state.IsExpiringSoon,
            state.HasExpired,
            state.IsTaskOverride);
    }

    public static bool IsUserCanceledReason(string? errorMessage) => ClickraStorage.IsUserCanceledReason(errorMessage);

    private static HistoryQueryRecord Snapshot(ClickraStorage.HistoryEntry entry) => new(
        entry.Id ?? string.Empty,
        entry.Time ?? string.Empty,
        entry.Command ?? string.Empty,
        entry.FileCount,
        entry.Status switch
        {
            ConversionStatus.Pending => HistoryQueryStatus.Pending,
            ConversionStatus.InProgress => HistoryQueryStatus.InProgress,
            ConversionStatus.Parked => HistoryQueryStatus.Parked,
            ConversionStatus.Success => HistoryQueryStatus.Success,
            _ => HistoryQueryStatus.Failed
        },
        entry.ErrorMessage ?? string.Empty,
        entry.EndTime ?? string.Empty,
        entry.ElapsedMs,
        entry.InputPaths ?? string.Empty,
        entry.OutputPath ?? string.Empty,
        entry.CurrentIndex,
        entry.Pid,
        entry.ParkedRetentionDays,
        entry.ParkedSince ?? string.Empty);
}

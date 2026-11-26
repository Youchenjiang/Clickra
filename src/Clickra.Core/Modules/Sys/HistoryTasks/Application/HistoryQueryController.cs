namespace Clickra.Core.Modules.Sys.HistoryTasks.Application;

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
    string ParkedSince)
{
    public bool IsSuccess => Status == HistoryQueryStatus.Success;
}

/// <summary>Application query boundary for task/history persistence facts.</summary>
public static class HistoryQueryController
{
    public static IReadOnlyList<HistoryQueryRecord> GetActive() =>
        ClickraStorage.GetActiveTasks().Select(Snapshot).ToArray();

    public static IReadOnlyList<HistoryQueryRecord> GetParked() =>
        ClickraStorage.GetParkedTasks().Select(Snapshot).ToArray();

    public static IReadOnlyList<HistoryQueryRecord> GetCompleted(int limit) =>
        ClickraStorage.GetHistory(limit).Select(Snapshot).ToArray();

    public static bool IsUserCanceledReason(string? errorMessage) => ClickraStorage.IsUserCanceledReason(errorMessage);

    private static HistoryQueryRecord Snapshot(HistoryEntry entry) => new(
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
        entry.ParkedSince ?? string.Empty);
}

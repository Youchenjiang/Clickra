using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clickra.Core.Storage;

/// <summary>Canonical reason identifiers and predicates for task terminal states.</summary>
public static class StorageTaskReasons
{
    public const string Canceled = "Canceled";
    public const string Abandoned = "Abandoned";
    public const string LegacyUserAborted = "User Aborted";

    public static bool IsUserCanceled(string? errorMessage) =>
        errorMessage is not null &&
        (errorMessage.Equals(Canceled, StringComparison.Ordinal) ||
         errorMessage.Equals(LegacyUserAborted, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Task entry as recorded in the history log and task queue files.</summary>
public struct HistoryEntry
{
    public string Id { get; set; }
    public string Time { get; set; }
    public string Command { get; set; }
    public int FileCount { get; set; }
    public ConversionStatus Status { get; set; }
    public bool IsSuccess => Status == ConversionStatus.Success;
    public string ErrorMessage { get; set; }
    public string EndTime { get; set; }
    public long ElapsedMs { get; set; }
    public string InputPaths { get; set; }
    public string OutputPath { get; set; }
    public int CurrentIndex { get; set; }
    public int Pid { get; set; }
    public int? ParkedRetentionDays { get; set; }
    public string ParkedSince { get; set; }
}

/// <summary>Request contract for completing a task execution.</summary>
public record CompleteTaskRequest
{
    public string StartTime { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string ErrorMsg { get; init; } = string.Empty;
    public string? EndTime { get; init; }
    public long ElapsedMs { get; init; } = -1;
    public string? InputPaths { get; init; }
    public string? OutputPath { get; init; }
}

/// <summary>Resume data returned when claiming a parked task.</summary>
public record ResumedTaskInfo(string TaskId, string Command, List<string> Files, int StartIndex);

/// <summary>Calculated retention status for a parked task.</summary>
public readonly record struct ParkedRetentionInfo(
    bool IsUnlimited,
    int RemainingDays,
    TimeSpan RemainingTime,
    bool IsExpiringSoon,
    bool HasExpired,
    bool IsTaskOverride = false);

internal sealed record TaskFileData
{
    internal string TaskId { get; init; } = string.Empty;
    internal string Command { get; init; } = string.Empty;
    internal int FileCount { get; init; }
    internal ConversionStatus Status { get; init; }
    internal string ErrorMessage { get; init; } = string.Empty;
    internal string? Time { get; init; }
    internal string? InputPaths { get; init; }
    internal int CurrentIndex { get; init; }
    internal string? OutputPath { get; init; }
    internal string? EndTime { get; init; }
    internal long ElapsedMs { get; init; } = -1;
    internal int Pid { get; init; }
    internal int? ParkedRetentionDays { get; init; }
    internal string? ParkedSince { get; init; }
}

/// <summary>Owns the file format and filesystem details for task-*.tmp records.</summary>
internal static class TaskRecordStore
{
    internal static string PathFor(string taskId) =>
        Path.Combine(StorageRuntime.TasksDirectory, $"{StorageRuntime.TaskFilePrefix}{taskId}.tmp");

    internal static string CreateTaskId() =>
        $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString("N")[..8]}";

    internal static void EnsureDirectory()
    {
        try { Directory.CreateDirectory(StorageRuntime.TasksDirectory); }
        catch
        {
            // Callers handle unavailable storage through the existing read/write fallbacks.
        }
    }

    internal static List<string> ListTaskIds()
    {
        try
        {
            if (!Directory.Exists(StorageRuntime.TasksDirectory)) return new List<string>();
            return Directory.GetFiles(StorageRuntime.TasksDirectory, $"{StorageRuntime.TaskFilePrefix}*.tmp")
                .OrderByDescending(GetTaskFileSortKey)
                .Select(file => Path.GetFileNameWithoutExtension(file)[StorageRuntime.TaskFilePrefix.Length..])
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    internal static bool Write(TaskFileData data)
    {
        lock (StorageRuntime.FileLock)
        {
            try
            {
                using var writer = new StreamWriter(PathFor(data.TaskId), false, System.Text.Encoding.UTF8);
                writer.WriteLine($"Id={data.TaskId}");
                writer.WriteLine($"Time={data.Time ?? DateTime.UtcNow.ToString(StorageRuntime.DateTimeFormat)}");
                writer.WriteLine($"Command={data.Command}");
                writer.WriteLine($"FileCount={data.FileCount}");
                writer.WriteLine($"Status={data.Status}");
                writer.WriteLine($"ErrorMessage={(data.ErrorMessage ?? "").Replace("\r", " ").Replace("\n", " ")}");
                writer.WriteLine($"InputPaths={(data.InputPaths ?? "").Replace("\r", " ").Replace("\n", " ")}");
                writer.WriteLine($"CurrentIndex={data.CurrentIndex}");
                writer.WriteLine($"OutputPath={(data.OutputPath ?? "").Replace("\r", " ").Replace("\n", " ")}");
                writer.WriteLine($"EndTime={data.EndTime ?? ""}");
                writer.WriteLine($"ElapsedMs={data.ElapsedMs}");
                writer.WriteLine($"Pid={data.Pid}");
                if (data.ParkedRetentionDays.HasValue) writer.WriteLine($"ParkedRetentionDays={data.ParkedRetentionDays.Value}");
                if (!string.IsNullOrEmpty(data.ParkedSince)) writer.WriteLine($"ParkedSince={data.ParkedSince}");
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static HistoryEntry? Read(string taskId)
    {
        lock (StorageRuntime.FileLock)
        {
            string path = PathFor(taskId);
            if (!File.Exists(path)) return null;
            try
            {
                Dictionary<string, string> values = StorageRuntime.ParseKeyValueLines(File.ReadAllLines(path));
                if (!int.TryParse(values.GetValueOrDefault("FileCount", "0"), out int fileCount)) fileCount = 0;
                if (!Enum.TryParse(values.GetValueOrDefault("Status", "Pending"), out ConversionStatus status)) status = ConversionStatus.Pending;
                if (!int.TryParse(values.GetValueOrDefault("CurrentIndex", "0"), out int currentIndex)) currentIndex = 0;
                if (!long.TryParse(values.GetValueOrDefault("ElapsedMs", "-1"), out long elapsedMs)) elapsedMs = -1;
                if (!int.TryParse(values.GetValueOrDefault("Pid", "0"), out int pid)) pid = 0;
                int? parkedDays = int.TryParse(values.GetValueOrDefault("ParkedRetentionDays", ""), out int parsedDays) ? parsedDays : null;

                return new HistoryEntry
                {
                    Id = values.GetValueOrDefault("Id", taskId),
                    Time = values.GetValueOrDefault("Time", ""),
                    Command = values.GetValueOrDefault("Command", ""),
                    FileCount = fileCount,
                    Status = status,
                    ErrorMessage = values.GetValueOrDefault("ErrorMessage", ""),
                    InputPaths = values.GetValueOrDefault("InputPaths", ""),
                    CurrentIndex = currentIndex,
                    OutputPath = values.GetValueOrDefault("OutputPath", ""),
                    EndTime = values.GetValueOrDefault("EndTime", ""),
                    ElapsedMs = elapsedMs,
                    Pid = pid,
                    ParkedRetentionDays = parkedDays,
                    ParkedSince = values.GetValueOrDefault("ParkedSince", "")
                };
            }
            catch
            {
                return null;
            }
        }
    }

    internal static TaskFileData FromEntry(
        HistoryEntry entry,
        ConversionStatus status,
        string? errorMessage = null,
        string? endTime = null,
        long elapsedMs = -1,
        int? pidOverride = null) => new()
    {
        TaskId = entry.Id,
        Command = entry.Command,
        FileCount = entry.FileCount,
        Status = status,
        ErrorMessage = errorMessage ?? entry.ErrorMessage,
        Time = entry.Time,
        InputPaths = entry.InputPaths,
        CurrentIndex = entry.CurrentIndex,
        OutputPath = entry.OutputPath,
        EndTime = endTime ?? entry.EndTime,
        ElapsedMs = elapsedMs >= 0 ? elapsedMs : entry.ElapsedMs,
        Pid = pidOverride ?? entry.Pid,
        ParkedRetentionDays = entry.ParkedRetentionDays,
        ParkedSince = entry.ParkedSince
    };

    private static string GetTaskFileSortKey(string filePath)
    {
        string name = Path.GetFileNameWithoutExtension(filePath);
        string id = name.Length > StorageRuntime.TaskFilePrefix.Length ? name[StorageRuntime.TaskFilePrefix.Length..] : name;
        string timestamp = id.Length >= 17 ? id[..17] : id;
        return timestamp.PadRight(17, '0');
    }
}

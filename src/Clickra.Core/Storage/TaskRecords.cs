using System;
using System.Collections.Generic;

namespace Clickra.Core.Storage;

public enum ConversionStatus
{
    Pending,
    InProgress,
    Parked,
    Success,
    Failed
}

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

internal sealed record LegacyActiveRecordCompletion
{
    internal string Command { get; init; } = string.Empty;
    internal string StartTime { get; init; } = string.Empty;
    internal bool IsSuccess { get; init; }
    internal string ErrorMessage { get; init; } = string.Empty;
    internal string? EndTime { get; init; }
    internal long ElapsedMs { get; init; }
    internal string? InputPaths { get; init; }
    internal string? OutputPath { get; init; }
}

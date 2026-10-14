using System;
using System.Collections.Generic;
using System.IO;

namespace Clickra.Core.Storage;

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

/// <summary>Owns the legacy singleton active.tmp compatibility format and lifecycle.</summary>
internal static class LegacyActiveRecordStore
{
    private static string ActiveFilePath => Path.Combine(StorageRuntime.DataDirectory, "active.tmp");

    internal static void Start(string command, int fileCount, string? inputPaths) =>
        StorageRuntime.RunWithMutex(() => Write(command, fileCount, ConversionStatus.Pending, "", null, inputPaths));

    internal static void SetInProgress()
    {
        StorageRuntime.RunWithMutex(() =>
        {
            ClickraStorage.HistoryEntry? entry = Read();
            if (entry.HasValue)
            {
                Write(
                    entry.Value.Command,
                    entry.Value.FileCount,
                    ConversionStatus.InProgress,
                    "",
                    entry.Value.Time,
                    entry.Value.InputPaths);
            }
        });
    }

    internal static void SetIndex(int index)
    {
        StorageRuntime.RunWithMutex(() =>
        {
            ClickraStorage.HistoryEntry? entry = Read();
            if (entry.HasValue)
            {
                Write(
                    entry.Value.Command,
                    entry.Value.FileCount,
                    entry.Value.Status,
                    entry.Value.ErrorMessage,
                    entry.Value.Time,
                    entry.Value.InputPaths,
                    index);
            }
        });
    }

    internal static void Complete(LegacyActiveRecordCompletion completion)
    {
        StorageRuntime.RunWithMutex(() =>
        {
            lock (StorageRuntime.FileLock)
            {
                try
                {
                    string error = HistoryStore.Sanitize(completion.ErrorMessage);
                    string completedAt = completion.EndTime ?? DateTime.UtcNow.ToString(StorageRuntime.DateTimeFormat);
                    string inputs = HistoryStore.Sanitize(completion.InputPaths);
                    string output = HistoryStore.Sanitize(completion.OutputPath);
                    string[] inputList = inputs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                    int currentIndex = 0;
                    try
                    {
                        ClickraStorage.HistoryEntry? activeEntry = Read();
                        if (activeEntry.HasValue) currentIndex = activeEntry.Value.CurrentIndex;
                    }
                    catch
                    {
                        // Missing or unreadable legacy state leaves the completion index at zero.
                    }

                    if (File.Exists(ActiveFilePath)) File.Delete(ActiveFilePath);

                    Write(
                        completion.Command,
                        inputList.Length,
                        completion.IsSuccess ? ConversionStatus.Success : ConversionStatus.Failed,
                        error,
                        completion.StartTime,
                        inputs,
                        currentIndex);

                    HistoryStore.AppendCompleted(new ClickraStorage.HistoryEntry
                    {
                        Time = completion.StartTime,
                        Command = completion.Command,
                        FileCount = inputList.Length,
                        Status = completion.IsSuccess ? ConversionStatus.Success : ConversionStatus.Failed,
                        ErrorMessage = error,
                        EndTime = completedAt,
                        ElapsedMs = completion.ElapsedMs,
                        InputPaths = inputs,
                        OutputPath = output
                    });
                }
                catch
                {
                    // Legacy compatibility writes remain best effort.
                }
            }
        });
    }

    internal static void Clear()
    {
        StorageRuntime.RunWithMutex(() =>
        {
            lock (StorageRuntime.FileLock)
            {
                try
                {
                    if (File.Exists(ActiveFilePath)) File.Delete(ActiveFilePath);
                }
                catch
                {
                    // Cleanup is best effort if another process holds the compatibility file.
                }
            }
        });
    }

    internal static ClickraStorage.HistoryEntry? Get() => StorageRuntime.RunWithMutex(Read);

    private static void Write(
        string command,
        int fileCount,
        ConversionStatus status,
        string errorMessage,
        string? time = null,
        string? inputPaths = null,
        int currentIndex = 0)
    {
        lock (StorageRuntime.FileLock)
        {
            try
            {
                using var writer = new StreamWriter(ActiveFilePath, false, System.Text.Encoding.UTF8);
                writer.WriteLine($"Time={time ?? DateTime.UtcNow.ToString(StorageRuntime.DateTimeFormat)}");
                writer.WriteLine($"Command={command}");
                writer.WriteLine($"FileCount={fileCount}");
                writer.WriteLine($"Status={status}");
                writer.WriteLine($"ErrorMessage={(errorMessage ?? "").Replace("\r", " ").Replace("\n", " ")}");
                writer.WriteLine($"InputPaths={(inputPaths ?? "").Replace("\r", " ").Replace("\n", " ")}");
                writer.WriteLine($"CurrentIndex={currentIndex}");
            }
            catch
            {
                // The legacy mirror is optional; a write failure must not interrupt conversions.
            }
        }
    }

    private static ClickraStorage.HistoryEntry? Read()
    {
        lock (StorageRuntime.FileLock)
        {
            if (!File.Exists(ActiveFilePath)) return null;
            try
            {
                Dictionary<string, string> values = StorageRuntime.ParseKeyValueLines(File.ReadAllLines(ActiveFilePath));
                if (!int.TryParse(values.GetValueOrDefault("FileCount", "0"), out int fileCount)) fileCount = 0;
                if (!Enum.TryParse(values.GetValueOrDefault("Status", "Pending"), out ConversionStatus status)) status = ConversionStatus.Pending;
                if (!int.TryParse(values.GetValueOrDefault("CurrentIndex", "0"), out int currentIndex)) currentIndex = 0;

                return new ClickraStorage.HistoryEntry
                {
                    Time = values.GetValueOrDefault("Time", ""),
                    Command = values.GetValueOrDefault("Command", ""),
                    FileCount = fileCount,
                    Status = status,
                    ErrorMessage = values.GetValueOrDefault("ErrorMessage", ""),
                    InputPaths = values.GetValueOrDefault("InputPaths", ""),
                    CurrentIndex = currentIndex
                };
            }
            catch
            {
                return null;
            }
        }
    }
}

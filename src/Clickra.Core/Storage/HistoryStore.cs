using System;
using System.Collections.Generic;
using System.IO;

namespace Clickra.Core.Storage;

/// <summary>Owns history.log parsing, append format, and deletion.</summary>
internal static class HistoryStore
{
    internal static List<ClickraStorage.HistoryEntry> Read(int limit)
    {
        return StorageRuntime.RunWithMutex(() =>
        {
            var entries = new List<ClickraStorage.HistoryEntry>();
            lock (StorageRuntime.FileLock)
            {
                if (!File.Exists(StorageRuntime.HistoryFilePath)) return entries;
                try
                {
                    string[] lines = File.ReadAllLines(StorageRuntime.HistoryFilePath);
                    int start = Math.Max(0, lines.Length - limit);
                    for (int index = lines.Length - 1; index >= start; index--)
                    {
                        string line = lines[index];
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        string[] parts = line.Split('|');
                        if (parts.Length < 4) continue;

                        _ = int.TryParse(parts[2], out int count);
                        bool success = parts[3].Equals("Success", StringComparison.OrdinalIgnoreCase);
                        entries.Add(new ClickraStorage.HistoryEntry
                        {
                            Time = parts[0],
                            Command = parts[1],
                            FileCount = count,
                            Status = success ? ConversionStatus.Success : ConversionStatus.Failed,
                            ErrorMessage = parts.Length > 4 ? parts[4] : "",
                            EndTime = parts.Length > 5 ? parts[5] : parts[0],
                            ElapsedMs = parts.Length > 6 && long.TryParse(parts[6], out long elapsed) ? elapsed : -1,
                            InputPaths = parts.Length > 7 ? parts[7] : "",
                            OutputPath = parts.Length > 8 ? parts[8] : ""
                        });
                    }
                }
                catch
                {
                }
            }
            return entries;
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
                    if (File.Exists(StorageRuntime.HistoryFilePath)) File.Delete(StorageRuntime.HistoryFilePath);
                }
                catch
                {
                }
            }
        });
    }

    internal static void AppendCompleted(
        string startTime,
        string command,
        int fileCount,
        ConversionStatus status,
        string errorMessage,
        string endTime,
        long elapsedMs,
        string inputPaths,
        string outputPath)
    {
        lock (StorageRuntime.FileLock)
        {
            string line = $"{startTime}|{command}|{fileCount}|{status}|{errorMessage}|{endTime}|{elapsedMs}|{inputPaths}|{outputPath}";
            File.AppendAllText(StorageRuntime.HistoryFilePath, line + Environment.NewLine, System.Text.Encoding.UTF8);
        }
    }

    internal static void AppendCanceled(ClickraStorage.HistoryEntry entry, string reason, DateTime now)
    {
        lock (StorageRuntime.FileLock)
        {
            string inputs = Sanitize(entry.InputPaths);
            string line = $"{entry.Time}|{entry.Command}|{entry.FileCount}|Failed|{reason}|{now:yyyy-MM-dd HH:mm:ss}|-1|{inputs}|{entry.OutputPath}";
            File.AppendAllText(StorageRuntime.HistoryFilePath, line + Environment.NewLine, System.Text.Encoding.UTF8);
        }
    }

    internal static string Sanitize(string? value) =>
        (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
}

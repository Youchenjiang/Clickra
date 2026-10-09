using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clickra.Core;

public static partial class ClickraStorage
{
    /// <summary>Owns the file-format and filesystem details for task-*.tmp records.</summary>
    private static class TaskRecordStore
    {
        internal static string PathFor(string taskId) => Path.Combine(TasksDir, $"{TaskFilePrefix}{taskId}.tmp");

        internal static string NewTaskId() =>
            $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString("N")[..8]}";

        internal static void EnsureDirectory()
        {
            try
            {
                if (!Directory.Exists(TasksDir))
                {
                    Directory.CreateDirectory(TasksDir);
                }
            }
            catch
            {
                // The task directory may not exist yet on first run or may be temporarily unavailable.
            }
        }

        internal static List<string> ListTaskIds()
        {
            try
            {
                if (!Directory.Exists(TasksDir)) return new List<string>();
                return Directory.GetFiles(TasksDir, $"{TaskFilePrefix}*.tmp")
                    .OrderByDescending(GetTaskFileSortKey)
                    .Select(file => Path.GetFileNameWithoutExtension(file)[TaskFilePrefix.Length..])
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        internal static bool Write(TaskFileData data)
        {
            lock (FileLock)
            {
                try
                {
                    using var writer = new StreamWriter(PathFor(data.TaskId), false, System.Text.Encoding.UTF8);
                    writer.WriteLine($"Id={data.TaskId}");
                    writer.WriteLine($"Time={data.Time ?? DateTime.UtcNow.ToString(DateTimeFormat)}");
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
                    if (data.ParkedRetentionDays.HasValue)
                        writer.WriteLine($"ParkedRetentionDays={data.ParkedRetentionDays.Value}");
                    if (!string.IsNullOrEmpty(data.ParkedSince))
                        writer.WriteLine($"ParkedSince={data.ParkedSince}");
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
            lock (FileLock)
            {
                string path = PathFor(taskId);
                if (!File.Exists(path)) return null;
                try
                {
                    var values = ParseKeyValueLines(File.ReadAllLines(path));

                    if (!int.TryParse(values.GetValueOrDefault("FileCount", "0"), out int fileCount)) fileCount = 0;
                    if (!Enum.TryParse(values.GetValueOrDefault("Status", "Pending"), out ConversionStatus status)) status = ConversionStatus.Pending;
                    if (!int.TryParse(values.GetValueOrDefault("CurrentIndex", "0"), out int currentIndex)) currentIndex = 0;
                    if (!long.TryParse(values.GetValueOrDefault("ElapsedMs", "-1"), out long elapsedMs)) elapsedMs = -1;
                    if (!int.TryParse(values.GetValueOrDefault("Pid", "0"), out int pid)) pid = 0;
                    int? parkedDays = int.TryParse(values.GetValueOrDefault("ParkedRetentionDays", ""), out int parsedDays)
                        ? parsedDays
                        : null;

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

        private static string GetTaskFileSortKey(string filePath)
        {
            string name = Path.GetFileNameWithoutExtension(filePath);
            string id = name.Length > TaskFilePrefix.Length ? name[TaskFilePrefix.Length..] : name;
            string timestamp = id.Length >= 17 ? id[..17] : id;
            return timestamp.PadRight(17, '0');
        }
    }
}

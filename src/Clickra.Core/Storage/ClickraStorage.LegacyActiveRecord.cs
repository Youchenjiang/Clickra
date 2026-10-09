using System;
using System.IO;

namespace Clickra.Core;

public static partial class ClickraStorage
{
    // Legacy singleton active.tmp compatibility kept behind a dedicated adapter.
    // Public facade methods remain for callers that have not migrated to task-*.tmp records yet.

    public static void StartActiveRecord(string command, int fileCount, string? inputPaths = null) =>
        LegacyActiveRecordAdapter.Start(command, fileCount, inputPaths);

    public static void SetActiveRecordInProgress() => LegacyActiveRecordAdapter.SetInProgress();

    public static void SetActiveRecordIndex(int index) => LegacyActiveRecordAdapter.SetIndex(index);

    public static void CompleteActiveRecord(
        string command,
        string startTime,
        bool isSuccess,
        string errorMsg,
        string? endTime = null,
        long elapsedMs = -1,
        string? inputPaths = null,
        string? outputPath = null) =>
        LegacyActiveRecordAdapter.Complete(
            command,
            startTime,
            isSuccess,
            errorMsg,
            endTime,
            elapsedMs,
            inputPaths,
            outputPath);

    public static void ClearActiveRecord() => LegacyActiveRecordAdapter.Clear();

    public static HistoryEntry? GetActiveEntry() => LegacyActiveRecordAdapter.Get();

    private static class LegacyActiveRecordAdapter
    {
        private static string ActiveFile => Path.Combine(DataDir, "active.tmp");

        internal static void Start(string command, int fileCount, string? inputPaths)
        {
            RunWithMutex(() =>
            {
                Write(command, fileCount, ConversionStatus.Pending, "", null, inputPaths);
            });
        }

        internal static void SetInProgress()
        {
            RunWithMutex(() =>
            {
                var entry = Read();
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
            RunWithMutex(() =>
            {
                var entry = Read();
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

        internal static void Complete(
            string command,
            string startTime,
            bool isSuccess,
            string errorMsg,
            string? endTime,
            long elapsedMs,
            string? inputPaths,
            string? outputPath)
        {
            RunWithMutex(() =>
            {
                lock (FileLock)
                {
                    try
                    {
                        string cleanError = (errorMsg ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
                        string completedAt = endTime ?? DateTime.UtcNow.ToString(DateTimeFormat);
                        string inputs = (inputPaths ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
                        string output = (outputPath ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
                        string[] inputList = inputs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                        int currentIndex = 0;
                        try
                        {
                            var activeEntry = Read();
                            if (activeEntry.HasValue)
                            {
                                currentIndex = activeEntry.Value.CurrentIndex;
                            }
                        }
                        catch
                        {
                            // Legacy state lookup is best effort; completion still records history.
                        }

                        if (File.Exists(ActiveFile))
                        {
                            File.Delete(ActiveFile);
                        }

                        Write(
                            command,
                            inputList.Length,
                            isSuccess ? ConversionStatus.Success : ConversionStatus.Failed,
                            cleanError,
                            startTime,
                            inputs,
                            currentIndex);

                        string historyLine = $"{startTime}|{command}|{inputList.Length}|{(isSuccess ? "Success" : "Failed")}|{cleanError}|{completedAt}|{elapsedMs}|{inputs}|{output}";
                        File.AppendAllText(HistoryFile, historyLine + Environment.NewLine, System.Text.Encoding.UTF8);
                    }
                    catch
                    {
                        // Legacy compatibility writes are best effort.
                    }
                }
            });
        }

        internal static void Clear()
        {
            RunWithMutex(() =>
            {
                lock (FileLock)
                {
                    try
                    {
                        if (File.Exists(ActiveFile))
                        {
                            File.Delete(ActiveFile);
                        }
                    }
                    catch
                    {
                        // Another process may already have removed the legacy record.
                    }
                }
            });
        }

        internal static HistoryEntry? Get() => RunWithMutex(Read);

        private static void Write(
            string command,
            int fileCount,
            ConversionStatus status,
            string errorMsg,
            string? time = null,
            string? inputPaths = null,
            int currentIndex = 0)
        {
            lock (FileLock)
            {
                try
                {
                    using var writer = new StreamWriter(ActiveFile, false, System.Text.Encoding.UTF8);
                    writer.WriteLine($"Time={time ?? DateTime.UtcNow.ToString(DateTimeFormat)}");
                    writer.WriteLine($"Command={command}");
                    writer.WriteLine($"FileCount={fileCount}");
                    writer.WriteLine($"Status={status}");
                    writer.WriteLine($"ErrorMessage={(errorMsg ?? "").Replace("\r", " ").Replace("\n", " ")}");
                    writer.WriteLine($"InputPaths={(inputPaths ?? "").Replace("\r", " ").Replace("\n", " ")}");
                    writer.WriteLine($"CurrentIndex={currentIndex}");
                }
                catch
                {
                    // Legacy compatibility writes are best effort.
                }
            }
        }

        private static HistoryEntry? Read()
        {
            lock (FileLock)
            {
                if (!File.Exists(ActiveFile)) return null;
                try
                {
                    var values = ParseKeyValueLines(File.ReadAllLines(ActiveFile));

                    if (!int.TryParse(values.GetValueOrDefault("FileCount", "0"), out int fileCount)) fileCount = 0;
                    if (!Enum.TryParse(values.GetValueOrDefault("Status", "Pending"), out ConversionStatus status)) status = ConversionStatus.Pending;
                    if (!int.TryParse(values.GetValueOrDefault("CurrentIndex", "0"), out int currentIndex)) currentIndex = 0;

                    return new HistoryEntry
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
}

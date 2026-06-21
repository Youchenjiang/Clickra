using System;
using System.IO;

namespace Clickra.Core
{
    public static partial class ClickraStorage
    {
        // ─── Active Job Tracking (File-based IPC) ─────────────────────────────
        // ProgressWindow 與 DashboardWindow 是不同進程，必須透過檔案溝通即時狀態。
        // 格式：每行 Key=Value，欄位有 Time / Command / FileCount / Status / ErrorMessage

        private static string ActiveFile => Path.Combine(DataDir, "active.tmp");

        /// <summary>
        /// 開始追蹤一個新的作業（Pending 狀態），寫入 active.tmp。
        /// </summary>
        public static void StartActiveRecord(string command, int fileCount, string? inputPaths = null)
        {
            RunWithMutex(() =>
            {
                WriteActiveFileInternal(command, fileCount, ConversionStatus.Pending, "", null, inputPaths);
            });
        }

        /// <summary>
        /// 將進行中作業的狀態更新為 InProgress。
        /// </summary>
        public static void SetActiveRecordInProgress()
        {
            RunWithMutex(() =>
            {
                var entry = ReadActiveFileInternal();
                if (entry.HasValue)
                {
                    WriteActiveFileInternal(entry.Value.Command, entry.Value.FileCount, ConversionStatus.InProgress, "", entry.Value.Time, entry.Value.InputPaths);
                }
            });
        }

        /// <summary>
        /// 更新進行中作業的當前處理檔案索引。
        /// </summary>
        public static void SetActiveRecordIndex(int index)
        {
            RunWithMutex(() =>
            {
                var entry = ReadActiveFileInternal();
                if (entry.HasValue)
                {
                    WriteActiveFileInternal(entry.Value.Command, entry.Value.FileCount, entry.Value.Status, entry.Value.ErrorMessage, entry.Value.Time, entry.Value.InputPaths, index);
                }
            });
        }

        public static void CompleteActiveRecord(string command, string startTime, bool isSuccess, string errorMsg, string? endTime = null, long elapsedMs = -1, string? inputPaths = null, string? outputPath = null)
        {
            RunWithMutex(() =>
            {
                lock (FileLock)
                {
                    try
                    {
                        string cleanErr = (errorMsg ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
                        string et = endTime ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        string inputs = (inputPaths ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
                        string output = (outputPath ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", " ");

                        var inputList = inputs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                        var outputList = output.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                        int currentIndex = 0;
                        try
                        {
                            var activeEntry = ReadActiveFileInternal();
                            if (activeEntry.HasValue)
                            {
                                currentIndex = activeEntry.Value.CurrentIndex;
                            }
                        }
                        catch { }

                        if (File.Exists(ActiveFile))
                        {
                            File.Delete(ActiveFile);
                        }
                        WriteActiveFileInternal(command, inputList.Length, isSuccess ? ConversionStatus.Success : ConversionStatus.Failed, cleanErr, startTime, inputs, currentIndex);

                        string historyLine = $"{startTime}|{command}|{inputList.Length}|{(isSuccess ? "Success" : "Failed")}|{cleanErr}|{et}|{elapsedMs}|{inputs}|{output}";
                        File.AppendAllText(HistoryFile, historyLine + Environment.NewLine, System.Text.Encoding.UTF8);
                    }
                    catch { }
                }
            });
        }

        public static void ClearActiveRecord()
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
                    catch { }
                }
            });
        }

        private static void WriteActiveFileInternal(string command, int fileCount, ConversionStatus status, string errorMsg, string? time = null, string? inputPaths = null, int currentIndex = 0)
        {
            lock (FileLock)
            {
                try
                {
                    using var sw = new StreamWriter(ActiveFile, false, System.Text.Encoding.UTF8);
                    sw.WriteLine($"Time={time ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");
                    sw.WriteLine($"Command={command}");
                    sw.WriteLine($"FileCount={fileCount}");
                    sw.WriteLine($"Status={status}");
                    sw.WriteLine($"ErrorMessage={(errorMsg ?? "").Replace("\r", " ").Replace("\n", " ")}");
                    sw.WriteLine($"InputPaths={(inputPaths ?? "").Replace("\r", " ").Replace("\n", " ")}");
                    sw.WriteLine($"CurrentIndex={currentIndex}");
                }
                catch { }
            }
        }

        public static HistoryEntry? GetActiveEntry()
        {
            return RunWithMutex(() => ReadActiveFileInternal());
        }

        private static HistoryEntry? ReadActiveFileInternal()
        {
            lock (FileLock)
            {
                if (!File.Exists(ActiveFile)) return null;
                try
                {
                    var lines = File.ReadAllLines(ActiveFile);
                    var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var line in lines)
                    {
                        int idx = line.IndexOf('=');
                        if (idx > 0)
                        {
                            dict[line.Substring(0, idx)] = line.Substring(idx + 1);
                        }
                    }

                    if (!int.TryParse(dict.GetValueOrDefault("FileCount", "0"), out int fc)) fc = 0;
                    if (!Enum.TryParse(dict.GetValueOrDefault("Status", "Pending"), out ConversionStatus status)) status = ConversionStatus.Pending;
                    if (!int.TryParse(dict.GetValueOrDefault("CurrentIndex", "0"), out int ci)) ci = 0;

                    return new HistoryEntry
                    {
                        Time = dict.GetValueOrDefault("Time", ""),
                        Command = dict.GetValueOrDefault("Command", ""),
                        FileCount = fc,
                        Status = status,
                        ErrorMessage = dict.GetValueOrDefault("ErrorMessage", ""),
                        InputPaths = dict.GetValueOrDefault("InputPaths", ""),
                        CurrentIndex = ci
                    };
                }
                catch { return null; }
            }
        }
    }
}

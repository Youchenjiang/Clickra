using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Clickra.Core
{
    public static partial class ClickraStorage
    {
        // ─── Task Queue (File-based IPC) ───────────────────────────────────────
        // 每個轉換任務在 tasks/ 目錄下擁有自己的進度檔（task-{id}.tmp），因此
        // 多個並行任務不會像舊版單一 active.tmp 那樣互相覆蓋進度；Dashboard /
        // ProgressWindow / Fluent 跨進程透過這些檔案即時檢視「進行中」任務佇列。
        // 格式：每行 Key=Value，欄位有 Id / Time / Command / FileCount / Status /
        // ErrorMessage / InputPaths / CurrentIndex / OutputPath / EndTime / ElapsedMs / Pid

        private static string TasksDir => Path.Combine(DataDir, "tasks");
        private static string LegacyActiveFile => Path.Combine(DataDir, "active.tmp");

        // 完成後的任務檔保留一小段時間供 UI 短暫顯示結果，之後自動清除。
        private const int CompletedTaskTtlMinutes = 10;
        // 逾時仍未完成的任務視為遺棄（進程崩潰/被強制結束），自動清除。
        private const int AbandonedTaskTtlHours = 24;
        private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>封裝 task-*.tmp 檔案所有欄位，供 WriteTaskFileInternal 使用。</summary>
        private record TaskFileData
        {
            public string TaskId { get; init; } = string.Empty;
            public string Command { get; init; } = string.Empty;
            public int FileCount { get; init; }
            public ConversionStatus Status { get; init; }
            public string ErrorMessage { get; init; } = string.Empty;
            public string? Time { get; init; }
            public string? InputPaths { get; init; }
            public int CurrentIndex { get; init; }
            public string? OutputPath { get; init; }
            public string? EndTime { get; init; }
            public long ElapsedMs { get; init; } = -1;
            public int Pid { get; init; }
        }

        /// <summary>從 HistoryEntry 構建 TaskFileData（保留所有既有欄位）。</summary>
        private static TaskFileData ToTaskData(HistoryEntry entry, ConversionStatus status, string? errorMsg = null, string? endTime = null, long elapsedMs = -1, int? pidOverride = null)
        {
            return new TaskFileData
            {
                TaskId = entry.Id,
                Command = entry.Command,
                FileCount = entry.FileCount,
                Status = status,
                ErrorMessage = errorMsg ?? entry.ErrorMessage,
                Time = entry.Time,
                InputPaths = entry.InputPaths,
                CurrentIndex = entry.CurrentIndex,
                OutputPath = entry.OutputPath,
                EndTime = endTime ?? entry.EndTime,
                ElapsedMs = elapsedMs >= 0 ? elapsedMs : entry.ElapsedMs,
                Pid = pidOverride ?? entry.Pid
            };
        }

        // 目前執行緒正在處理的任務（Win32 ProgressWindow 在同一執行緒上建立並
        // 回報任務；Fluent 走 Task.Run，執行緒不同，則退回 Pid 定位，見
        // SetActiveRecordIndex）。
        [ThreadStatic]
        private static string? _threadTaskId;

        /// <summary>
        /// 建立一個新的任務（Pending 狀態），回傳可識別該任務的唯一 ID。
        /// 重複啟動多個任務會各自建立獨立的進度檔，互不覆蓋。
        /// </summary>
        public static string StartTask(string command, int fileCount, string? inputPaths = null)
        {
            string taskId = NewTaskId();
            RunWithMutex(() =>
            {
                EnsureTasksDir();
                var data = new TaskFileData
                {
                    TaskId = taskId,
                    Command = command,
                    FileCount = fileCount,
                    Status = ConversionStatus.Pending,
                    InputPaths = inputPaths,
                    Pid = Environment.ProcessId
                };
                if (!WriteTaskFileInternal(data))
                    throw new IOException($"Failed to write task file for {taskId}; check data directory permissions.");
            });
            _threadTaskId = taskId;
            return taskId;
        }

        /// <summary>將任務狀態由 Pending 切換為 InProgress，並把 Pid 更新為目前進程。
        /// （暫存恢復時沿用原任務檔、原 Pid 已死；不更新會讓遺棄清理誤判為死任務。）</summary>
        public static void SetTaskInProgress(string taskId)
        {
            RunWithMutex(() =>
            {
                var entry = ReadTaskFileInternal(taskId);
                if (entry.HasValue)
                {
                    var data = ToTaskData(entry.Value, ConversionStatus.InProgress, pidOverride: Environment.ProcessId);
                    WriteTaskFileInternal(data);
                }
            });
        }

        /// <summary>更新目前任務的批次處理檔案索引（供儀表板顯示單檔狀態）。</summary>
        public static void SetTaskIndex(string taskId, int index)
        {
            RunWithMutex(() =>
            {
                var entry = ReadTaskFileInternal(taskId);
                if (entry.HasValue)
                {
                    var data = ToTaskData(entry.Value, entry.Value.Status) with { CurrentIndex = index };
                    WriteTaskFileInternal(data);
                }
            });
        }

        /// <summary>CompleteTask 參數封裝，減少方法參數數量。</summary>
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

        /// <summary>
        /// 完成任務：寫入 Success/Failed 最終狀態並附加一行持久化歷史紀錄。
        /// 任務檔會保留一小段時間（見 CompletedTaskTtlMinutes）供 UI 顯示結果。
        /// </summary>
        public static void CompleteTask(string taskId, string command, CompleteTaskRequest req)
        {
            RunWithMutex(() =>
            {
                lock (FileLock)
                {
                    try
                    {
                        string cleanErr = (req.ErrorMsg ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
                        string et = req.EndTime ?? DateTime.UtcNow.ToString(DateTimeFormat);

                        // Read existing task to preserve FileCount/InputPaths when caller omits them.
                        var existing = ReadTaskFileInternal(taskId);
                        string inputs = req.InputPaths != null
                            ? req.InputPaths.Replace("\r", " ").Replace("\n", " ").Replace("|", " ")
                            : existing?.InputPaths ?? "";
                        string output = req.OutputPath != null
                            ? req.OutputPath.Replace("\r", " ").Replace("\n", " ").Replace("|", " ")
                            : existing?.OutputPath ?? "";

                        var inputList = inputs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                        int fileCount = inputList.Length > 0 ? inputList.Length : existing?.FileCount ?? 0;
                        int currentIndex = existing?.CurrentIndex ?? 0;

                        var finalStatus = req.IsSuccess ? ConversionStatus.Success : ConversionStatus.Failed;
                        var data = new TaskFileData
                        {
                            TaskId = taskId,
                            Command = command,
                            FileCount = fileCount,
                            Status = finalStatus,
                            ErrorMessage = cleanErr,
                            Time = req.StartTime,
                            InputPaths = inputs,
                            CurrentIndex = currentIndex,
                            OutputPath = output,
                            EndTime = et,
                            ElapsedMs = req.ElapsedMs,
                            Pid = Environment.ProcessId
                        };
                        WriteTaskFileInternal(data);

                        string historyLine = $"{req.StartTime}|{command}|{fileCount}|{finalStatus}|{cleanErr}|{et}|{req.ElapsedMs}|{inputs}|{output}";
                        File.AppendAllText(HistoryFile, historyLine + Environment.NewLine, System.Text.Encoding.UTF8);
                    }
                    catch
                    {
                        // History append or task write failed — mark task as Failed
                        // so the UI does not show a phantom success.
                        try
                        {
                            var fallback = new TaskFileData
                            {
                                TaskId = taskId,
                                Command = command,
                                Status = ConversionStatus.Failed,
                                ErrorMessage = "History persistence error",
                                Time = req.StartTime,
                                EndTime = req.EndTime ?? DateTime.UtcNow.ToString(DateTimeFormat),
                                Pid = Environment.ProcessId
                            };
                            WriteTaskFileInternal(fallback);
                        }
                        catch { /* best-effort fallback write */ }
                    }
                }
            });
            if (_threadTaskId == taskId) _threadTaskId = null;
        }

        /// <summary>
        /// 將進行中的任務標記為已暫存（Parked）：記錄暫存原因與「下一個要處理的檔案索引」，
        /// 不寫歷史——任務仍可從 dashboard「繼續」或「取消」。
        /// </summary>
        public static void ParkTask(string taskId, string reason, int nextIndex)
        {
            RunWithMutex(() =>
            {
                var entry = ReadTaskFileInternal(taskId);
                if (entry.HasValue)
                {
                    var data = ToTaskData(entry.Value, ConversionStatus.Parked, errorMsg: reason) with { CurrentIndex = nextIndex };
                    WriteTaskFileInternal(data);
                }
            });
            if (_threadTaskId == taskId) _threadTaskId = null;
        }

        /// <summary>列出所有已暫存（Parked）的任務，新的在前。</summary>
        public static List<HistoryEntry> GetParkedTasks()
        {
            return RunWithMutex(() =>
            {
                PruneTasks();
                return ListTaskFiles()
                    .Select(ReadTaskFileInternal)
                    .Where(e => e.HasValue && e.Value.Status == ConversionStatus.Parked)
                    .Select(e => e.GetValueOrDefault())
                    .ToList();
            });
        }

        /// <summary>已暫存任務的保留天數（設定 ParkedTaskRetention；0 = 無限期，預設 7）。</summary>
        public static int GetParkedRetentionDays()
        {
            string raw = GetSetting("ParkedTaskRetention");
            if (string.IsNullOrWhiteSpace(raw)) return 7;
            return int.TryParse(raw, out int days) ? Math.Max(days, 0) : 7;
        }

        /// <summary>刪除任務進度檔（例如任務完成且不需保留，或診斷錯誤後清理）。</summary>
        public static void DeleteTask(string taskId)
        {
            RunWithMutex(() =>
            {
                lock (FileLock)
                {
                    try
                    {
                        string path = TaskFilePath(taskId);
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                    catch { /* file may already be deleted by another process */ }
                }
            });
            if (_threadTaskId == taskId) _threadTaskId = null;
        }

        /// <summary>讀取單一任務的最新狀態。</summary>
        public static HistoryEntry? GetTask(string taskId)
        {
            return RunWithMutex(() => ReadTaskFileInternal(taskId));
        }

        /// <summary>列出所有進行中任務（Pending / InProgress），新的在前。</summary>
        public static List<HistoryEntry> GetActiveTasks()
        {
            return RunWithMutex(() =>
            {
                PruneTasks();
                return ListTaskFiles()
                    .Select(ReadTaskFileInternal)
                    .Where(e => e.HasValue && (e.Value.Status == ConversionStatus.Pending || e.Value.Status == ConversionStatus.InProgress))
                    .Select(e => e.GetValueOrDefault())
                    .ToList();
            });
        }

        /// <summary>列出所有任務（含已完成），新的在前；同時清理過期檔案。</summary>
        public static List<HistoryEntry> GetTasks(int limit = 50)
        {
            return RunWithMutex(() =>
            {
                PruneTasks();
                return ListTaskFiles().Take(limit).Select(ReadTaskFileInternal).Where(e => e.HasValue).Select(e => e.GetValueOrDefault()).ToList();
            });
        }

        // NOTE: GetActiveEntry and SetActiveRecordIndex are defined in the
        // Legacy Active Record API section below, operating on the single active.tmp file.
        // They will be migrated to use the Task API in a later branch.

        // ─── Internal ──────────────────────────────────────────────────────────

        private static string TaskFilePath(string taskId) => Path.Combine(TasksDir, $"task-{taskId}.tmp");

        /// <summary>可排序的唯一任務 ID：時間戳 + 短 GUID（檔名排序即建立順序）。</summary>
        private static string NewTaskId()
            => $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString("N")[..8]}";

        private static void EnsureTasksDir()
        {
            try
            {
                if (!Directory.Exists(TasksDir))
                {
                    Directory.CreateDirectory(TasksDir);
                }
                // NOTE: Legacy active.tmp is NOT deleted here. Clickra.CLI
                // (ProgressWindow, DashboardWindow) still writes to it.
                // Defer cleanup until the legacy API is fully migrated.
            }            catch { /* tasks dir may not exist yet on first run */ }
        }


        private static List<string> ListTaskFiles()
        {
            try
            {
                if (!Directory.Exists(TasksDir)) return new List<string>();
                return Directory.GetFiles(TasksDir, "task-*.tmp")
                    .OrderByDescending(GetTaskFileSortKey)
                    .Select(f => Path.GetFileNameWithoutExtension(f)["task-".Length..])
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>Extract the creation timestamp from a task filename for ordering.
        /// File.GetLastWriteTime has only 2-second resolution on NTFS, causing
        /// ordering instability when two tasks are created within the same window.</summary>
        private static string GetTaskFileSortKey(string filePath)
        {
            string name = Path.GetFileNameWithoutExtension(filePath); // task-{id}
            string id = name.Length > "task-".Length ? name["task-".Length..] : name;
            // NewTaskId format: yyyyMMddHHmmssfff-GUID[0..8]
            // Timestamp is the first 17 chars; the dash and GUID follow.
            // Use only the 17-char timestamp for deterministic chronological ordering.
            string ts = id.Length >= 17 ? id[..17] : id;
            return ts.PadRight(17, '0');
        }

        /// <summary>清除過期的任務檔：已完成超過 10 分鐘、進行中超過 24 小時（遺棄）、
        /// 或建立進程已死的進行中任務（崩潰/被強制結束/系統重啟）——後者記錄為
        /// Canceled 歷史後刪除，避免 dashboard 永遠顯示「轉換中」。
        /// 注意：進行中任務的檔案 last-write-time 由 SetTaskIndex/SetTaskInProgress
        /// 更新，因此 24h 超時實際上偵測的是「24 小時無任何進度更新」。</summary>
        private static void PruneTasks()
        {
            try
            {
                EnsureTasksDir();
                if (!Directory.Exists(TasksDir)) return;
                var now = DateTime.UtcNow;
                foreach (string file in Directory.GetFiles(TasksDir, "task-*.tmp"))
                {
                    try { PruneSingleTaskFile(file, now); }
                    catch { /* skip corrupt task file */ }
                }
            }
            catch { /* tasks dir not ready */ }
        }

        private static void PruneSingleTaskFile(string file, DateTime now)
        {
            string taskId = Path.GetFileNameWithoutExtension(file);
            if (taskId.StartsWith("task-", StringComparison.OrdinalIgnoreCase))
            {
                taskId = taskId["task-".Length..];
            }
            var entry = ReadTaskFileInternal(taskId);
            if (!entry.HasValue) return;

            var e = entry.Value;
            bool finished = e.Status == ConversionStatus.Success || e.Status == ConversionStatus.Failed;
            bool parked = e.Status == ConversionStatus.Parked;
            TimeSpan age = now - File.GetLastWriteTime(file);

            if (IsOrphaned(e, finished, parked, age))
            {
                string reason = IsDeadPid(e, finished, parked) ? "Abandoned" : "Canceled";
                WriteCanceledHistory(e, reason, now);
                File.Delete(file);
                return;
            }

            if (IsExpired(finished, parked, age))
            {
                File.Delete(file);
            }
        }

        private static bool IsDeadPid(HistoryEntry e, bool finished, bool parked)
            => !finished && !parked && e.Pid > 0 && !IsProcessAlive(e.Pid);

        private static bool IsOrphaned(HistoryEntry e, bool finished, bool parked, TimeSpan age)
        {
            if (IsDeadPid(e, finished, parked)) return true;
            return !finished && !parked && age.TotalHours > AbandonedTaskTtlHours;
        }

        private static bool IsExpired(bool finished, bool parked, TimeSpan age)
        {
            if (finished) return age.TotalMinutes > CompletedTaskTtlMinutes;
            if (parked) return GetParkedRetentionDays() > 0 && age.TotalDays > GetParkedRetentionDays();
            return false;
        }

        private static void WriteCanceledHistory(HistoryEntry e, string reason, DateTime now)
        {
            lock (FileLock)
            {
                string cleanInputs = e.InputPaths.Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
                string historyLine = $"{e.Time}|{e.Command}|{e.FileCount}|Failed|{reason}|{now:yyyy-MM-dd HH:mm:ss}|-1|{cleanInputs}|{e.OutputPath}";
                File.AppendAllText(HistoryFile, historyLine + Environment.NewLine, System.Text.Encoding.UTF8);
            }
        }

        /// <summary>指定的進程 ID 是否仍在執行（用於偵測遺棄任務）。</summary>
        private static bool IsProcessAlive(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static bool WriteTaskFileInternal(TaskFileData d)
        {
            lock (FileLock)
            {
                try
                {
                    using var sw = new StreamWriter(TaskFilePath(d.TaskId), false, System.Text.Encoding.UTF8);
                    sw.WriteLine($"Id={d.TaskId}");
                    sw.WriteLine($"Time={d.Time ?? DateTime.UtcNow.ToString(DateTimeFormat)}");
                    sw.WriteLine($"Command={d.Command}");
                    sw.WriteLine($"FileCount={d.FileCount}");
                    sw.WriteLine($"Status={d.Status}");
                    sw.WriteLine($"ErrorMessage={(d.ErrorMessage ?? "").Replace("\r", " ").Replace("\n", " ")}");
                    sw.WriteLine($"InputPaths={(d.InputPaths ?? "").Replace("\r", " ").Replace("\n", " ")}");
                    sw.WriteLine($"CurrentIndex={d.CurrentIndex}");
                    sw.WriteLine($"OutputPath={(d.OutputPath ?? "").Replace("\r", " ").Replace("\n", " ")}");
                    sw.WriteLine($"EndTime={d.EndTime ?? ""}");
                    sw.WriteLine($"ElapsedMs={d.ElapsedMs}");
                    sw.WriteLine($"Pid={d.Pid}");
                    return true;
                }
                catch { return false; }
            }
        }

        private static HistoryEntry? ReadTaskFileInternal(string taskId)
        {
            lock (FileLock)
            {
                string path = TaskFilePath(taskId);
                if (!File.Exists(path)) return null;
                try
                {
                    var lines = File.ReadAllLines(path);
                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var line in lines)
                    {
                        int idx = line.IndexOf('=');
                        if (idx > 0)
                        {
                            dict[line[..idx]] = line[(idx + 1)..];
                        }
                    }

                    if (!int.TryParse(dict.GetValueOrDefault("FileCount", "0"), out int fc)) fc = 0;
                    if (!Enum.TryParse(dict.GetValueOrDefault("Status", "Pending"), out ConversionStatus status)) status = ConversionStatus.Pending;
                    if (!int.TryParse(dict.GetValueOrDefault("CurrentIndex", "0"), out int ci)) ci = 0;
                    if (!long.TryParse(dict.GetValueOrDefault("ElapsedMs", "-1"), out long ms)) ms = -1;
                    if (!int.TryParse(dict.GetValueOrDefault("Pid", "0"), out int pid)) pid = 0;

                    return new HistoryEntry
                    {
                        Id = dict.GetValueOrDefault("Id", taskId),
                        Time = dict.GetValueOrDefault("Time", ""),
                        Command = dict.GetValueOrDefault("Command", ""),
                        FileCount = fc,
                        Status = status,
                        ErrorMessage = dict.GetValueOrDefault("ErrorMessage", ""),
                        InputPaths = dict.GetValueOrDefault("InputPaths", ""),
                        CurrentIndex = ci,
                        OutputPath = dict.GetValueOrDefault("OutputPath", ""),
                        EndTime = dict.GetValueOrDefault("EndTime", ""),
                        ElapsedMs = ms,
                        Pid = pid
                    };
                }
                catch { return null; }
            }
        }

        // ─── Legacy Active Record API (single active.tmp) ──────────────────────
        // These methods are retained for backward compatibility with Clickra.CLI
        // (ProgressWindow, ClickraStartup) until they migrate to the Task API above.

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
                        string et = endTime ?? DateTime.UtcNow.ToString(DateTimeFormat);
                        string inputs = (inputPaths ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
                        string output = (outputPath ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", " ");

                        var inputList = inputs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

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
                    using var sw = new StreamWriter(ActiveFile, false, System.Text.Encoding.UTF8);                            sw.WriteLine($"Time={time ?? DateTime.UtcNow.ToString(DateTimeFormat)}");
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
                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var line in lines)
                    {
                        int idx = line.IndexOf('=');
                        if (idx > 0)
                        {
                            dict[line[..idx]] = line[(idx + 1)..];
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

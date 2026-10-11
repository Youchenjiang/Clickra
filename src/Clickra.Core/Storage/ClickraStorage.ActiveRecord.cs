using System;
using System.Collections.Generic;
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

        private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
        private const string TaskFilePrefix = "task-";

        /// <summary>封裝 task-*.tmp 檔案所有欄位，供 WriteTaskFileInternal 使用。</summary>
        private sealed record TaskFileData
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

            /// <summary>單一任務的保留天數覆寫；null = 沿用全域政策。</summary>
            public int? ParkedRetentionDays { get; init; }

            /// <summary>暫存起始時間（UTC）；null = 尚未記錄，年資退回檔案 mtime。</summary>
            public string? ParkedSince { get; init; }
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
                Pid = pidOverride ?? entry.Pid,
                ParkedRetentionDays = entry.ParkedRetentionDays,
                ParkedSince = entry.ParkedSince
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
                if (entry.HasValue && entry.Value.Status is ConversionStatus.Pending or ConversionStatus.InProgress)
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
            => ParkedTaskCoordinator.Park(taskId, reason, nextIndex);

        /// <summary>列出所有已暫存（Parked）的任務，新的在前。</summary>
        public static List<HistoryEntry> GetParkedTasks()
            => ParkedTaskCoordinator.GetAll();

        /// <summary>取消一個已暫存的任務：寫入 Canceled 歷史（與逾時/遺棄清理相同的記帳）
        /// 並刪除任務檔，讓它離開 dashboard 的「待繼續」清單。
        /// 非暫存或已不存在的任務為 no-op。</summary>
        public static void CancelParkedTask(string taskId)
            => ParkedTaskCoordinator.Cancel(taskId);

        /// <summary>已暫存任務的保留天數（0 = 無限期）。未設定或無法解析時採用登錄表的預設值。</summary>
        public record ResumedTaskInfo(string TaskId, string Command, List<string> Files, int StartIndex);

        /// <summary>
        /// Atomically claims a parked task for resume. The same mutex covers deadline validation,
        /// input/index capture, and the Parked -> InProgress transition so only one consumer can win.
        /// </summary>
        public static ResumedTaskInfo? ClaimParkedTaskForResume(string taskId)
            => ParkedTaskCoordinator.ClaimForResume(taskId);

        public static int GetParkedRetentionDays() =>
            ParkedTaskCoordinator.GetRetentionDays();

        public static void SetParkedRetentionOverride(string taskId, int? days)
            => ParkedTaskCoordinator.SetRetentionOverride(taskId, days);

        public static int? AdjustParkedRetention(string taskId, int deltaDays)
            => ParkedTaskCoordinator.AdjustRetention(taskId, deltaDays);

        /// <summary>封裝已暫存任務的保留天數與過期狀態資訊。</summary>
        public readonly record struct ParkedRetentionInfo(
            bool IsUnlimited,
            int RemainingDays,
            TimeSpan RemainingTime,
            bool IsExpiringSoon,
            bool HasExpired,
            bool IsTaskOverride = false);

        /// <summary>Formats the shared parked-task retention state for every UI surface.</summary>
        public static string DescribeParkedRetention(ParkedRetentionInfo info)
            => ParkedTaskCoordinator.DescribeRetention(info);

        /// <summary>根據設定的保留天數與暫存經過時間，計算剩餘保留狀態。</summary>
        public static ParkedRetentionInfo CalculateRetentionInfo(int retentionDays, TimeSpan age, bool isTaskOverride = false) =>
            ParkedTaskCoordinator.CalculateInfo(retentionDays, age, isTaskOverride);

        /// <summary>取得指定暫存任務的保留與過期資訊。若任務檔不存在或已過期，傳回對應狀態。</summary>
        public static ParkedRetentionInfo GetParkedRetentionInfo(string taskId)
            => ParkedTaskCoordinator.GetRetentionInfo(taskId);

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
                TaskPruningCoordinator.Prune();
                return ListTaskFiles()
                    .Select(ReadTaskFileInternal)
                    .Where(e => e.HasValue && (e.Value.Status == ConversionStatus.Pending || e.Value.Status == ConversionStatus.InProgress))
                    .Select(e => e.GetValueOrDefault())
                    .ToList();
            });
        }

        // NOTE: GetActiveEntry and SetActiveRecordIndex are defined in the
        // Legacy Active Record API section below, operating on the single active.tmp file.
        // They will be migrated to use the Task API in a later branch.

        // ─── Internal ──────────────────────────────────────────────────────────

        private static string TaskFilePath(string taskId) => TaskRecordStore.PathFor(taskId);

        /// <summary>可排序的唯一任務 ID：時間戳 + 短 GUID（檔名排序即建立順序）。</summary>
        private static string NewTaskId() => TaskRecordStore.CreateTaskId();

        private static void EnsureTasksDir() => TaskRecordStore.EnsureDirectory();


        private static List<string> ListTaskFiles() => TaskRecordStore.ListTaskIds();

        private static void WriteCanceledHistory(HistoryEntry e, string reason, DateTime now)
        {
            lock (FileLock)
            {
                string cleanInputs = e.InputPaths.Replace("\r", " ").Replace("\n", " ").Replace("|", " ");
                string historyLine = $"{e.Time}|{e.Command}|{e.FileCount}|Failed|{reason}|{now:yyyy-MM-dd HH:mm:ss}|-1|{cleanInputs}|{e.OutputPath}";
                File.AppendAllText(HistoryFile, historyLine + Environment.NewLine, System.Text.Encoding.UTF8);
            }
        }

        /// <summary>Parse a Key=Value text file into a dictionary (shared by Task and Legacy readers).</summary>
        private static Dictionary<string, string> ParseKeyValueLines(string[] lines)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                int idx = line.IndexOf('=');
                if (idx > 0)
                {
                    dict[line[..idx]] = line[(idx + 1)..];
                }
            }
            return dict;
        }

        private static bool WriteTaskFileInternal(TaskFileData d) => TaskRecordStore.Write(d);

        private static HistoryEntry? ReadTaskFileInternal(string taskId) => TaskRecordStore.Read(taskId);

    }
}

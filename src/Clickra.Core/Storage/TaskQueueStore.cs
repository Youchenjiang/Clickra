using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clickra.Core.Storage;

/// <summary>Owns task lifecycle persistence over task-*.tmp records.</summary>
internal static class TaskQueueStore
{
    [ThreadStatic]
    private static string? _threadTaskId;

    internal static string Start(string command, int fileCount, string? inputPaths)
    {
        string taskId = TaskRecordStore.CreateTaskId();
        StorageRuntime.RunWithMutex(() =>
        {
            TaskRecordStore.EnsureDirectory();
            var data = new TaskFileData
            {
                TaskId = taskId,
                Command = command,
                FileCount = fileCount,
                Status = ConversionStatus.Pending,
                InputPaths = inputPaths,
                Pid = Environment.ProcessId
            };
            if (!TaskRecordStore.Write(data))
                throw new IOException($"Failed to write task file for {taskId}; check data directory permissions.");
        });
        _threadTaskId = taskId;
        return taskId;
    }

    internal static void SetInProgress(string taskId)
    {
        StorageRuntime.RunWithMutex(() =>
        {
            ClickraStorage.HistoryEntry? entry = TaskRecordStore.Read(taskId);
            if (entry.HasValue && entry.Value.Status is ConversionStatus.Pending or ConversionStatus.InProgress)
                TaskRecordStore.Write(TaskRecordStore.FromEntry(entry.Value, ConversionStatus.InProgress, pidOverride: Environment.ProcessId));
        });
    }

    internal static void SetIndex(string taskId, int index)
    {
        StorageRuntime.RunWithMutex(() =>
        {
            ClickraStorage.HistoryEntry? entry = TaskRecordStore.Read(taskId);
            if (!entry.HasValue) return;
            TaskFileData data = TaskRecordStore.FromEntry(entry.Value, entry.Value.Status) with { CurrentIndex = index };
            TaskRecordStore.Write(data);
        });
    }

    internal static void Complete(string taskId, string command, ClickraStorage.CompleteTaskRequest request)
    {
        StorageRuntime.RunWithMutex(() =>
        {
            lock (StorageRuntime.FileLock)
            {
                try
                {
                    string error = HistoryStore.Sanitize(request.ErrorMsg);
                    string endTime = request.EndTime ?? DateTime.UtcNow.ToString(StorageRuntime.DateTimeFormat);
                    ClickraStorage.HistoryEntry? existing = TaskRecordStore.Read(taskId);
                    string inputs = request.InputPaths is not null ? HistoryStore.Sanitize(request.InputPaths) : existing?.InputPaths ?? "";
                    string output = request.OutputPath is not null ? HistoryStore.Sanitize(request.OutputPath) : existing?.OutputPath ?? "";
                    string[] inputList = inputs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    int fileCount = inputList.Length > 0 ? inputList.Length : existing?.FileCount ?? 0;
                    int currentIndex = existing?.CurrentIndex ?? 0;
                    ConversionStatus status = request.IsSuccess ? ConversionStatus.Success : ConversionStatus.Failed;

                    TaskRecordStore.Write(new TaskFileData
                    {
                        TaskId = taskId,
                        Command = command,
                        FileCount = fileCount,
                        Status = status,
                        ErrorMessage = error,
                        Time = request.StartTime,
                        InputPaths = inputs,
                        CurrentIndex = currentIndex,
                        OutputPath = output,
                        EndTime = endTime,
                        ElapsedMs = request.ElapsedMs,
                        Pid = Environment.ProcessId
                    });

                    HistoryStore.AppendCompleted(request.StartTime, command, fileCount, status, error, endTime, request.ElapsedMs, inputs, output);
                }
                catch
                {
                    try
                    {
                        TaskRecordStore.Write(new TaskFileData
                        {
                            TaskId = taskId,
                            Command = command,
                            Status = ConversionStatus.Failed,
                            ErrorMessage = "History persistence error",
                            Time = request.StartTime,
                            EndTime = request.EndTime ?? DateTime.UtcNow.ToString(StorageRuntime.DateTimeFormat),
                            Pid = Environment.ProcessId
                        });
                    }
                    catch
                    {
                    }
                }
            }
        });
        ClearThreadTask(taskId);
    }

    internal static void Delete(string taskId)
    {
        StorageRuntime.RunWithMutex(() =>
        {
            lock (StorageRuntime.FileLock)
            {
                try
                {
                    string path = TaskRecordStore.PathFor(taskId);
                    if (File.Exists(path)) File.Delete(path);
                }
                catch
                {
                }
            }
        });
        ClearThreadTask(taskId);
    }

    internal static ClickraStorage.HistoryEntry? Get(string taskId) =>
        StorageRuntime.RunWithMutex(() => TaskRecordStore.Read(taskId));

    internal static List<ClickraStorage.HistoryEntry> GetActive() =>
        StorageRuntime.RunWithMutex(() =>
        {
            TaskPruningCoordinator.Prune();
            return TaskRecordStore.ListTaskIds()
                .Select(TaskRecordStore.Read)
                .Where(entry => entry.HasValue && entry.Value.Status is ConversionStatus.Pending or ConversionStatus.InProgress)
                .Select(entry => entry.GetValueOrDefault())
                .ToList();
        });

    internal static void ClearThreadTask(string taskId)
    {
        if (_threadTaskId == taskId) _threadTaskId = null;
    }
}

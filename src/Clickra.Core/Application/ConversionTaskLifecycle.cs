using System;
using System.Collections.Generic;
using System.Diagnostics;
using Clickra.Core.Modules.HistoryTasks.Application;
using Clickra.Core.Storage;

namespace Clickra.Core.Application;

/// <summary>Owns the shared task-record lifecycle around a conversion run without owning command dispatch or UI.</summary>
public sealed class ConversionTaskLifecycle
{
    private readonly string _command;
    private readonly string _inputs;
    private readonly bool _bestEffort;
    private readonly Stopwatch _stopwatch;
    private readonly ITaskLifecycleRepository _repository;
    private bool _stopped;

    private ConversionTaskLifecycle(
        string command,
        string inputs,
        string taskId,
        string startTime,
        bool bestEffort,
        Stopwatch stopwatch,
        ITaskLifecycleRepository repository)
    {
        _command = command;
        _inputs = inputs;
        TaskId = taskId;
        StartTime = startTime;
        _bestEffort = bestEffort;
        _stopwatch = stopwatch;
        _repository = repository;
    }

    public string TaskId { get; private set; }
    public string StartTime { get; }
    public Exception? LastPersistenceError { get; private set; }

    public static ConversionTaskLifecycle Start(
        string command,
        IReadOnlyCollection<string> files,
        string? existingTaskId,
        bool bestEffort,
        string? startTimeOverride) =>
        Start(command, files, existingTaskId, bestEffort, startTimeOverride, (ITaskLifecycleRepository?)null);

    public static ConversionTaskLifecycle Start(
        string command,
        IReadOnlyCollection<string> files,
        string? existingTaskId = null,
        bool bestEffort = false,
        string? startTimeOverride = null,
        ITaskLifecycleRepository? repository = null)
    {
        string inputs = string.Join(";", files);
        string startTime = startTimeOverride ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var repo = repository ?? DefaultStorageLifecycleRepository.Instance;
        var lifecycle = new ConversionTaskLifecycle(
            command,
            inputs,
            existingTaskId ?? "",
            startTime,
            bestEffort,
            Stopwatch.StartNew(),
            repo);

        lifecycle.RunStorage(() =>
        {
            string? newTaskId = null;
            if (existingTaskId is not null)
            {
                lifecycle.TaskId = existingTaskId;
            }
            else
            {
                newTaskId = repo.Start(command, files.Count, inputs);
                lifecycle.TaskId = newTaskId;
            }

            try
            {
                repo.SetInProgress(lifecycle.TaskId);
            }
            catch
            {
                if (newTaskId is not null)
                {
                    lifecycle.TaskId = string.Empty;
                    try
                    {
                        repo.Delete(newTaskId);
                    }
                    catch
                    {
                        // best-effort rollback cleanup
                    }
                }
                throw;
            }
        });
        return lifecycle;
    }

    /// <summary>Records an early dispatch failure directly to task history and cleans up task tracking without timing delays.</summary>
    public static void RecordDispatchFailure(
        string command,
        string error,
        string? startTime,
        IReadOnlyCollection<string>? files) =>
        RecordDispatchFailure(command, error, startTime, files, null);

    public static void RecordDispatchFailure(
        string command,
        string error,
        string? startTime = null,
        IReadOnlyCollection<string>? files = null,
        ITaskLifecycleRepository? repository = null)
    {
        var lifecycle = Start(
            command,
            files ?? Array.Empty<string>(),
            bestEffort: true,
            startTimeOverride: startTime,
            repository: repository);
        lifecycle.CompleteFailure(error, outputPath: "");
        lifecycle.Delete();
    }

    public void CompleteSuccess(string outputPath, string? endTime = null) =>
        Complete(isSuccess: true, error: "", outputPath, endTime);

    public void CompleteFailure(string error, string outputPath, string? endTime = null) =>
        Complete(isSuccess: false, error, outputPath, endTime);

    public void Park(string reason, int nextFileIndex)
    {
        Stop();
        RunStorage(() => _repository.Park(TaskId, reason, nextFileIndex));
    }

    public void RecordFileStarting(int fileIndex) =>
        RunStorage(() => _repository.SetIndex(TaskId, fileIndex));

    public void Delete() => RunStorage(() => _repository.Delete(TaskId));

    private void Complete(bool isSuccess, string error, string outputPath, string? endTime)
    {
        Stop();
        RunStorage(() => _repository.Complete(TaskId, _command, new TaskCompletionRequest
        {
            StartTime = StartTime,
            IsSuccess = isSuccess,
            ErrorMessage = error,
            EndTime = endTime,
            ElapsedMs = _stopwatch.ElapsedMilliseconds,
            InputPaths = _inputs,
            OutputPath = outputPath
        }));
    }

    private void Stop()
    {
        if (_stopped) return;
        _stopwatch.Stop();
        _stopped = true;
    }

    private void RunStorage(Action action)
    {
        if (!_bestEffort)
        {
            action();
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            LastPersistenceError = ex;
            Console.Error.WriteLine($"[ConversionTaskLifecycle] Task persistence failed: {ex.Message}");
        }
    }

    private sealed class DefaultStorageLifecycleRepository : ITaskLifecycleRepository
    {
        public static readonly DefaultStorageLifecycleRepository Instance = new();

        string ITaskLifecycleRepository.Start(string command, int fileCount, string? inputPaths) =>
            ClickraStorage.StartTask(command, fileCount, inputPaths);

        void ITaskLifecycleRepository.SetInProgress(string taskId) =>
            ClickraStorage.SetTaskInProgress(taskId);

        void ITaskLifecycleRepository.SetIndex(string taskId, int index) =>
            ClickraStorage.SetTaskIndex(taskId, index);

        void ITaskLifecycleRepository.Complete(string taskId, string command, TaskCompletionRequest request) =>
            ClickraStorage.CompleteTask(taskId, command, new CompleteTaskRequest
            {
                StartTime = request.StartTime,
                IsSuccess = request.IsSuccess,
                ErrorMsg = request.ErrorMessage,
                EndTime = request.EndTime,
                ElapsedMs = request.ElapsedMs,
                InputPaths = request.InputPaths,
                OutputPath = request.OutputPath
            });

        void ITaskLifecycleRepository.Park(string taskId, string reason, int nextIndex) =>
            ClickraStorage.ParkTask(taskId, reason, nextIndex);

        void ITaskLifecycleRepository.Delete(string taskId) =>
            ClickraStorage.DeleteTask(taskId);
    }
}

/// <summary>
/// Centralizes lifecycle completion and result mapping without owning command planning or execution bodies.
/// Execution families keep their own loops, checkpoints, batch semantics, and processor options.
/// </summary>
internal sealed record ConversionLifecycleRunContext(
    ConversionPlan Plan,
    Stopwatch Stopwatch,
    ConversionTaskLifecycle? Lifecycle,
    string OutputRecord,
    bool ParkOnSignal,
    Func<Exception, ConversionFailureKind>? ClassifyFailure = null);

internal static class ConversionLifecycleResultRunner
{
    public static async Task<ConversionResult> RunAsync(
        ConversionLifecycleRunContext context,
        Func<ConversionTaskLifecycle?, Task> executeAsync,
        Func<int> completedFiles)
    {
        var (plan, stopwatch, lifecycle, outputRecord, parkOnSignal, classifyFailure) = context;
        try
        {
            await executeAsync(lifecycle);
            lifecycle?.CompleteSuccess(outputRecord);
            stopwatch.Stop();
            return Result(ConversionResultStatus.Succeeded, null);
        }
        catch (ConversionParkedException ex) when (parkOnSignal)
        {
            lifecycle?.Park(ex.Message, ex.NextFileIndex);
            stopwatch.Stop();
            return Result(ConversionResultStatus.Parked, ex.Message);
        }
        catch (OperationCanceledException)
        {
            lifecycle?.CompleteFailure("Canceled", outputRecord);
            stopwatch.Stop();
            return Result(ConversionResultStatus.Canceled, null);
        }
        catch (Exception ex)
        {
            lifecycle?.CompleteFailure(ex.Message, outputRecord);
            stopwatch.Stop();
            return Result(
                ConversionResultStatus.Failed,
                ex.Message,
                classifyFailure?.Invoke(ex) ?? ConversionFailureKind.None);
        }

        ConversionResult Result(
            ConversionResultStatus status,
            string? error,
            ConversionFailureKind failureKind = ConversionFailureKind.None) =>
            new(
                status,
                plan.Outputs,
                error,
                stopwatch.Elapsed,
                completedFiles(),
                lifecycle?.TaskId ?? "",
                failureKind);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Clickra.Core.Processors;

/// <summary>Owns the shared task-record lifecycle around a conversion run without owning command dispatch or UI.</summary>
public sealed class ConversionTaskLifecycle
{
    private readonly string _command;
    private readonly string _inputs;
    private readonly bool _bestEffort;
    private readonly Stopwatch _stopwatch;
    private bool _stopped;

    private ConversionTaskLifecycle(
        string command,
        string inputs,
        string taskId,
        string startTime,
        bool bestEffort,
        Stopwatch stopwatch)
    {
        _command = command;
        _inputs = inputs;
        TaskId = taskId;
        StartTime = startTime;
        _bestEffort = bestEffort;
        _stopwatch = stopwatch;
    }

    public string TaskId { get; private set; }
    public string StartTime { get; }

    public static ConversionTaskLifecycle Start(
        string command,
        IReadOnlyCollection<string> files,
        string? existingTaskId = null,
        bool bestEffort = false)
    {
        string inputs = string.Join(";", files);
        string startTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var lifecycle = new ConversionTaskLifecycle(
            command,
            inputs,
            existingTaskId ?? "",
            startTime,
            bestEffort,
            Stopwatch.StartNew());

        lifecycle.RunStorage(() =>
        {
            lifecycle.TaskId = existingTaskId ?? ClickraStorage.StartTask(command, files.Count, inputs);
            ClickraStorage.SetTaskInProgress(lifecycle.TaskId);
        });
        return lifecycle;
    }

    public void CompleteSuccess(string outputPath, string? endTime = null) =>
        Complete(isSuccess: true, error: "", outputPath, endTime);

    public void CompleteFailure(string error, string outputPath, string? endTime = null) =>
        Complete(isSuccess: false, error, outputPath, endTime);

    public void Park(string reason, int nextFileIndex)
    {
        Stop();
        RunStorage(() => ClickraStorage.ParkTask(TaskId, reason, nextFileIndex));
    }

    public void Delete() => RunStorage(() => ClickraStorage.DeleteTask(TaskId));

    private void Complete(bool isSuccess, string error, string outputPath, string? endTime)
    {
        Stop();
        RunStorage(() => ClickraStorage.CompleteTask(TaskId, _command, new ClickraStorage.CompleteTaskRequest
        {
            StartTime = StartTime,
            IsSuccess = isSuccess,
            ErrorMsg = error,
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
        catch
        {
            // Native progress UI treats task persistence as advisory; conversion output remains authoritative.
        }
    }
}

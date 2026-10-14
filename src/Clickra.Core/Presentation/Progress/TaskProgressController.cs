namespace Clickra.Core.Presentation.Progress;

public enum TaskProgressPresentationStatus
{
    Preparing,
    Running,
    WaitingForInput,
    Succeeded,
    Canceled,
    Parked,
    Failed
}

public sealed record TaskProgressPresentationState(
    string Command,
    int Current,
    int Total,
    string Message,
    string Error,
    TaskProgressPresentationStatus Status,
    string TaskId = "")
{
    public bool IsCompleted => Status == TaskProgressPresentationStatus.Succeeded;
    public bool HasError => Status == TaskProgressPresentationStatus.Failed;
    public bool IsTerminal => Status is TaskProgressPresentationStatus.Succeeded
        or TaskProgressPresentationStatus.Canceled
        or TaskProgressPresentationStatus.Parked
        or TaskProgressPresentationStatus.Failed;
    public bool CanCancel => Status is TaskProgressPresentationStatus.Preparing
        or TaskProgressPresentationStatus.Running
        or TaskProgressPresentationStatus.WaitingForInput;
    public int Percent => Total > 0 ? Math.Clamp(Current * 100 / Total, 0, 100) : 0;
}

/// <summary>
/// Authoritative semantic state for a conversion-progress screen. Native and Fluent keep only
/// platform/window mechanics; status, progress, terminal outcome and task identity live here.
/// </summary>
public sealed class TaskProgressController
{
    private readonly object _sync = new();
    private TaskProgressPresentationState _state = new(
        string.Empty,
        0,
        0,
        string.Empty,
        string.Empty,
        TaskProgressPresentationStatus.Preparing);

    public TaskProgressPresentationState State
    {
        get
        {
            lock (_sync) return _state;
        }
    }

    public void Prepare(string command, int current, int total, string message)
    {
        lock (_sync)
        {
            _state = new TaskProgressPresentationState(
                command,
                Math.Max(0, current),
                Math.Max(0, total),
                message ?? string.Empty,
                string.Empty,
                TaskProgressPresentationStatus.Preparing,
                _state.TaskId);
        }
    }

    public void SetTaskId(string taskId)
    {
        lock (_sync) _state = _state with { TaskId = taskId ?? string.Empty };
    }

    public void Report(int current, int total, string message)
    {
        lock (_sync)
        {
            int effectiveTotal = total > 0 ? total : _state.Total;
            _state = _state with
            {
                Current = Math.Max(0, current),
                Total = Math.Max(0, effectiveTotal),
                Message = message ?? string.Empty,
                Error = string.Empty,
                Status = TaskProgressPresentationStatus.Running
            };
        }
    }

    public void WaitForInput(string message = "")
    {
        lock (_sync)
            _state = _state with { Message = message, Status = TaskProgressPresentationStatus.WaitingForInput };
    }

    public void ResumeRunning()
    {
        lock (_sync)
            if (!_state.IsTerminal) _state = _state with { Status = TaskProgressPresentationStatus.Running };
    }

    public void Succeed(string message)
    {
        lock (_sync)
            _state = _state with
            {
                Current = _state.Total > 0 ? _state.Total : _state.Current,
                Message = message ?? string.Empty,
                Error = string.Empty,
                Status = TaskProgressPresentationStatus.Succeeded
            };
    }

    public void Cancel(string message = "")
    {
        lock (_sync)
            _state = _state with { Message = message, Error = string.Empty, Status = TaskProgressPresentationStatus.Canceled };
    }

    public void Park(string message = "")
    {
        lock (_sync)
            _state = _state with { Message = message, Error = string.Empty, Status = TaskProgressPresentationStatus.Parked };
    }

    public void Fail(string error)
    {
        lock (_sync)
            _state = _state with { Error = error ?? string.Empty, Status = TaskProgressPresentationStatus.Failed };
    }
}

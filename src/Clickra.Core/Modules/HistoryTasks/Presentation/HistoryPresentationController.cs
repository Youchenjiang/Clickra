using Clickra.Core.Modules.HistoryTasks.Application;
using Clickra.Core.Application;

namespace Clickra.Core.Modules.HistoryTasks.Presentation;

public sealed record ParkedTaskResumePresentation(string TaskId, string Command, List<string> Files, int StartIndex);

/// <summary>
/// Authoritative presentation owner for History/Overview task state. Renderers keep only visual
/// concerns such as row geometry, scroll position and expansion animation.
/// </summary>
public sealed class HistoryPresentationController : IHistoryPresentationController
{
    private HistoryFeed _feed = HistoryFeed.Empty;

    public HistoryFeed Feed => _feed;

    public IReadOnlyList<HistoryItem> ActiveItems => _feed.OfKind(HistoryItemKind.Active);

    public IReadOnlyList<HistoryItem> ParkedItems => _feed.OfKind(HistoryItemKind.Parked);

    public IReadOnlyList<HistoryItem> CompletedItems => _feed.OfKind(HistoryItemKind.Completed);

    public int CompletedCount => _feed.CompletedCount;

    public int SuccessCount => _feed.SuccessCount;

    public int FailedCount => _feed.FailedCount;

    public int ExpiringSoonCount => _feed.ExpiringSoonCount;

    public bool IsEmpty => ActiveItems.Count == 0 && ParkedItems.Count == 0 && CompletedItems.Count == 0;

    public int RetentionStepDays => ParkedTaskController.RetentionStepDays;

    public void Refresh(string language, int completedLimit = 50)
    {
        _feed = HistoryFeed.Load(language, completedLimit);
    }

    public void Reset() => _feed = HistoryFeed.Empty;

    public bool AdjustRetention(string taskId, int deltaDays)
    {
        if (string.IsNullOrWhiteSpace(taskId)) return false;
        return ParkedTaskController.AdjustRetention(taskId, deltaDays).HasValue;
    }

    public void ResetRetention(string taskId)
    {
        if (!string.IsNullOrWhiteSpace(taskId))
            ParkedTaskController.ResetRetention(taskId);
    }

    public void CancelParked(string taskId)
    {
        if (!string.IsNullOrWhiteSpace(taskId))
            ParkedTaskController.Cancel(taskId);
    }

    public void ClearCompleted() => HistoryController.Clear();

    public ParkedTaskResumePresentation? ClaimForResume(string taskId)
    {
        ParkedTaskController.ResumeInfo? resumed = ParkedTaskController.ClaimForResume(taskId);
        return resumed is null
            ? null
            : new ParkedTaskResumePresentation(resumed.TaskId, resumed.Command, resumed.Files, resumed.StartIndex);
    }

    public string GetLogPath(string? preferredDataDirectory = null) => HistoryController.GetLogPath(preferredDataDirectory);
}

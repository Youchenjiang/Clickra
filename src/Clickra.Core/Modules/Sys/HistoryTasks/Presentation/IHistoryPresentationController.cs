namespace Clickra.Core.Modules.Sys.HistoryTasks.Presentation;

/// <summary>Instance contract for UI history state and parked-task actions.</summary>
public interface IHistoryPresentationController
{
    HistoryFeed Feed { get; }
    IReadOnlyList<HistoryItem> ActiveItems { get; }
    IReadOnlyList<HistoryItem> ParkedItems { get; }
    IReadOnlyList<HistoryItem> CompletedItems { get; }
    int CompletedCount { get; }
    int SuccessCount { get; }
    int FailedCount { get; }
    int ExpiringSoonCount { get; }
    bool IsEmpty { get; }
    void Refresh(string language, int completedLimit = 50);
    void Reset();
    void CancelParked(string taskId);
    void ReturnToParked(string taskId, string reason, int nextIndex);
    void ClearCompleted();
    ParkedTaskResumePresentation? ClaimForResume(string taskId);
    string GetLogPath(string? preferredDataDirectory = null);
}

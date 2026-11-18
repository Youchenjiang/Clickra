using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Clickra.Core.Modules.Sys.HistoryTasks.Application;
using Clickra.Core.Modules.Sys.HistoryTasks.Presentation;
using Clickra.Core.Modules.Sys.Settings.Application;

namespace Clickra.Desktop.ViewModels;

public enum HistoryFilter
{
    All,
    Success,
    Failed
}

/// <summary>Finished rows of one day under a 今天／昨天／date heading (spec 4.2).</summary>
public sealed class HistoryDayGroup(string title, IReadOnlyList<HistoryRowViewModel> rows)
{
    public string Title { get; } = title;
    public IReadOnlyList<HistoryRowViewModel> Rows { get; } = rows;
}

/// <summary>A parked task to continue: ClaimForResume already moved it out of 待繼續.</summary>
public sealed class ResumeRequestEventArgs(ParkedTaskResumePresentation task) : EventArgs
{
    public ParkedTaskResumePresentation Task { get; } = task;
}

/// <summary>
/// History page (spec 4.2): 進行中 pinned on top, then 待繼續, then finished tasks grouped by day.
/// The filter 「全部｜✓ n｜✕ n」 applies to the finished part only; canceled tasks count only in 全部.
/// </summary>
public sealed class HistoryViewModel : HistoryRowHostBase
{
    private readonly HistoryPresentationController _presentation = new();
    private HistoryFilter _filter = HistoryFilter.All;
    private HistoryRowViewModel? _expandedRow;
    private readonly List<HistoryRowViewModel> _completed = [];
    private IConversionSession? _session;
    private string? _expandAfterLoad;

    public ObservableCollection<HistoryRowViewModel> ActiveRows { get; } = [];
    public ObservableCollection<HistoryRowViewModel> ParkedRows { get; } = [];
    public ObservableCollection<HistoryDayGroup> Groups { get; } = [];

    /// <summary>Raised by ▶ 繼續 after the task was claimed; the window runs it.</summary>
    public event EventHandler<ResumeRequestEventArgs>? ResumeRequested;

    /// <summary>Asks the user to confirm (title, body, confirm text, is destructive). Set by the page.</summary>
    public Func<string, string, string, bool, Task<bool>>? Confirm { get; set; }

    public HistoryViewModel()
    {
        Refresh();
    }

    /// <summary>The conversion run by this window; its row gets live progress, ⏸ and ■.</summary>
    public IConversionSession? Session
    {
        get => _session;
        set
        {
            if (_session is not null)
            {
                _session.Changed -= OnSessionChanged;
                _session.Finished -= OnSessionFinished;
            }
            _session = value;
            if (_session is not null)
            {
                _session.Changed += OnSessionChanged;
                _session.Finished += OnSessionFinished;
            }
        }
    }

    public HistoryFilter Filter
    {
        get => _filter;
        set
        {
            if (_filter == value) return;
            _filter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilterIndex));
            ApplyFilter();
        }
    }

    /// <summary>Selected segment: 0 全部, 1 ✓, 2 ✕.</summary>
    public int FilterIndex
    {
        get => (int)_filter;
        set => Filter = value is >= 0 and <= 2 ? (HistoryFilter)value : HistoryFilter.All;
    }

    public int SuccessCount => _completed.Count(r => r.IsSuccess);
    public int FailedCount => _completed.Count(r => r.IsFailed);

    public bool HasActive => ActiveRows.Count > 0;
    public bool HasParked => ParkedRows.Count > 0;
    public bool HasCompleted => _completed.Count > 0;
    public bool HasGroups => Groups.Count > 0;
    /// <summary>Finished tasks exist but none match the filter.</summary>
    public bool IsFilterEmpty => HasCompleted && !HasGroups;
    public bool IsEmpty => !HasCompleted && !HasActive && !HasParked;

    private int _refreshVersion;
    private string _signature = "";

    /// <summary>Reloads history now, on the calling (UI) thread.</summary>
    public void Refresh()
    {
        _refreshVersion++;
        HistoryFeed? feed = null;
        try
        {
            feed = HistoryFeed.Load(SettingsController.Language, completedLimit: 100);
        }
        catch
        {
            // Graceful fallback
        }
        Apply(feed, force: true);
    }

    /// <summary>
    /// Reloads history without blocking the UI: the file is read on a worker thread, and the list is only
    /// rebuilt after <paramref name="notBefore"/> (so it never lands in the middle of the tab transition)
    /// and only when something changed.
    /// </summary>
    public async Task RefreshInBackground(TimeSpan notBefore)
    {
        int version = ++_refreshVersion;
        Task delay = Task.Delay(notBefore);
        HistoryFeed? feed = null;
        try
        {
            feed = await Task.Run(() => HistoryFeed.Load(SettingsController.Language, completedLimit: 100));
        }
        catch
        {
            // Graceful fallback
        }
        await delay;
        if (version != _refreshVersion || feed is null) return;
        Apply(feed, force: false);
    }

    /// <summary>Opens History with this task expanded (a row pressed in 最近處理).</summary>
    public void ShowTask(string taskId)
    {
        _expandAfterLoad = taskId;
        Refresh();
    }

    private void Apply(HistoryFeed? feed, bool force)
    {
        var active = feed?.OfKind(HistoryItemKind.Active).ToList() ?? [];
        var parked = feed?.OfKind(HistoryItemKind.Parked).ToList() ?? [];
        var completed = feed?.OfKind(HistoryItemKind.Completed).ToList() ?? [];
        string signature = string.Join("|", active.Concat(parked).Concat(completed)
            .Select(i => $"{i.Entry.Id}:{i.Entry.Status}:{i.Entry.CurrentIndex}:{i.Entry.EndTime}"));
        if (!force && signature == _signature) return;
        _signature = signature;

        string? keepExpanded = _expandAfterLoad ?? _expandedRow?.Id;
        _expandAfterLoad = null;
        _expandedRow = null;

        ActiveRows.Clear();
        ParkedRows.Clear();
        _completed.Clear();
        foreach (var item in active) ActiveRows.Add(new HistoryRowViewModel(item, this));
        foreach (var item in parked) ParkedRows.Add(new HistoryRowViewModel(item, this));
        foreach (var item in completed) _completed.Add(new HistoryRowViewModel(item, this));
        OnSessionChanged(null, EventArgs.Empty);

        if (keepExpanded is not null)
        {
            HistoryRowViewModel? row = ActiveRows.Concat(ParkedRows).Concat(_completed).FirstOrDefault(r => r.Id == keepExpanded);
            if (row is not null)
            {
                // The task must be visible: a filter that hides it goes back to 全部.
                if ((_filter == HistoryFilter.Success && !row.IsSuccess) || (_filter == HistoryFilter.Failed && !row.IsFailed))
                {
                    _filter = HistoryFilter.All;
                    OnPropertyChanged(nameof(Filter));
                    OnPropertyChanged(nameof(FilterIndex));
                }
                row.IsExpanded = true;
                _expandedRow = row;
            }
        }

        OnPropertyChanged(nameof(SuccessCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(HasActive));
        OnPropertyChanged(nameof(HasParked));
        OnPropertyChanged(nameof(HasCompleted));
        OnPropertyChanged(nameof(IsEmpty));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Groups.Clear();
        IEnumerable<HistoryRowViewModel> rows = _filter switch
        {
            HistoryFilter.Success => _completed.Where(r => r.IsSuccess),
            HistoryFilter.Failed => _completed.Where(r => r.IsFailed),
            _ => _completed,
        };
        foreach (var group in rows.GroupBy(r => r.DayGroup))
        {
            Groups.Add(new HistoryDayGroup(group.Key, group.ToList()));
        }
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(IsFilterEmpty));
    }

    // ----- live progress of this window's conversion -----

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (_session is not { IsRunning: true, TaskId: { } id })
        {
            foreach (var row in ActiveRows) row.RefreshButtons();
            return;
        }
        HistoryRowViewModel? running = ActiveRows.FirstOrDefault(r => r.Id == id);
        if (running is null)
        {
            // Core just recorded the task: load it so 進行中 shows it.
            if (sender is not null) _ = RefreshInBackground(TimeSpan.Zero);
            return;
        }
        running.UpdateRunning(_session.Percent, _session.FileIndex, _session.FileCount);
    }

    private void OnSessionFinished(object? sender, EventArgs e) => Refresh();

    // ----- IHistoryRowHost -----

    public override void ToggleExpand(HistoryRowViewModel row)
    {
        DismissNoticeExcept(row);
        if (_expandedRow is not null && !ReferenceEquals(_expandedRow, row))
        {
            _expandedRow.IsExpanded = false;
        }
        row.IsExpanded = !row.IsExpanded;
        _expandedRow = row.IsExpanded ? row : null;
    }

    public override bool CanPauseRow(HistoryRowViewModel row) =>
        _session is { IsRunning: true, CanPause: true } s && s.TaskId == row.Id;

    public override bool CanStopRow(HistoryRowViewModel row) =>
        _session is { IsRunning: true } s && s.TaskId == row.Id;

    public override void Pause(HistoryRowViewModel row)
    {
        DismissNoticeExcept(row);
        if (CanPauseRow(row)) _session!.RequestPause();
        row.RefreshButtons();
    }

    public override async void Stop(HistoryRowViewModel row)
    {
        DismissNoticeExcept(row);
        if (row.IsRunning)
        {
            if (CanStopRow(row)) _session!.Stop();
            return;
        }
        if (!row.IsParked) return;

        // Stopping a parked task cannot be undone, so ask first.
        if (Confirm is not null &&
            !await Confirm("停止這個工作？", $"剩下的檔案不會處理，這筆會移到下方的紀錄。", "停止", false))
            return;
        try { _presentation.CancelParked(row.Id); }
        catch { /* best effort */ }
        Refresh();
    }

    public override void Resume(HistoryRowViewModel row)
    {
        DismissNoticeExcept(row);
        if (IsBusy?.Invoke() == true)
        {
            row.ShowNotice("正在處理其他檔案，做完後再繼續");
            return;
        }
        ParkedTaskResumePresentation? task = null;
        try { task = _presentation.ClaimForResume(row.Id); }
        catch { /* handled below */ }
        if (task is null)
        {
            row.ShowNotice("這個工作已無法繼續，可能已過期或已在別處處理");
            Refresh();
            return;
        }
        ResumeRequested?.Invoke(this, new ResumeRequestEventArgs(task));
    }

    /// <summary>⋯ › 清除歷史…, after the confirm dialog. Converted files are kept.</summary>
    public async Task ClearHistory()
    {
        if (Confirm is not null && !await Confirm("清除全部歷史？", "轉好的檔案不會被刪除。", "清除", true))
            return;
        try
        {
            _presentation.ClearCompleted();
        }
        catch
        {
            // Best effort clear
        }
        Refresh();
    }
}

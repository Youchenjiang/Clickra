using System;
using Clickra.Desktop.L10n;

namespace Clickra.Desktop.ViewModels;

/// <summary>Top-level pages reachable from the three tabs.</summary>
public enum MainPage
{
    Convert,
    History,
    Settings,
}

/// <summary>Places on the Settings page that other pages can link to.</summary>
public enum SettingsSection
{
    ContextMenu,
}

public sealed class SettingsSectionEventArgs(SettingsSection section) : EventArgs
{
    public SettingsSection Section { get; } = section;
}

/// <summary>Window-level state: which tab is shown and the tab labels.</summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private MainPage _currentPage = MainPage.Convert;

    public ConvertViewModel Convert { get; } = new();
    public HistoryViewModel History { get; } = new();
    public SettingsViewModel Settings { get; } = new();

    public MainWindowViewModel()
    {
        History.Session = Convert;
        History.IsBusy = () => Convert.IsExecuting;
        Settings.IsBusy = () => Convert.IsExecuting;
        History.RerunRequested += (_, e) => Rerun(e);
        Convert.RerunRequested += (_, e) => Rerun(e);
        History.ResumeRequested += async (_, e) => await Convert.ResumeParkedAsync(e.Task);
        Convert.ShowTaskRequested += (_, e) =>
        {
            History.ShowTask(e.TaskId);
            CurrentPage = MainPage.History;
        };
    }

    /// <summary>再轉一次: open the convert page on the same action with these files; the user presses 開始.</summary>
    private void Rerun(RerunRequestEventArgs e)
    {
        if (Convert.PrepareRerun(e.Command, e.Files)) CurrentPage = MainPage.Convert;
    }

    public string ConvertTabLabel { get; } = Strings.T("ui_tab_convert");
    public string HistoryTabLabel { get; } = Strings.T("ui_tab_history");
    public string SettingsTabLabel { get; } = Strings.T("ui_tab_settings");

    public MainPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (_currentPage == value) return;
            _currentPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConvertPage));
            OnPropertyChanged(nameof(IsHistoryPage));
            OnPropertyChanged(nameof(IsSettingsPage));
        }
    }

    /// <summary>Raised when a page asks to show a specific place on the Settings page.</summary>
    public event EventHandler<SettingsSectionEventArgs>? SettingsSectionRequested;

    /// <summary>Switches to Settings and asks it to scroll to and highlight <paramref name="section"/>.</summary>
    public void OpenSettings(SettingsSection section)
    {
        CurrentPage = MainPage.Settings;
        SettingsSectionRequested?.Invoke(this, new SettingsSectionEventArgs(section));
    }

    public bool IsConvertPage
    {
        get => CurrentPage == MainPage.Convert;
        set { if (value) CurrentPage = MainPage.Convert; }
    }

    public bool IsHistoryPage
    {
        get => CurrentPage == MainPage.History;
        set { if (value) CurrentPage = MainPage.History; }
    }

    public bool IsSettingsPage
    {
        get => CurrentPage == MainPage.Settings;
        set { if (value) CurrentPage = MainPage.Settings; }
    }
}

using Clickra.Core;

namespace Clickra.Desktop.ViewModels;

/// <summary>Top-level pages reachable from the three tabs.</summary>
public enum MainPage
{
    Convert,
    History,
    Settings,
}

/// <summary>Window-level state: which tab is shown and the tab labels.</summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private MainPage _currentPage = MainPage.Convert;

    public string ConvertTabLabel { get; } = Localization.T("tab_convert");
    public string HistoryTabLabel { get; } = Localization.T("tab_history");
    public string SettingsTabLabel { get; } = Localization.T("tab_settings");

    public MainPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            OnPropertyChanged(nameof(IsConvertPage));
            OnPropertyChanged(nameof(IsHistoryPage));
            OnPropertyChanged(nameof(IsSettingsPage));
        }
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

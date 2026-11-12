using System;
using System.Runtime.InteropServices;
using Avalonia.Styling;

namespace Clickra.Desktop.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private int _selectedNavigationIndex;
    private string _currentThemeName = "System";

    public MainWindowViewModel()
    {
        Title = "Clickra";
        PlatformBanner = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})";
        VersionInfo = "v4.0.0-preview.1 (NativeAOT)";
    }

    public string Title { get; }
    public string PlatformBanner { get; }
    public string VersionInfo { get; }

    public int SelectedNavigationIndex
    {
        get => _selectedNavigationIndex;
        set
        {
            if (SetProperty(ref _selectedNavigationIndex, value))
            {
                OnPropertyChanged(nameof(IsConvertSelected));
                OnPropertyChanged(nameof(IsHistorySelected));
                OnPropertyChanged(nameof(IsSettingsSelected));
            }
        }
    }

    public bool IsConvertSelected => SelectedNavigationIndex == 0;
    public bool IsHistorySelected => SelectedNavigationIndex == 1;
    public bool IsSettingsSelected => SelectedNavigationIndex == 2;

    public string CurrentThemeName
    {
        get => _currentThemeName;
        private set => SetProperty(ref _currentThemeName, value);
    }

    public void SelectNavigation(int index)
    {
        SelectedNavigationIndex = index;
    }

    public void ToggleTheme(Avalonia.Application app)
    {
        if (app.RequestedThemeVariant == ThemeVariant.Dark)
        {
            app.RequestedThemeVariant = ThemeVariant.Light;
            CurrentThemeName = "Light";
        }
        else if (app.RequestedThemeVariant == ThemeVariant.Light)
        {
            app.RequestedThemeVariant = ThemeVariant.Default;
            CurrentThemeName = "System";
        }
        else
        {
            app.RequestedThemeVariant = ThemeVariant.Dark;
            CurrentThemeName = "Dark";
        }
    }
}

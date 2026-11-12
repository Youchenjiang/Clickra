using Avalonia.Controls;
using Avalonia.Interactivity;
using Clickra.Desktop.ViewModels;

namespace Clickra.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnNavConvertClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel?.SelectNavigation(0);
    }

    private void OnNavHistoryClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel?.SelectNavigation(1);
    }

    private void OnNavSettingsClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel?.SelectNavigation(2);
    }

    private void OnThemeToggleClicked(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current != null)
        {
            ViewModel?.ToggleTheme(Avalonia.Application.Current);
        }
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clickra_Fluent;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        NavView.SelectionChanged += NavView_SelectionChanged;
        NavView.SelectedItem = NavView.MenuItems[0];
        ShowPanel("Overview");
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            ShowPanel(tag);
        }
    }

    private void ShowPanel(string name)
    {
        OverviewPanel.Visibility = name == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        ConvertPanel.Visibility = name == "Convert" ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = name == "History" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = name == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = name == "About" ? Visibility.Visible : Visibility.Collapsed;
    }
}
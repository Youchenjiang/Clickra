using Avalonia.Controls;
using Clickra.Desktop.ViewModels;

namespace Clickra.Desktop.Views.Pages;

public partial class HistoryPage : UserControl
{
    public HistoryPage()
    {
        InitializeComponent();

        GoConvertButton.Click += (_, _) =>
        {
            if (VisualRoot is Window { DataContext: MainWindowViewModel mainVm })
            {
                mainVm.CurrentPage = MainPage.Convert;
            }
        };

        FilterAllButton.Click += (_, _) => GetViewModel()?.Refresh();
        FilterSuccessButton.Click += (_, _) =>
        {
            if (GetViewModel() is { } vm) vm.Filter = HistoryFilter.Success;
        };
        FilterFailedButton.Click += (_, _) =>
        {
            if (GetViewModel() is { } vm) vm.Filter = HistoryFilter.Failed;
        };

        ClearHistoryButton.Click += (_, _) => GetViewModel()?.ClearHistory();
    }

    private HistoryViewModel? GetViewModel() => DataContext as HistoryViewModel;
}

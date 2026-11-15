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
            if (DataContext is MainWindowViewModel vm) vm.CurrentPage = MainPage.Convert;
        };
    }
}

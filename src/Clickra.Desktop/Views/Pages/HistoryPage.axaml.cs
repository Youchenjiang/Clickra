using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Clickra.Desktop.ViewModels;

namespace Clickra.Desktop.Views.Pages;

public partial class HistoryPage : UserControl
{
    public HistoryPage()
    {
        InitializeComponent();

        GoConvertButton.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel mainVm })
            {
                mainVm.CurrentPage = MainPage.Convert;
            }
        };

        // ⋯ menu: 清除歷史… (confirmed in a dialog).
        var clear = new MenuItem { Header = "清除歷史…" };
        clear.Click += (_, _) => GetViewModel()?.ClearHistory();
        MoreButton.Flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight, Items = { clear } };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (GetViewModel() is { } vm)
        {
            vm.Confirm = async (title, body, confirm, danger) =>
                TopLevel.GetTopLevel(this) is Window owner && await Dialogs.ConfirmAsync(owner, title, body, confirm, danger);
        }
    }

    private HistoryViewModel? GetViewModel() => DataContext as HistoryViewModel;
}

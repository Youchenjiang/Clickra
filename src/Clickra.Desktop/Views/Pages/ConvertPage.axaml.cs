using Avalonia.Controls;
using Clickra.Desktop.ViewModels;

namespace Clickra.Desktop.Views.Pages;

public partial class ConvertPage : UserControl
{
    public ConvertPage()
    {
        InitializeComponent();

        // Navigation link: opens Settings, scrolls to the right-click menu group and highlights it.
        MenuSettingsLink.Click += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm) vm.OpenSettings(SettingsSection.ContextMenu);
        };
    }
}

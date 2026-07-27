using Microsoft.UI.Xaml;
using System.IO;

namespace Clickra_Fluent;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        string iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);
        RootFrame.Navigate(typeof(MainPage));
    }
}

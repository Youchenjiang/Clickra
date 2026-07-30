using Microsoft.UI.Xaml;
using System.IO;

namespace Clickra_Fluent;

public sealed partial class MainWindow : Window
{
    public MainWindow(string launchArguments = "")
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        string iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);
        bool progressMode = !string.IsNullOrWhiteSpace(launchArguments);
        if (progressMode)
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(720, 440));
        }
        RootFrame.Navigate(progressMode ? typeof(TaskProgressPage) : typeof(MainPage), launchArguments);
    }
}

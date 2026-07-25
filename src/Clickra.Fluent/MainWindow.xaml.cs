using Microsoft.UI.Xaml;

namespace Clickra_Fluent;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.png");
        RootFrame.Navigate(typeof(MainPage));
    }
}
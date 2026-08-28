using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.IO;

namespace Clickra_Fluent;

public sealed partial class MainWindow : Window
{
    /// <summary>主導覽 Frame（供 App 處理單一實例導向時讀取目前頁面）。</summary>
    // skipcq: CS-R1093 — MainFrame must be instance-level to access the XAML-generated RootFrame field.
    public Frame MainFrame => RootFrame;

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
            // 一般轉換用緊湊尺寸（內容填滿視窗）；分割預覽需要整片空間時
            // 由 TaskProgressPage 暫時放大（TaskProgressPage.PromptSplitAsync）。
            AppWindow.Resize(TaskProgressPage.CompactWindowSize);
        }
        RootFrame.Navigate(progressMode ? typeof(TaskProgressPage) : typeof(MainPage), launchArguments);
        if (progressMode && RootFrame.Content is TaskProgressPage taskPage)
        {
            // 記錄宿主視窗，讓任務完成時關閉「自己的」視窗而非主視窗。
            taskPage.HostWindow = this;
        }
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Clickra.Desktop.Views;

public partial class MainWindow : Window
{
    // Width reserved on the right for the Windows caption buttons when the top row
    // is drawn inside the title bar.
    private const double WindowsCaptionButtonsWidth = 138;

    // Width reserved on the left for the macOS traffic-light buttons.
    private const double MacTrafficLightsWidth = 78;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureTitleBar();
    }

    /// <summary>
    /// Section 3.1: on Windows and macOS the tabs share the title bar row; on Linux the
    /// system title bar stays and the tabs sit right under it.
    /// </summary>
    private void ConfigureTitleBar()
    {
        if (OperatingSystem.IsLinux()) return;

        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
        ExtendClientAreaTitleBarHeightHint = 48;

        this.GetObservable(IsExtendedIntoWindowDecorationsProperty).Subscribe(new TitleBarObserver(this));
    }

    private void ApplyTitleBarInsets(bool extended)
    {
        if (!extended)
        {
            TopBar.Margin = default;
            return;
        }

        TopBar.Margin = OperatingSystem.IsMacOS()
            ? new Thickness(MacTrafficLightsWidth - 16, 0, 0, 0)
            : new Thickness(0, 0, WindowsCaptionButtonsWidth, 0);
    }

    private sealed class TitleBarObserver(MainWindow owner) : IObserver<bool>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(bool value) => owner.ApplyTitleBarInsets(value);
    }
}

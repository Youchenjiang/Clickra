using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Clickra.Desktop.Controls;

namespace Clickra.Desktop.Views;

public partial class MainWindow : Window
{
    // Width reserved on the right for the Windows caption buttons when the top row
    // is drawn inside the title bar.
    private const double WindowsCaptionButtonsWidth = 138;

    // Width reserved on the left for the macOS traffic-light buttons.
    private const double MacTrafficLightsWidth = 78;

    private IDisposable? _backgroundBinding;

    // Section 2: the window opens at most 960 x 660, but never takes more than about
    // 70% of the screen width and 80% of its height, so it does not fill small screens
    // such as 1920 x 1080 at 150% scaling.
    private const double DefaultWidth = 960;
    private const double DefaultHeight = 660;
    private const double MaxScreenWidthShare = 0.7;
    private const double MaxScreenHeightShare = 0.8;

    public MainWindow()
    {
        InitializeComponent();

        // Switching tabs: the new page fades in and slides 16 px sideways, from the side of the tab it came
        // from (tab to the right -> enters from the right), matching the left-right tab row (spec section 7).
        Control[] pages = [ConvertView, HistoryView, SettingsView];
        int current = Array.FindIndex(pages, p => p.IsVisible);
        for (int i = 0; i < pages.Length; i++)
        {
            int index = i;
            pages[i].PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty || e.NewValue is not true) return;
                double fromX = ComputeSlideOffset(current, index);
                current = index;
                Motion.Enter(pages[index], fromX, 0);
            };
        }
        ConfigureBackdrop();
        ConfigureTitleBar();
    }

    private static double ComputeSlideOffset(int current, int index)
    {
        if (current < 0 || index == current) return 0;
        return index > current ? 16 : -16;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitToScreen();
    }

    private void FitToScreen()
    {
        Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        double scale = screen.Scaling;
        PixelRect area = screen.WorkingArea;
        double width = Math.Clamp(Math.Floor(area.Width / scale * MaxScreenWidthShare), MinWidth, DefaultWidth);
        double height = Math.Clamp(Math.Floor(area.Height / scale * MaxScreenHeightShare), MinHeight, DefaultHeight);

        Width = width;
        Height = height;
        Position = new PixelPoint(
            area.X + (int)((area.Width - width * scale) / 2),
            area.Y + (int)((area.Height - height * scale) / 2));
    }

    /// <summary>
    /// Section 7: the window base uses the system backdrop where available (Mica on Windows 11,
    /// a translucent blur on macOS) and falls back to the solid Clk.Bg color elsewhere
    /// (Windows 10, Linux). Content sections stay solid on top for readable contrast.
    /// </summary>
    private void ConfigureBackdrop()
    {
        if (!OperatingSystem.IsLinux())
        {
            TransparencyLevelHint = new[]
            {
                WindowTransparencyLevel.Mica,
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.None,
            };
        }

        ApplyBackdrop(ActualTransparencyLevel);
        this.GetObservable(ActualTransparencyLevelProperty).Subscribe(new Observer<WindowTransparencyLevel>(ApplyBackdrop));
    }

    private void ApplyBackdrop(WindowTransparencyLevel level)
    {
        _backgroundBinding?.Dispose();
        _backgroundBinding = null;

        if (level == WindowTransparencyLevel.Mica)
        {
            Background = Brushes.Transparent;
            return;
        }

        string key = level == WindowTransparencyLevel.AcrylicBlur ? "Clk.BgTranslucent" : "Clk.Bg";
        _backgroundBinding = this.Bind(BackgroundProperty, this.GetResourceObservable(key));
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

        this.GetObservable(IsExtendedIntoWindowDecorationsProperty).Subscribe(new Observer<bool>(extended => ApplyTitleBarInsets(TopBar, extended)));
    }

    private static void ApplyTitleBarInsets(Control topBar, bool extended)
    {
        if (!extended)
        {
            topBar.Margin = default;
            return;
        }

        topBar.Margin = OperatingSystem.IsMacOS()
            ? new Thickness(MacTrafficLightsWidth - 16, 0, 0, 0)
            : new Thickness(0, 0, WindowsCaptionButtonsWidth, 0);
    }

    private sealed class Observer<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => onNext(value);
    }
}

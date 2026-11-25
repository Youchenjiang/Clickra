using System;
using System.Linq;
using Avalonia;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Platform;
using Clickra.Desktop.Controls;
using Clickra.Desktop.ViewModels;
using Clickra.Desktop.L10n;

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
        // The interface language picks the font: Noto Sans TC for Traditional Chinese, the system UI font
        // elsewhere so kanji, Simplified hanzi and Hangul get their own shapes (localization plan 3.4).
        FontFamily = Avalonia.Media.FontFamily.Parse(Strings.FontFamily);

        // Switching tabs: the new page fades in and slides 16 px sideways, from the side of the tab it came
        // from (tab to the right -> enters from the right), matching the left-right tab row (spec section 7).
        Control[] pages = [ConvertView, HistoryView, SettingsView];
        int current = Array.FindIndex(pages, p => p.IsVisible);
        for (int i = 0; i < pages.Length; i++)
        {
            int index = i;
            pages[i].PropertyChanged += (s, e) =>
            {
                if (_prewarming || e.Property != IsVisibleProperty || e.NewValue is not true) return;
                double fromX = ComputeSlideOffset(current, index);
                current = index;
                Motion.Enter(pages[index], fromX, 0);
                if (index == 1 && DataContext is MainWindowViewModel vm)
                {
                    // Off the UI thread, applied after the slide-in, so the transition stays smooth.
                    _ = vm.History.RefreshInBackground(Motion.Normal);
                }
            };
        }
        ConfigureBackdrop();
        ConfigureTitleBar();

        AddHandler(DragDrop.DragEnterEvent, OnWindowDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver);
        AddHandler(DragDrop.DropEvent, OnWindowDrop);
    }

    private static double ComputeSlideOffset(int current, int index)
    {
        if (current < 0 || index == current) return 0;
        return index > current ? 16 : -16;
    }

    private void OnWindowDragEnter(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel { Convert.IsExecuting: false } &&
            e.DataTransfer.TryGetFiles()?.Any() == true)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel { Convert.IsExecuting: false } &&
            e.DataTransfer.TryGetFiles()?.Any() == true)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void OnWindowDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { Convert.IsExecuting: false } vm) return;
        List<string> paths = e.DataTransfer.TryGetFiles()?.Select(f => f.Path.LocalPath).ToList() ?? [];
        if (paths.Count == 0) return;
        e.Handled = true;
        vm.CurrentPage = MainPage.Convert;
        vm.Convert.AddFiles(paths);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (!RestoreBounds()) FitToScreen();
        TrackNormalBounds();
        PositionChanged += (_, _) => TrackNormalBounds();
        SizeChanged += (_, _) => TrackNormalBounds();
        Dispatcher.UIThread.Post(PrewarmHiddenPages, DispatcherPriority.Background);
    }

    private bool _prewarming;

    /// <summary>
    /// A page's first layout (templates, text shaping) took about 0.25 s for Settings, so the first switch to a
    /// tab stalled the slide-in. Right after start-up, lay out the hidden pages once while they stay invisible
    /// (opacity 0, not hit-testable), then hide them again; later switches only re-arrange.
    /// </summary>
    private void PrewarmHiddenPages()
    {
        var overrides = new List<IDisposable?>();
        _prewarming = true;
        try
        {
            foreach (Control page in new Control[] { ConvertView, HistoryView, SettingsView })
            {
                if (page.IsVisible) continue;
                overrides.Add(page.SetValue(OpacityProperty, 0d, BindingPriority.Animation));
                overrides.Add(page.SetValue(IsHitTestVisibleProperty, false, BindingPriority.Animation));
                overrides.Add(page.SetValue(IsVisibleProperty, true, BindingPriority.Animation));
            }
            UpdateLayout();
        }
        finally
        {
            // Reverse order: IsVisible first, so the page is hidden again before it could become opaque.
            for (int i = overrides.Count - 1; i >= 0; i--) overrides[i]?.Dispose();
            _prewarming = false;
        }
    }

    /// <summary>
    /// Reopens where the window was last closed (spec branch 7: 記住視窗大小與位置), but only when that place
    /// is still mostly on a connected screen; otherwise the default size and centre are used.
    /// </summary>
    private bool RestoreBounds()
    {
        string saved = "";
        try { saved = Clickra.Core.Modules.Sys.Settings.Application.SettingsController.DesktopWindowBounds; }
        catch { return false; }
        string[] parts = saved.Split(',');
        if (parts.Length < 4 ||
            !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y) ||
            !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double w) ||
            !double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double h))
            return false;

        Screen? screen = Screens.All.FirstOrDefault(sc => sc.WorkingArea.Contains(new PixelPoint(x + 80, y + 20)));
        if (screen is null) return false;
        double scale = screen.Scaling;
        PixelRect area = screen.WorkingArea;
        w = Math.Clamp(w, MinWidth, area.Width / scale);
        h = Math.Clamp(h, MinHeight, area.Height / scale);
        Width = w;
        Height = h;
        Position = new PixelPoint(
            Math.Clamp(x, area.X, Math.Max(area.X, area.Right - (int)(w * scale))),
            Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - (int)(h * scale))));
        if (parts.Length > 4 && parts[4] == "1") WindowState = WindowState.Maximized;
        return true;
    }

    private void SaveBounds()
    {
        try
        {
            // Maximized: keep the last normal size so un-maximizing next time returns to it.
            bool maximized = WindowState == WindowState.Maximized;
            int maxFlag = maximized ? 1 : 0;
            string value = maximized && _normalBounds is { } nb
                ? $"{nb.X},{nb.Y},{nb.W.ToString(System.Globalization.CultureInfo.InvariantCulture)},{nb.H.ToString(System.Globalization.CultureInfo.InvariantCulture)},1"
                : $"{Position.X},{Position.Y},{Width.ToString(System.Globalization.CultureInfo.InvariantCulture)},{Height.ToString(System.Globalization.CultureInfo.InvariantCulture)},{maxFlag}";
            Clickra.Core.Modules.Sys.Settings.Application.SettingsController.DesktopWindowBounds = value;
        }
        catch
        {
            // best effort
        }
    }

    private (int X, int Y, double W, double H)? _normalBounds;

    private void TrackNormalBounds()
    {
        if (WindowState == WindowState.Normal) _normalBounds = (Position.X, Position.Y, Width, Height);
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

        // Avalonia 12 removed ExtendClientAreaChromeHints. On Windows it now draws the title bar itself when the
        // client area is extended; Styles/WindowDecorations.axaml (WindowDecorationsTheme) keeps only the caption
        // buttons so the drawn title text no longer prints over our brand row.
        WindowDecorations = WindowDecorations.Full;
        ExtendClientAreaToDecorationsHint = true;
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

    // ===== Closing while files are being processed (spec 4.5) =====

    private bool _closeConfirmed;
    private bool _askingClose;
    private TrayIcon? _tray;
    private NativeMenuItem? _trayProgress;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Settings.RestartRequested -= OnRestartRequested;
            vm.Settings.RestartRequested += OnRestartRequested;
        }
    }

    /// <summary>立即重新開啟 after a language change: start a fresh Clickra, then close this one.</summary>
    private void OnRestartRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.Convert.IsExecuting) return;
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false })?.Dispose();
        }
        catch
        {
            return; // could not start a new one: keep this window
        }
        CloseForGood();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || e.CloseReason == WindowCloseReason.OSShutdown) return;
        if (DataContext is not MainWindowViewModel vm || !vm.Convert.IsExecuting) return;

        e.Cancel = true;
        if (_askingClose) return;

        CloseWhileRunning action = CloseWhileRunningSetting.Value;
        if (action == CloseWhileRunning.Ask)
        {
            _askingClose = true;
            CloseChoice? choice;
            try { choice = await Dialogs.AskCloseAsync(this, vm.Convert.RemainingCount); }
            finally { _askingClose = false; }
            if (choice is null) return; // ✕ or Esc: keep the window open
            if (choice.Remember)
            {
                CloseWhileRunningSetting.Value = choice.Action;
                vm.Settings.Reload();
            }
            action = choice.Action;
        }

        if (!vm.Convert.IsExecuting)
        {
            CloseForGood();
            return;
        }
        if (action == CloseWhileRunning.Background) ContinueInBackground(vm.Convert);
        else StopThenClose(vm.Convert);
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveBounds();
        base.OnClosed(e);
    }

    private void CloseForGood()
    {
        RemoveTray();
        _closeConfirmed = true;
        Close();
    }

    /// <summary>Stops the run (remaining files are not processed) and closes once Core has recorded it.</summary>
    private void StopThenClose(ConvertViewModel session)
    {
        void OnFinished(object? sender, EventArgs e)
        {
            session.Finished -= OnFinished;
            CloseForGood();
        }
        session.Finished += OnFinished;
        session.Stop();
    }

    /// <summary>
    /// The window hides and a tray icon shows progress with 顯示視窗 and 停止. When the run ends the icon is
    /// removed and Clickra exits; it does not stay resident.
    /// </summary>
    private void ContinueInBackground(ConvertViewModel session)
    {
        _trayProgress = new NativeMenuItem { IsEnabled = false };
        var show = new NativeMenuItem(Strings.T("ui_tray_show_window"));
        show.Click += (_, _) => RestoreFromTray();
        var stop = new NativeMenuItem(Strings.T("ui_common_stop"));
        stop.Click += (_, _) => session.Stop();
        _tray = new TrayIcon
        {
            Icon = Icon,
            Menu = new NativeMenu { _trayProgress, new NativeMenuItemSeparator(), show, stop },
        };
        _tray.Clicked += (_, _) => RestoreFromTray();
        if (Application.Current is { } app) TrayIcon.SetIcons(app, new TrayIcons { _tray });

        void OnChanged(object? sender, EventArgs e) => UpdateTray(session);
        void OnFinished(object? sender, EventArgs e)
        {
            session.Changed -= OnChanged;
            session.Finished -= OnFinished;
            if (_tray is null) return; // restored meanwhile
            CloseForGood();
        }
        session.Changed += OnChanged;
        session.Finished += OnFinished;
        UpdateTray(session);
        Hide();
    }

    private void UpdateTray(ConvertViewModel session)
    {
        if (_tray is null || _trayProgress is null) return;
        string percent = session.Percent.ToString("0", Strings.Culture);
        string text = session.FileCount > 1
            ? Strings.F("ui_tray_processing_n_of_total", ("percent", percent),
                ("current", Math.Min(session.FileIndex + 1, session.FileCount)), ("total", session.FileCount))
            : Strings.F("ui_tray_processing", ("percent", percent));
        _trayProgress.Header = text;
        _tray.ToolTipText = Strings.F("ui_tray_tooltip", ("text", text));
    }

    private void RestoreFromTray()
    {
        RemoveTray();
        Show();
        Activate();
    }

    private void RemoveTray()
    {
        if (_tray is null) return;
        if (Application.Current is { } app) TrayIcon.SetIcons(app, new TrayIcons());
        _tray.Dispose();
        _tray = null;
        _trayProgress = null;
    }

    private sealed class Observer<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => onNext(value);
    }
}

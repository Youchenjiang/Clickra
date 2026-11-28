using System;
using System.Threading.Tasks;
using Avalonia;
using Clickra.Core.Diagnostics;
using Clickra.Core.Modules.Sys.Settings.Application;
using Clickra.Desktop.ViewModels;

namespace Clickra.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ClickraLog.Source = "desktop";
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ClickraLog.Error("UnhandledException", "AppDomain unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            ClickraLog.Error("UnobservedTaskException", "TaskScheduler unobserved exception", e.Exception);

        ClickraLog.Info("Startup", $"version={ExternalOpen.AppVersion} os={Environment.OSVersion} lang={SettingsController.Language}");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

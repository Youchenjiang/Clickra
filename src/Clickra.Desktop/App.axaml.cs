using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Clickra.Desktop.ViewModels;
using Clickra.Desktop.Views;

namespace Clickra.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            Clickra.Core.Diagnostics.ClickraLog.Error("UIThreadException", "Avalonia UIThread unhandled exception", e.Exception);
        Clickra.Core.Modules.Pdf.Print.Ports.SystemPrinterDiscoveryService.WarmUp();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            if (desktop.Args is { Length: > 0 } args)
            {
                var files = args.Where(System.IO.File.Exists).Select(System.IO.Path.GetFullPath).ToList();
                if (files.Count > 0)
                {
                    vm.Convert.AddFiles(files);
                }
            }
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

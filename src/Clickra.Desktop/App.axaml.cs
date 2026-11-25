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
        Clickra.Core.Modules.Pdf.Print.Ports.SystemPrinterDiscoveryService.WarmUp();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            if (desktop.Args is { Length: > 0 } args)
            {
                var files = new System.Collections.Generic.List<string>();
                foreach (string a in args)
                {
                    if (System.IO.File.Exists(a)) files.Add(System.IO.Path.GetFullPath(a));
                }
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

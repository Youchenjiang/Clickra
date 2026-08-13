using Microsoft.UI.Xaml;

namespace Clickra_Fluent;

public partial class App : Application
{
    private Window? _window;
    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        string launchArguments = string.IsNullOrWhiteSpace(args.Arguments)
            ? string.Join(" ", Environment.GetCommandLineArgs().Skip(1).Select(QuoteArgument))
            : args.Arguments;
        _window = new MainWindow(launchArguments);
        MainWindow = _window;
        _window.Activate();
    }

    private static string QuoteArgument(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}

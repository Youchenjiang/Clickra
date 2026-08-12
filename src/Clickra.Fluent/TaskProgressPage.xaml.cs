using Clickra.Core;
using Clickra.Core.Processors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Diagnostics;

namespace Clickra_Fluent;

public sealed partial class TaskProgressPage : Page
{
    private CancellationTokenSource? _cts;
    private string _arguments = "";
    private string _outputFolder = "";

    public TaskProgressPage()
    {
        InitializeComponent();
        ApplyLanguage();
        CancelButton.Click += (_, _) => _cts?.Cancel();
        OpenFolderButton.Click += (_, _) => OpenOutputFolder();
        Loaded += async (_, _) => await RunAsync();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _arguments = e.Parameter as string ?? "";
    }

    // NOSONAR:S2325 — XAML event handler touching generated instance fields (ActionButtons).
    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < 300;
        Grid.SetColumn(ActionButtons, narrow ? 0 : 1);
        Grid.SetRow(ActionButtons, narrow ? 1 : 0);
        ActionButtons.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        ActionButtonsRow.Height = narrow ? GridLength.Auto : new GridLength(0);
    }

    private static string L(string key) => Localization.T(key, ClickraStorage.GetSetting("Language"));

    private void ApplyLanguage()
    {
        TitleText.Text = L("fluent_progress_running_title");
        FileText.Text = L("fluent_progress_preparing");
        StateText.Text = L("fluent_progress_preparing");
        OutputText.Text = $"{L("fluent_progress_output")}{L("fluent_progress_preparing")}";
        FooterText.Text = L("fluent_progress_waiting");
        OpenFolderButton.Content = L("fluent_progress_open_folder");
        CancelButton.Content = L("fluent_cancel");
    }

    private async Task RunAsync()
    {
        var args = ConvertCommandRegistry.SplitCommandLine(_arguments);
        if (args.Count < 2 || !ConvertCommandRegistry.IsKnownCommand(args[0]))
        {
            Complete(L("fluent_progress_invalid_command"), false);
            CancelButton.Click += (_, _) => App.MainWindow?.Close();
            return;
        }

        string command = args[0];
        var files = ConvertCommandRegistry.ExpandDirectoryArguments(command, args.Skip(1)).Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            Complete(L("fluent_progress_file_not_found"), false);
            CancelButton.Click += (_, _) => App.MainWindow?.Close();
            return;
        }

        if (!OfficeEnginePreflight.TryValidate(command, L, out string preflightError))
        {
            Complete(preflightError, false);
            return;
        }

        var outputs = ConvertCommandRegistry.EstimateOutputs(command, files);
        _outputFolder = Path.GetDirectoryName(outputs[0]) ?? "";
        _cts = new CancellationTokenSource();

        TitleText.Text = string.Format(L("fluent_progress_running_title"), L(ConvertCommandRegistry.GetLabelKey(command)));
        FileText.Text = files.Count > 1
            ? string.Format(L("fluent_progress_multiple_files"), Path.GetFileName(files[0]), files.Count)
            : Path.GetFileName(files[0]);
        OutputText.Text = $"{L("fluent_progress_output")}{Path.GetFileName(outputs[0])}";
        StateText.Text = L("fluent_progress_running");

        try
        {
            var result = await ConvertCommandRunner.RunTrackedAsync(command, files, outputs,
                (percent, message) => DispatcherQueue.TryEnqueue(() => SetProgress(percent, message)),
                _cts.Token,
                () => DispatcherQueue.EnqueueAsync(() => FluentDialogs.PromptPasswordAsync(XamlRoot, L)),
                pdfPath => DispatcherQueue.EnqueueAsync(() => SplitOverlay.ShowForAsync(pdfPath)));

            switch (result.Status)
            {
                case ConvertCommandRunner.ConvertRunStatus.Succeeded:
                    Complete(L("fluent_progress_completed"), true);
                    break;
                case ConvertCommandRunner.ConvertRunStatus.Canceled:
                    Complete(L("fluent_progress_canceled"), false);
                    break;
                default:
                    Complete(result.Error ?? "", false);
                    break;
            }
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }


    private void SetProgress(int percent, string message)
    {
        ProgressBar.Value = percent;
        PercentText.Text = $"{percent}%";
        StateText.Text = string.IsNullOrWhiteSpace(message) ? L("fluent_progress_processing") : message;
    }

    private void Complete(string message, bool success)
    {
        SetProgress(success ? 100 : 0, message);
        TitleText.Text = success ? L("fluent_progress_done_title") : L("fluent_progress_failed_title");
        StatusIcon.Glyph = success ? "\uE73E" : "\uE783";
        StateText.Text = success ? message : L("fluent_progress_failed");
        ErrorText.Text = success ? "" : message;
        ErrorText.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
        FooterText.Text = success ? L("fluent_progress_done_footer") : L("fluent_progress_failed_footer");
        OpenFolderButton.Visibility = success ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        CancelButton.Content = L("fluent_progress_close");
        CancelButton.Click += (_, _) => App.MainWindow?.Close();
    }

    private void OpenOutputFolder()
    {
        if (string.IsNullOrWhiteSpace(_outputFolder) || !Directory.Exists(_outputFolder)) return;
        Process.Start(new ProcessStartInfo(Clickra.Core.SystemPaths.Explorer, $"\"{_outputFolder}\"") { UseShellExecute = true })?.Dispose();
    }

}

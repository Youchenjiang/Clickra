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
        ApplyLanguage(TitleText, FileText, StateText, OutputText, FooterText, OpenFolderButton, CancelButton);
        LayoutRoot.SizeChanged += (_, e) => ApplyLayoutSize(ActionButtons, ActionButtonsRow, e.NewSize.Width);
        CancelButton.Click += (_, _) => _cts?.Cancel();
        OpenFolderButton.Click += (_, _) => OpenOutputFolder();
        Loaded += async (_, _) => await RunAsync();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _arguments = e.Parameter as string ?? "";
    }

    private static void ApplyLayoutSize(StackPanel actionButtons, RowDefinition actionButtonsRow, double width)
    {
        bool narrow = width < 300;
        Grid.SetColumn(actionButtons, narrow ? 0 : 1);
        Grid.SetRow(actionButtons, narrow ? 1 : 0);
        actionButtons.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        actionButtonsRow.Height = narrow ? GridLength.Auto : new GridLength(0);
    }

    private static string L(string key) => Localization.T(key, ClickraStorage.GetSetting("Language"));

    private static void ApplyLanguage(TextBlock titleText, TextBlock fileText, TextBlock stateText, TextBlock outputText, TextBlock footerText, Button openFolderButton, Button cancelButton)
    {
        titleText.Text = L("fluent_progress_running_title");
        fileText.Text = L("fluent_progress_preparing");
        stateText.Text = L("fluent_progress_preparing");
        outputText.Text = $"{L("fluent_progress_output")}{L("fluent_progress_preparing")}";
        footerText.Text = L("fluent_progress_waiting");
        openFolderButton.Content = L("fluent_progress_open_folder");
        cancelButton.Content = L("fluent_cancel");
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
                (percent, message) => DispatcherQueue.TryEnqueue(() => SetProgress(ProgressBar, PercentText, StateText, percent, message)),
                () => DispatcherQueue.EnqueueAsync(() => FluentDialogs.PromptPasswordAsync(XamlRoot, L)),
                pdfPath => DispatcherQueue.EnqueueAsync(() => SplitOverlay.ShowForAsync(pdfPath)),
                _cts.Token);

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


    private static void SetProgress(ProgressBar progressBar, TextBlock percentText, TextBlock stateText, int percent, string message)
    {
        progressBar.Value = percent;
        percentText.Text = $"{percent}%";
        stateText.Text = string.IsNullOrWhiteSpace(message) ? L("fluent_progress_processing") : message;
    }

    private void Complete(string message, bool success)
    {
        SetProgress(ProgressBar, PercentText, StateText, success ? 100 : 0, message);
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

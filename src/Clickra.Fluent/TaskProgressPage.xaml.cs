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

    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < 300;
        Grid.SetColumn(ActionButtons, narrow ? 0 : 1);
        Grid.SetRow(ActionButtons, narrow ? 1 : 0);
        ActionButtons.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        ActionButtonsRow.Height = narrow ? GridLength.Auto : new GridLength(0);
    }

    private string L(string key) => Localization.T(key, ClickraStorage.GetSetting("Language"));

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

        string startTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string inputs = string.Join(";", files);
        var outputs = ConvertCommandRegistry.EstimateOutputs(command, files);
        _outputFolder = Path.GetDirectoryName(outputs[0]) ?? "";
        var stopwatch = Stopwatch.StartNew();
        _cts = new CancellationTokenSource();

        TitleText.Text = string.Format(L("fluent_progress_running_title"), L(ConvertCommandRegistry.GetLabelKey(command)));
        FileText.Text = files.Count > 1
            ? string.Format(L("fluent_progress_multiple_files"), Path.GetFileName(files[0]), files.Count)
            : Path.GetFileName(files[0]);
        OutputText.Text = $"{L("fluent_progress_output")}{Path.GetFileName(outputs[0])}";
        StateText.Text = L("fluent_progress_running");

        try
        {
            ClickraStorage.StartActiveRecord(command, files.Count, inputs);
            ClickraStorage.SetActiveRecordInProgress();
            void Progress(int current, int total, string message)
            {
                int percent = total > 0 ? Math.Clamp((int)(current * 100.0 / total), 0, 100) : 0;
                DispatcherQueue.TryEnqueue(() => SetProgress(percent, message));
            }
            await Task.Run(() => ConvertCommandRunner.Run(command, files, outputs, Progress, _cts.Token,
                () => DispatcherQueue.EnqueueAsync(PromptPasswordAsync),
                pdfPath => DispatcherQueue.EnqueueAsync(() => PromptSplitPagesAsync(pdfPath))), _cts.Token);
            stopwatch.Stop();
            ClickraStorage.CompleteActiveRecord(command, startTime, true, "", elapsedMs: stopwatch.ElapsedMilliseconds, inputPaths: inputs, outputPath: string.Join(";", outputs));
            Complete(L("fluent_progress_completed"), true);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            ClickraStorage.CompleteActiveRecord(command, startTime, false, "Canceled", elapsedMs: stopwatch.ElapsedMilliseconds, inputPaths: inputs, outputPath: string.Join(";", outputs));
            Complete(L("fluent_progress_canceled"), false);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            ClickraStorage.CompleteActiveRecord(command, startTime, false, ex.Message, elapsedMs: stopwatch.ElapsedMilliseconds, inputPaths: inputs, outputPath: string.Join(";", outputs));
            Complete(ex.Message, false);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task<string?> PromptPasswordAsync()
    {
        var box = new PasswordBox { PlaceholderText = L("fluent_pdf_password_placeholder") };
        var dialog = new ContentDialog
        {
            Title = L("fluent_pdf_password"),
            Content = box,
            PrimaryButtonText = L("fluent_ok"),
            CloseButtonText = L("dialog_cancel"),
            XamlRoot = XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Password : null;
    }

    private async Task<string?> PromptSplitPagesAsync(string pdfPath)
    {
        SplitOverlay.Visibility = Visibility.Visible;
        string? spec = await SplitOverlay.ShowForAsync(pdfPath);
        SplitOverlay.Visibility = Visibility.Collapsed;
        return spec;
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

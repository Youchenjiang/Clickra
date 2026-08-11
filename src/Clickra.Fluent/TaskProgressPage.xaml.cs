using Clickra.Core;
using Clickra.Core.Processors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Diagnostics;
using System.Text;

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
        var args = SplitCommandLine(_arguments);
        if (args.Count < 2 || !IsKnownCommand(args[0]))
        {
            Complete(L("fluent_progress_invalid_command"), false);
            CancelButton.Click += (_, _) => App.MainWindow?.Close();
            return;
        }

        string command = args[0];
        var files = ExpandDirectoryArguments(command, args.Skip(1)).Where(File.Exists).ToList();
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
        var outputs = EstimateOutputs(command, files);
        _outputFolder = Path.GetDirectoryName(outputs[0]) ?? "";
        var stopwatch = Stopwatch.StartNew();
        _cts = new CancellationTokenSource();

        TitleText.Text = string.Format(L("fluent_progress_running_title"), CommandLabel(command));
        FileText.Text = files.Count > 1
            ? string.Format(L("fluent_progress_multiple_files"), Path.GetFileName(files[0]), files.Count)
            : Path.GetFileName(files[0]);
        OutputText.Text = $"{L("fluent_progress_output")}{Path.GetFileName(outputs[0])}";
        StateText.Text = L("fluent_progress_running");

        try
        {
            ClickraStorage.StartActiveRecord(command, files.Count, inputs);
            ClickraStorage.SetActiveRecordInProgress();
            await Task.Run(() => RunCommand(command, files, outputs, _cts.Token), _cts.Token);
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

    private void RunCommand(string command, List<string> files, List<string> outputs, CancellationToken token)
    {
        void Progress(int current, int total, string message)
        {
            int percent = total > 0 ? Math.Clamp((int)(current * 100.0 / total), 0, 100) : 0;
            DispatcherQueue.TryEnqueue(() => SetProgress(percent, message));
        }

        switch (command)
        {
            case "ppt2pdf":
                FileProcessor.ConvertPptToPdf(files, Progress, token);
                break;
            case "word2pdf":
                FileProcessor.ConvertWordToPdf(files, Progress, token);
                break;
            case "excel2pdf":
                FileProcessor.ConvertExcelToPdf(files, Progress, token);
                break;
            case "merge-pdf":
                FileProcessor.MergePdfs(files, outputs[0], Progress, token);
                break;
            case "compress-pdf":
                for (int i = 0; i < files.Count; i++)
                    FileProcessor.CompressPdf(files[i], outputs[i], CompressionOptions(), (c, t, m) => Progress((i * 100) + c, files.Count * 100, m), token);
                break;
            case "translate-pdf":
                for (int i = 0; i < files.Count; i++)
                    FileProcessor.TranslatePdf(files[i], outputs[i], ClickraStorage.GetSetting("TranslateTargetLang"), (c, t, m) => Progress((i * 100) + c, files.Count * 100, m), token);
                break;
            case "img2pdf":
                for (int i = 0; i < files.Count; i++)
                    FileProcessor.ConvertImagesToPdf(new List<string> { files[i] }, outputs[i], (c, t, m) => Progress((i * 100) + c, files.Count * 100, m), token);
                break;
            case "img-merge":
                FileProcessor.ConvertImagesToPdf(files, outputs[0], Progress, token);
                break;
            case "img-stitch":
                FileProcessor.StitchImages(files, outputs[0], Progress, token);
                break;
            case "decrypt-pdf":
                string? password = DispatcherQueue.EnqueueAsync(PromptPasswordAsync).GetAwaiter().GetResult();
                if (password is null) throw new OperationCanceledException(token);
                for (int i = 0; i < files.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    FileProcessor.DecryptPdf(files[i], outputs[i], password, (c, t, m) => Progress((i * 100) + c, files.Count * 100, m), token);
                }
                break;
            case "split-pdf":
                string? splitPages = DispatcherQueue.EnqueueAsync(PromptSplitPagesAsync).GetAwaiter().GetResult();
                if (splitPages is null) throw new OperationCanceledException(token);
                for (int i = 0; i < files.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    FileProcessor.SplitPdf(files[i], outputs[i], splitPages, (c, t, m) => Progress((i * 100) + c, files.Count * 100, m), token);
                }
                break;
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

    private async Task<string?> PromptSplitPagesAsync()
    {
        var box = new TextBox { Text = "all", PlaceholderText = L("pdf_split_prompt") };
        var dialog = new ContentDialog
        {
            Title = L("pdf_split_title"),
            Content = box,
            PrimaryButtonText = L("fluent_ok"),
            CloseButtonText = L("dialog_cancel"),
            XamlRoot = XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? (string.IsNullOrWhiteSpace(box.Text) ? "all" : box.Text.Trim()) : null;
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
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_outputFolder}\"") { UseShellExecute = true })?.Dispose();
    }

    private static List<string> SplitCommandLine(string value)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        bool inQuote = false;
        foreach (char ch in value)
        {
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (char.IsWhiteSpace(ch) && !inQuote)
            {
                if (current.Length > 0) { args.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(ch);
        }
        if (current.Length > 0) args.Add(current.ToString());
        return args;
    }

    private static IEnumerable<string> ExpandDirectoryArguments(string command, IEnumerable<string> inputs)
    {
        string[] allowed = GetAllowedExtensions(command);
        foreach (var input in inputs)
        {
            if (Directory.Exists(input))
            {
                foreach (var file in Directory.EnumerateFiles(input).Where(file => allowed.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)))
                    yield return file;
            }
            else
            {
                yield return input;
            }
        }
    }

    private static List<string> EstimateOutputs(string command, List<string> files)
    {
        string outputDir = ClickraStorage.GetOutputDir(files[0]);
        return command switch
        {
            "merge-pdf" => new() { Path.Combine(outputDir, "Merged_PDF.pdf") },
            "img-merge" => new() { Path.Combine(outputDir, "Merged_Images.pdf") },
            "img-stitch" => new() { Path.Combine(outputDir, "Stitched_Image.png") },
            "compress-pdf" => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + "_compressed.pdf")).ToList(),
            "translate-pdf" => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + "_translated.pdf")).ToList(),
            "decrypt-pdf" => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + "_decrypted.pdf")).ToList(),
            "split-pdf" => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + "_split.pdf")).ToList(),
            "img2pdf" => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + ".pdf")).ToList(),
            _ => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + ".pdf")).ToList()
        };
    }

    private static Dictionary<string, object> CompressionOptions() => new()
    {
        ["level"] = ClickraStorage.GetSetting("PdfCompressImageLevel") switch { "0" => "small", "2" or "3" => "high", _ => "balanced" },
        ["strip_fonts"] = ClickraStorage.GetSetting("PdfCompressStripFonts").Equals("true", StringComparison.OrdinalIgnoreCase),
        ["minify_content"] = !ClickraStorage.GetSetting("PdfCompressMinifyContent").Equals("false", StringComparison.OrdinalIgnoreCase)
    };

    private static bool IsKnownCommand(string command) => GetAllowedExtensions(command).Length > 0;

    private static string[] GetAllowedExtensions(string? command) => command switch
    {
        "ppt2pdf" => new[] { ".ppt", ".pptx" },
        "word2pdf" => new[] { ".doc", ".docx" },
        "excel2pdf" => new[] { ".xls", ".xlsx" },
        "merge-pdf" or "compress-pdf" or "translate-pdf" or "decrypt-pdf" or "split-pdf" => new[] { ".pdf" },
        "img2pdf" or "img-merge" or "img-stitch" => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" },
        _ => Array.Empty<string>()
    };

    private string CommandLabel(string command) => L(command switch
    {
        "ppt2pdf" => "cmd_ppt_to_pdf",
        "word2pdf" => "cmd_word_to_pdf",
        "excel2pdf" => "cmd_excel_to_pdf",
        "merge-pdf" => "cmd_merge_pdf",
        "compress-pdf" => "cmd_compress_pdf",
        "translate-pdf" => "cmd_translate_pdf",
        "decrypt-pdf" => "cmd_decrypt_pdf",
        "split-pdf" => "cmd_split_pdf",
        "img2pdf" => "cmd_img_to_pdf",
        "img-merge" => "cmd_merge_img",
        "img-stitch" => "cmd_stitch_img",
        _ => command
    });
}

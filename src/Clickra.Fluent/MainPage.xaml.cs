using Clickra.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Clickra_Fluent;

public sealed partial class MainPage : Page
{
    private readonly List<string> _selectedFiles = new();
    private readonly Dictionary<string, Button> _commandButtons = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private bool _loadingSettings;
    private bool _isRunning;
    private string? _selectedCommand;

    public MainPage()
    {
        InitializeComponent();
        NavView.SelectionChanged += NavView_SelectionChanged;
        DropZone.Tapped += DropZone_Tapped;
        DropZone.DragOver += DropZone_DragOver;
        DropZone.Drop += DropZone_Drop;
        ClearFilesButton.Click += (_, _) => { _selectedFiles.Clear(); RefreshFiles(); };
        StartButton.Click += async (_, _) => await StartConversionAsync();
        CancelButton.Click += (_, _) => _cts?.Cancel();
        ClearHistoryButton.Click += (_, _) => { ClickraStorage.ClearHistory(); RefreshHistory(); };
        OpenConvertButton.Click += (_, _) => SelectNavItem("Convert");
        ViewHistoryButton.Click += (_, _) => SelectNavItem("History");
        HookCommandButtons();
        LoadSettings();
        RefreshFiles();
        RefreshHistory();
        NavView.SelectedItem = NavView.MenuItems[0];
        ShowPanel("Overview");
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            ShowPanel(tag);
        }
    }

    private void ShowPanel(string name)
    {
        OverviewPanel.Visibility = name == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        ConvertPanel.Visibility = name == "Convert" ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = name == "History" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = name == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = name == "About" ? Visibility.Visible : Visibility.Collapsed;
        if (name is "History" or "Overview") RefreshHistory();
    }

    private void SelectNavItem(string tag)
    {
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is string itemTag && itemTag == tag)
            {
                NavView.SelectedItem = item;
                ShowPanel(tag);
                return;
            }
        }
    }

    private void HookCommandButtons()
    {
        foreach (var button in new[] { BtnWord2Pdf, BtnExcel2Pdf, BtnPpt2Pdf, BtnMergePdf, BtnCompressPdf, BtnTranslatePdf, BtnDecryptPdf, BtnImg2Pdf, BtnImgMerge, BtnImgStitch })
        {
            if (button.Tag is string command)
            {
                _commandButtons[command] = button;
                button.Click += (_, _) => SelectCommand(command);
            }
        }
    }

    private async void DropZone_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        if (App.MainWindow is not null)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        }

        var files = await picker.PickMultipleFilesAsync();
        AddFiles(files.Select(f => f.Path));
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        AddFiles(items.OfType<StorageFile>().Select(f => f.Path));
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path) && !_selectedFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _selectedFiles.Add(path);
            }
        }
        RefreshFiles();
    }

    private void SelectCommand(string command)
    {
        _selectedCommand = command;
        foreach (var pair in _commandButtons)
        {
            pair.Value.Style = pair.Key.Equals(command, StringComparison.OrdinalIgnoreCase)
                ? (Style)Application.Current.Resources["AccentButtonStyle"]
                : null;
        }
        CommandStatusText.Text = $"Selected: {CommandLabel(command)}";
        UpdateStartState();
    }

    private void RefreshFiles()
    {
        FileListContainer.Children.Clear();
        if (_selectedFiles.Count == 0)
        {
            FileListContainer.Children.Add(EmptyFileMessage);
        }
        else
        {
            foreach (var file in _selectedFiles)
            {
                FileListContainer.Children.Add(new TextBlock
                {
                    Text = Path.GetFileName(file),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }
        UpdateStartState();
    }

    private void UpdateStartState()
    {
        StartButton.Visibility = !_isRunning && _selectedFiles.Count > 0 && _selectedCommand is not null ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = _isRunning ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task StartConversionAsync()
    {
        if (_selectedCommand is null || _selectedFiles.Count == 0 || _isRunning) return;
        if (!ValidateSelection(_selectedCommand, out var error))
        {
            await ShowErrorAsync(error);
            return;
        }

        _isRunning = true;
        _cts = new CancellationTokenSource();
        UpdateStartState();
        ConversionProgressSection.Visibility = Visibility.Visible;
        SetProgress(0, "Starting...");

        string command = _selectedCommand;
        var files = _selectedFiles.ToList();
        string startTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string inputs = string.Join(";", files);
        var outputs = EstimateOutputs(command, files);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            ClickraStorage.StartActiveRecord(command, files.Count, inputs);
            ClickraStorage.SetActiveRecordInProgress();
            await Task.Run(() => RunCommand(command, files, outputs, _cts.Token), _cts.Token);
            stopwatch.Stop();
            ClickraStorage.CompleteActiveRecord(command, startTime, true, "", elapsedMs: stopwatch.ElapsedMilliseconds, inputPaths: inputs, outputPath: string.Join(";", outputs));
            SetProgress(100, "Completed.");
            _selectedFiles.Clear();
            RefreshFiles();
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            ClickraStorage.CompleteActiveRecord(command, startTime, false, "Canceled", elapsedMs: stopwatch.ElapsedMilliseconds, inputPaths: inputs, outputPath: string.Join(";", outputs));
            SetProgress(0, "Canceled.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            ClickraStorage.CompleteActiveRecord(command, startTime, false, ex.Message, elapsedMs: stopwatch.ElapsedMilliseconds, inputPaths: inputs, outputPath: string.Join(";", outputs));
            SetProgress(0, $"Failed: {ex.Message}");
            await ShowErrorAsync(ex.Message);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _isRunning = false;
            UpdateStartState();
            RefreshHistory();
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
                {
                    token.ThrowIfCancellationRequested();
                    FileProcessor.CompressPdf(files[i], outputs[i], CompressionLevel(), (c, t, m) => Progress((i * 100) + c, files.Count * 100, m), token);
                }
                break;
            case "translate-pdf":
                for (int i = 0; i < files.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    FileProcessor.TranslatePdf(files[i], outputs[i], ClickraStorage.GetSetting("TranslateTargetLang"), (c, t, m) => Progress((i * 100) + c, files.Count * 100, m), token);
                }
                break;
            case "decrypt-pdf":
                string password = DispatcherQueue.EnqueueAsync(PromptPasswordAsync).GetAwaiter().GetResult();
                for (int i = 0; i < files.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    FileProcessor.DecryptPdf(files[i], outputs[i], password, (c, t, m) => Progress((i * 100) + c, files.Count * 100, m), token);
                }
                break;
            case "img2pdf":
                for (int i = 0; i < files.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    FileProcessor.ConvertImagesToPdf(new List<string> { files[i] }, outputs[i], (c, t, m) => Progress((i * 100) + c, files.Count * 100, m), token);
                }
                break;
            case "img-merge":
                FileProcessor.ConvertImagesToPdf(files, outputs[0], Progress, token);
                break;
            case "img-stitch":
                FileProcessor.StitchImages(files, outputs[0], Progress, token);
                break;
        }
    }

    private void SetProgress(int percent, string message)
    {
        ConversionProgressBar.Value = percent;
        ConversionProgressText.Text = string.IsNullOrWhiteSpace(message) ? $"{percent}%" : $"{message}  {percent}%";
        ActiveJobSection.Visibility = _isRunning ? Visibility.Visible : Visibility.Collapsed;
        ActiveJobText.Text = ConversionProgressText.Text;
    }

    private bool ValidateSelection(string command, out string error)
    {
        error = "";
        string[] extensions = command switch
        {
            "ppt2pdf" => new[] { ".ppt", ".pptx" },
            "word2pdf" => new[] { ".doc", ".docx" },
            "excel2pdf" => new[] { ".xls", ".xlsx" },
            "merge-pdf" or "compress-pdf" or "translate-pdf" or "decrypt-pdf" => new[] { ".pdf" },
            _ => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" }
        };
        int minFiles = command is "merge-pdf" or "img-merge" or "img-stitch" ? 2 : 1;
        if (_selectedFiles.Count < minFiles)
        {
            error = $"{CommandLabel(command)} needs at least {minFiles} file(s).";
            return false;
        }
        var bad = _selectedFiles.FirstOrDefault(f => !extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        if (bad is not null)
        {
            error = $"{Path.GetFileName(bad)} is not valid for {CommandLabel(command)}.";
            return false;
        }
        return true;
    }

    private List<string> EstimateOutputs(string command, List<string> files)
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
            "img2pdf" => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + ".pdf")).ToList(),
            _ => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + ".pdf")).ToList()
        };
    }

    private void LoadSettings()
    {
        _loadingSettings = true;
        OutputDirCombo.SelectedIndex = ClickraStorage.GetSetting("OutputDir") switch { "desktop" => 1, "downloads" => 2, var s when !string.IsNullOrWhiteSpace(s) && s != "source" => 3, _ => 0 };
        EngineCombo.SelectedIndex = ClickraStorage.GetSetting("OfficeEngine") switch { "microsoft" => 1, "libreoffice" => 2, _ => 0 };
        LanguageCombo.SelectedIndex = ClickraStorage.GetSetting("Language") switch { "zh-CN" => 1, "en-US" => 2, "ja-JP" => 3, "ko-KR" => 4, _ => 0 };
        QuietModeToggle.IsOn = ClickraStorage.GetSetting("QuietMode").Equals("true", StringComparison.OrdinalIgnoreCase);
        NotificationToggle.IsOn = !ClickraStorage.GetSetting("Notification").Equals("false", StringComparison.OrdinalIgnoreCase);
        PdfLangCombo.SelectedIndex = ClickraStorage.GetSetting("TranslateTargetLang") switch { "en" => 1, "zh-CN" => 2, "ja" => 3, "ko" => 4, _ => 0 };
        CompressionSlider.Value = int.TryParse(ClickraStorage.GetSetting("PdfCompressImageLevel"), out int level) ? level : 1;
        StripFontsToggle.IsOn = ClickraStorage.GetSetting("PdfCompressStripFonts").Equals("true", StringComparison.OrdinalIgnoreCase);
        MinifyContentToggle.IsOn = !ClickraStorage.GetSetting("PdfCompressMinifyContent").Equals("false", StringComparison.OrdinalIgnoreCase);
        _loadingSettings = false;

        OutputDirCombo.SelectionChanged += (_, _) => SaveSettings();
        EngineCombo.SelectionChanged += (_, _) => SaveSettings();
        LanguageCombo.SelectionChanged += (_, _) => SaveSettings();
        PdfLangCombo.SelectionChanged += (_, _) => SaveSettings();
        CompressionSlider.ValueChanged += (_, _) => SaveSettings();
        StripFontsToggle.Toggled += (_, _) => SaveSettings();
        MinifyContentToggle.Toggled += (_, _) => SaveSettings();
        QuietModeToggle.Toggled += (_, _) => SaveSettings();
        NotificationToggle.Toggled += (_, _) => SaveSettings();
    }

    private void SaveSettings()
    {
        if (_loadingSettings) return;
        ClickraStorage.SaveSetting("OutputDir", OutputDirCombo.SelectedIndex switch { 1 => "desktop", 2 => "downloads", _ => "source" });
        ClickraStorage.SaveSetting("OfficeEngine", EngineCombo.SelectedIndex switch { 1 => "microsoft", 2 => "libreoffice", _ => "auto" });
        ClickraStorage.SaveSetting("Language", LanguageCombo.SelectedIndex switch { 1 => "zh-CN", 2 => "en-US", 3 => "ja-JP", 4 => "ko-KR", _ => "zh-TW" });
        ClickraStorage.SaveSetting("TranslateTargetLang", PdfLangCombo.SelectedIndex switch { 1 => "en", 2 => "zh-CN", 3 => "ja", 4 => "ko", _ => "zh-TW" });
        ClickraStorage.SaveSetting("PdfCompressImageLevel", ((int)CompressionSlider.Value).ToString());
        ClickraStorage.SaveSetting("PdfCompressStripFonts", StripFontsToggle.IsOn ? "true" : "false");
        ClickraStorage.SaveSetting("PdfCompressMinifyContent", MinifyContentToggle.IsOn ? "true" : "false");
        ClickraStorage.SaveSetting("QuietMode", QuietModeToggle.IsOn ? "true" : "false");
        ClickraStorage.SaveSetting("Notification", NotificationToggle.IsOn ? "true" : "false");
    }

    private void RefreshHistory()
    {
        var history = ClickraStorage.GetHistory(20);
        StatTotal.Text = history.Count.ToString();
        StatSuccess.Text = history.Count(h => h.IsSuccess).ToString();
        StatFailed.Text = history.Count(h => !h.IsSuccess).ToString();
        EmptyHistoryState.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryListContainer.Children.Clear();
        foreach (var entry in history)
        {
            var row = new Grid
            {
                ColumnSpacing = 12
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var status = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(entry.IsSuccess ? Colors.LimeGreen : Colors.IndianRed),
                VerticalAlignment = VerticalAlignment.Center
            };

            var title = new StackPanel { Spacing = 2 };
            title.Children.Add(new TextBlock { Text = CommandLabel(entry.Command), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            title.Children.Add(new TextBlock { Text = entry.Time, FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });

            var result = new TextBlock
            {
                Text = entry.IsSuccess ? "Success" : "Failed",
                FontSize = 13,
                Foreground = new SolidColorBrush(entry.IsSuccess ? Colors.LimeGreen : Colors.IndianRed),
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(status, 0);
            Grid.SetColumn(title, 1);
            Grid.SetColumn(result, 2);
            row.Children.Add(status);
            row.Children.Add(title);
            row.Children.Add(result);

            HistoryListContainer.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Child = row
            });
        }
    }

    private string CompressionLevel() => ((int)CompressionSlider.Value) switch
    {
        0 => "small",
        2 or 3 => "high",
        _ => "balanced"
    };

    private async Task<string> PromptPasswordAsync()
    {
        var box = new PasswordBox { PlaceholderText = "PDF password" };
        var dialog = new ContentDialog
        {
            Title = "PDF password",
            Content = box,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Password : "";
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Clickra",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private static string CommandLabel(string command) => command switch
    {
        "ppt2pdf" => "PPT to PDF",
        "word2pdf" => "Word to PDF",
        "excel2pdf" => "Excel to PDF",
        "merge-pdf" => "Merge PDF",
        "compress-pdf" => "Compress PDF",
        "translate-pdf" => "Translate PDF",
        "decrypt-pdf" => "Decrypt PDF",
        "img2pdf" => "Image to PDF",
        "img-merge" => "Merge Images",
        "img-stitch" => "Stitch Images",
        _ => command
    };
}

internal static class DispatcherQueueExtensions
{
    public static Task<T> EnqueueAsync<T>(this Microsoft.UI.Dispatching.DispatcherQueue queue, Func<Task<T>> action)
    {
        var tcs = new TaskCompletionSource<T>();
        queue.TryEnqueue(async () =>
        {
            try { tcs.SetResult(await action()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }
}

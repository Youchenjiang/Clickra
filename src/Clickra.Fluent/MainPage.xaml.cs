using Clickra.Core;
using Clickra.Core.Processors;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Diagnostics;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace Clickra_Fluent;

public sealed partial class MainPage : Page
{
    private readonly List<string> _selectedFiles = new();
    private readonly Dictionary<string, Button> _commandButtons = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private bool _loadingSettings;
    private bool _isRunning;
    private bool _libreOfficeSetupInProgress;
    private bool _startupCommandHandled;
    private string _startupArguments = "";
    private string? _selectedCommand;
    private List<ClickraStorage.HistoryEntry> _historyEntries = new();
    private int _selectedHistoryIndex = -1;

    public MainPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            ApplyResponsiveLayout();
            await RunStartupCommandAsync();
        };
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        NavView.SelectionChanged += NavView_SelectionChanged;
        DropZone.Tapped += DropZone_Tapped;
        DropZone.PointerEntered += (_, _) => SetDropZoneHot(true);
        DropZone.PointerExited += (_, _) => SetDropZoneHot(false);
        DropZone.DragOver += DropZone_DragOver;
        DropZone.DragLeave += (_, _) => SetDropZoneHot(false);
        DropZone.Drop += DropZone_Drop;
        ClearFilesButton.Click += (_, _) => { _selectedFiles.Clear(); RefreshFiles(); };
        StartButton.Click += async (_, _) => await StartConversionAsync();
        CancelButton.Click += (_, _) => _cts?.Cancel();
        ClearHistoryButton.Click += async (_, _) => await ClearHistoryAsync();
        OpenConvertButton.Click += (_, _) => SelectNavItem("Convert");
        ViewHistoryButton.Click += (_, _) => SelectNavItem("History");
        LibreOfficeBrowseButton.Click += async (_, _) => await BrowseLibreOfficeAsync();
        LibreOfficeDownloadButton.Click += async (_, _) => await InstallLibreOfficeAsync();
        LibreOfficeUninstallButton.Click += async (_, _) => await UninstallLibreOfficeAsync();
        GitHubButton.Click += async (_, _) => await OpenUriAsync("https://github.com/Youchenjiang/Clickra");
        OpenDataDirButton.Click += async (_, _) => await OpenDataDirAsync();
        GmailButton.Click += async (_, _) => await OpenDiagnosticsEmailAsync();
        HookCommandButtons();
        LoadSettings();
        ApplyLanguage();
        RefreshFiles();
        RefreshHistory();
        NavView.SelectedItem = NavView.MenuItems[0];
        ShowPanel("Overview");
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is string args)
        {
            _startupArguments = args;
        }
    }

    private string L(string key) => Localization.T(key, ClickraStorage.GetSetting("Language"));

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
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        var narrow = ActualWidth < 1000;

        SetTwoPaneLayout(OverviewSidePane, OverviewMainColumn, OverviewSideColumn, 1.4, 0.85, narrow);
        ApplyConvertResponsiveLayout(narrow);
        ApplyHistoryResponsiveLayout(narrow);
        ApplySettingsResponsiveLayout(narrow);
        ApplyAboutResponsiveLayout(narrow);
    }

    private static void SetTwoPaneLayout(FrameworkElement sidePane, ColumnDefinition mainColumn, ColumnDefinition sideColumn, double mainWide, double sideWide, bool narrow)
    {
        mainColumn.Width = new GridLength(1, GridUnitType.Star);
        sideColumn.Width = narrow ? new GridLength(0) : new GridLength(sideWide, GridUnitType.Star);
        if (!narrow)
        {
            mainColumn.Width = new GridLength(mainWide, GridUnitType.Star);
        }

        Grid.SetColumn(sidePane, narrow ? 0 : 1);
        Grid.SetRow(sidePane, narrow ? 1 : 0);
    }

    private void ApplyConvertResponsiveLayout(bool narrow)
    {
        ConvertMainColumn.Width = new GridLength(1, GridUnitType.Star);
        ConvertSideColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        Grid.SetColumn(ConvertCommandCard, narrow ? 0 : 1);
        Grid.SetRow(ConvertCommandCard, narrow ? 2 : 0);
        Grid.SetColumn(ConvertRunCard, narrow ? 0 : 1);
        Grid.SetRow(ConvertRunCard, narrow ? 3 : 1);
    }

    private void ApplyHistoryResponsiveLayout(bool narrow)
    {
        double availableHeight = Math.Max(360, ActualHeight - 118);
        HistoryListColumn.Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(430);
        HistoryDetailColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        HistoryTopRow.Height = narrow ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        HistoryDetailRow.Height = narrow ? GridLength.Auto : new GridLength(0);
        HistoryListScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HistoryListScrollViewer.MaxHeight = narrow ? 360 : double.PositiveInfinity;
        HistoryDetailScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HistoryPanel.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        HistoryLayout.Height = narrow ? double.NaN : availableHeight;
        HistoryLayout.MinHeight = 0;

        Grid.SetColumn(HistoryDetailPanel, narrow ? 0 : 1);
        Grid.SetRow(HistoryDetailPanel, narrow ? 1 : 0);
    }

    private void ApplyAboutResponsiveLayout(bool narrow)
    {
        var cards = AboutLayout.Children.OfType<FrameworkElement>().ToList();
        AboutLayout.ColumnDefinitions.Clear();
        AboutLayout.RowDefinitions.Clear();

        if (narrow)
        {
            AboutLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < cards.Count; i++)
            {
                AboutLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(cards[i], i);
                Grid.SetColumn(cards[i], 0);
            }
            return;
        }

        AboutLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AboutLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < Math.Ceiling(cards.Count / 2.0); i++)
            AboutLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < cards.Count; i++)
        {
            Grid.SetRow(cards[i], i / 2);
            Grid.SetColumn(cards[i], i % 2);
        }
    }

    private void ApplySettingsResponsiveLayout(bool narrow)
    {
        var cards = SettingsLayout.Children.OfType<FrameworkElement>().ToList();
        SettingsLayout.ColumnDefinitions.Clear();
        SettingsLayout.RowDefinitions.Clear();

        if (narrow)
        {
            SettingsLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < cards.Count; i++)
            {
                SettingsLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(cards[i], i);
                Grid.SetColumn(cards[i], 0);
            }
            return;
        }

        SettingsLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SettingsLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < Math.Ceiling(cards.Count / 2.0); i++)
        {
            SettingsLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var i = 0; i < cards.Count; i++)
        {
            Grid.SetRow(cards[i], i / 2);
            Grid.SetColumn(cards[i], i % 2);
        }
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
        if (_isRunning) return;
        var picker = new FileOpenPicker();
        foreach (var extension in GetAllowedExtensions(_selectedCommand))
            picker.FileTypeFilter.Add(extension);
        if (App.MainWindow is not null)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        }

        var files = await picker.PickMultipleFilesAsync();
        AddFiles(files.Select(f => f.Path));
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = _isRunning ? DataPackageOperation.None : DataPackageOperation.Copy;
        SetDropZoneHot(!_isRunning);
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (_isRunning) return;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        AddFiles(items.OfType<StorageFile>().Select(f => f.Path));
        SetDropZoneHot(false);
    }

    private void SetDropZoneHot(bool isHot)
    {
        DropZone.Background = (Brush)Application.Current.Resources[isHot ? "CardBackgroundFillColorSecondaryBrush" : "CardBackgroundFillColorDefaultBrush"];
        DropZone.BorderBrush = (Brush)Application.Current.Resources[isHot ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush"];
        DropZoneIcon.Opacity = isHot ? 1 : 0.85;
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

    private async Task RunStartupCommandAsync()
    {
        if (_startupCommandHandled) return;
        _startupCommandHandled = true;

        var args = SplitCommandLine(_startupArguments);
        if (args.Count < 2 || !IsKnownCommand(args[0])) return;

        SelectNavItem("Convert");
        string command = args[0];
        AddFiles(ExpandDirectoryArguments(command, args.Skip(1)));
        SelectCommand(command);
        await StartConversionAsync();
    }

    private static List<string> SplitCommandLine(string value)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        bool inQuote = false;

        foreach (char ch in value)
        {
            if (ch == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuote)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
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

    private void SelectCommand(string command)
    {
        if (!IsCommandCompatibleWithSelectedFiles(command)) return;
        _selectedCommand = command;
        UpdateCommandAvailability();
        CommandStatusText.Text = string.Format(L("fluent_selected_command"), CommandLabel(command));
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
        UpdateCommandAvailability();
        UpdateStartState();
    }

    private void UpdateStartState()
    {
        bool canStart = _selectedFiles.Count > 0 &&
                        _selectedCommand is not null &&
                        IsCommandCompatibleWithSelectedFiles(_selectedCommand);
        StartButton.Visibility = _isRunning ? Visibility.Collapsed : Visibility.Visible;
        StartButton.IsEnabled = canStart;
        CancelButton.Visibility = _isRunning ? Visibility.Visible : Visibility.Collapsed;
        UpdateInteractiveState();
    }

    private void UpdateCommandAvailability()
    {
        foreach (var pair in _commandButtons)
        {
            bool compatible = IsCommandCompatibleWithSelectedFiles(pair.Key);
            pair.Value.IsEnabled = !_isRunning && compatible;
            pair.Value.Style = compatible && pair.Key.Equals(_selectedCommand, StringComparison.OrdinalIgnoreCase)
                ? (Style)Application.Current.Resources["AccentButtonStyle"]
                : null;
        }

        if (_selectedCommand is not null && !IsCommandCompatibleWithSelectedFiles(_selectedCommand))
        {
            _selectedCommand = null;
            CommandStatusText.Text = L("fluent_choose_command");
        }
    }

    private void UpdateInteractiveState()
    {
        DropZone.AllowDrop = !_isRunning;
        DropZone.Opacity = _isRunning ? 0.55 : 1.0;
        ClearFilesButton.IsEnabled = !_isRunning && _selectedFiles.Count > 0;
        UpdateCommandAvailability();
    }

    private bool IsCommandCompatibleWithSelectedFiles(string command)
    {
        if (_selectedFiles.Count == 0) return true;
        int minFiles = command is "merge-pdf" or "img-merge" or "img-stitch" ? 2 : 1;
        if (_selectedFiles.Count < minFiles) return false;
        string[] extensions = GetAllowedExtensions(command);
        return _selectedFiles.All(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
    }

    private async Task StartConversionAsync()
    {
        if (_selectedCommand is null || _selectedFiles.Count == 0 || _isRunning) return;
        if (!ValidateSelection(_selectedCommand, out var error))
        {
            await ShowErrorAsync(error);
            return;
        }
        if (!OfficeEnginePreflight.TryValidate(_selectedCommand, L, out error))
        {
            await ShowErrorAsync(error);
            return;
        }

        _isRunning = true;
        _cts = new CancellationTokenSource();
        UpdateStartState();
        ConversionProgressSection.Visibility = Visibility.Visible;
        SetProgress(0, L("fluent_progress_starting"));

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
            SetProgress(100, L("fluent_progress_completed"));
            ShowToast(L("fluent_toast_done_title"), string.Format(L("fluent_toast_done_body"), CommandLabel(command), files.Count));
            _selectedFiles.Clear();
            RefreshFiles();
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            ClickraStorage.CompleteActiveRecord(command, startTime, false, "Canceled", elapsedMs: stopwatch.ElapsedMilliseconds, inputPaths: inputs, outputPath: string.Join(";", outputs));
            SetProgress(0, L("fluent_progress_canceled"));
            ShowToast(L("fluent_toast_canceled_title"), string.Format(L("fluent_toast_canceled_body"), CommandLabel(command)));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            ClickraStorage.CompleteActiveRecord(command, startTime, false, ex.Message, elapsedMs: stopwatch.ElapsedMilliseconds, inputPaths: inputs, outputPath: string.Join(";", outputs));
            SetProgress(0, string.Format(L("fluent_progress_failed"), ex.Message));
            ShowToast(L("fluent_toast_failed_title"), ex.Message);
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
                    FileProcessor.CompressPdf(files[i], outputs[i], CompressionOptions(), (c, t, m) => Progress((i * 100) + c, files.Count * 100, m), token);
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
            case "split-pdf":
                string splitPages = DispatcherQueue.EnqueueAsync(PromptSplitPagesAsync).GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(splitPages)) throw new OperationCanceledException(token);
                for (int i = 0; i < files.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    FileProcessor.SplitPdf(files[i], outputs[i], splitPages, (c, t, m) => Progress((i * 100) + c, files.Count * 100, m), token);
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
        string[] extensions = GetAllowedExtensions(command);
        int minFiles = command is "merge-pdf" or "img-merge" or "img-stitch" ? 2 : 1;
        if (_selectedFiles.Count < minFiles)
        {
            error = string.Format(L("fluent_validate_min_files"), CommandLabel(command), minFiles);
            return false;
        }
        var bad = _selectedFiles.FirstOrDefault(f => !extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        if (bad is not null)
        {
            error = string.Format(L("fluent_validate_bad_ext"), Path.GetFileName(bad), CommandLabel(command));
            return false;
        }
        return true;
    }

    private static bool IsKnownCommand(string command) => GetAllowedExtensions(command).Length > 0;

    private static string[] GetAllowedExtensions(string? command) => command switch
    {
        "ppt2pdf" => new[] { ".ppt", ".pptx" },
        "word2pdf" => new[] { ".doc", ".docx" },
        "excel2pdf" => new[] { ".xls", ".xlsx" },
        "merge-pdf" or "compress-pdf" or "translate-pdf" or "decrypt-pdf" or "split-pdf" => new[] { ".pdf" },
        "img2pdf" or "img-merge" or "img-stitch" => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" },
        _ => new[] { ".pdf", ".ppt", ".pptx", ".doc", ".docx", ".xls", ".xlsx", ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" }
    };

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
            "split-pdf" => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + "_split.pdf")).ToList(),
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
        ApplyLanguage();
        RefreshLibreOfficeStatus();

        OutputDirCombo.SelectionChanged += async (_, _) => await SaveOutputDirAsync();
        EngineCombo.SelectionChanged += (_, _) => SaveSettings();
        LanguageCombo.SelectionChanged += (_, _) =>
        {
            SaveSettings();
            ApplyLanguage();
            RefreshFiles();
            RefreshHistory();
        };
        PdfLangCombo.SelectionChanged += (_, _) => SaveSettings();
        CompressionSlider.ValueChanged += (_, _) => { SaveSettings(); UpdateCompressionLabel(); };
        StripFontsToggle.Toggled += (_, _) => SaveSettings();
        MinifyContentToggle.Toggled += (_, _) => SaveSettings();
        QuietModeToggle.Toggled += (_, _) => SaveSettings();
        NotificationToggle.Toggled += (_, _) => SaveSettings();
    }

    private void ApplyLanguage()
    {
        NavOverviewItem.Content = L("fluent_nav_overview");
        NavConvertItem.Content = L("fluent_nav_convert");
        NavHistoryItem.Content = L("fluent_nav_history");
        NavSettingsItem.Content = L("fluent_nav_settings");
        NavAboutItem.Content = L("fluent_nav_about");

        OverviewConvertTitle.Text = L("fluent_overview_title");
        OverviewConvertSubtitle.Text = L("fluent_overview_subtitle");
        OpenConvertButton.Content = L("fluent_choose_files");
        ViewHistoryButton.Content = L("fluent_recent_jobs");
        OverviewRecentTitle.Text = L("fluent_recent_activity");
        OverviewNoHistoryText.Text = L("fluent_no_history");
        OverviewActivityTitle.Text = L("fluent_activity_summary");
        OverviewTotalLabel.Text = L("fluent_total");
        OverviewOkLabel.Text = L("fluent_success");
        OverviewFailLabel.Text = L("fluent_failed");
        OverviewToolsLabel.Text = L("fluent_tools");
        OverviewToolsReady.Text = L("fluent_ready");
        OverviewExplorerLabel.Text = L("fluent_explorer_menu");
        OverviewExplorerReady.Text = L("fluent_available");
        OverviewPdfDesc.Text = L("fluent_pdf_desc");
        OverviewOfficeTitle.Text = L("fluent_office");
        OverviewOfficeDesc.Text = L("fluent_office_desc");
        OverviewImagesTitle.Text = L("fluent_images");
        OverviewImagesDesc.Text = L("fluent_images_desc");

        ConvertTitle.Text = L("fluent_nav_convert");
        DropZoneTitle.Text = L("fluent_drop_title");
        DropZoneBrowseText.Text = L("fluent_drop_browse");
        DropZoneTypesText.Text = L("fluent_drop_types");
        SelectedFilesTitle.Text = L("fluent_selected_files");
        SelectedFilesDesc.Text = L("fluent_selected_files_desc");
        ClearFilesButton.Content = L("fluent_clear");
        EmptyFileMessage.Text = L("fluent_no_files");
        CommandTitle.Text = L("fluent_command");
        CommandStatusText.Text = _selectedCommand is null ? L("fluent_choose_command") : string.Format(L("fluent_selected_command"), CommandLabel(_selectedCommand));
        OfficeCommandLabel.Text = L("fluent_office");
        PdfCommandLabel.Text = "PDF";
        ImageCommandLabel.Text = L("fluent_images");
        BtnWord2Pdf.Content = "Word";
        BtnExcel2Pdf.Content = "Excel";
        BtnPpt2Pdf.Content = "PPT";
        BtnMergePdf.Content = L("cmd_merge_pdf");
        BtnCompressPdf.Content = L("cmd_compress_pdf");
        BtnTranslatePdf.Content = L("cmd_translate_pdf");
        BtnDecryptPdf.Content = L("cmd_decrypt_pdf");
        BtnImg2Pdf.Content = "PDF";
        BtnImgMerge.Content = L("cmd_merge_img");
        BtnImgStitch.Content = L("cmd_stitch_img");
        RunTitle.Text = L("fluent_run");
        StartButton.Content = L("fluent_start");
        CancelButton.Content = L("fluent_cancel");
        if (!_isRunning) ConversionProgressText.Text = L("fluent_ready");

        HistoryTitle.Text = L("fluent_nav_history");
        HistorySubtitle.Text = L("fluent_history_subtitle");
        ClearHistoryButton.Content = L("fluent_clear");
        HistoryTotalLabel.Text = L("fluent_total");
        HistorySuccessLabel.Text = L("fluent_success");
        HistoryFailedLabel.Text = L("fluent_failed");
        ActiveJobTitle.Text = L("fluent_run");
        EmptyHistoryText.Text = L("fluent_no_history");

        SettingsTitle.Text = L("fluent_nav_settings");
        SettingsSubtitle.Text = L("fluent_settings_subtitle");
        OutputDirTitle.Text = L("fluent_output_dir");
        OutputDirDesc.Text = L("fluent_output_dir_desc");
        OutputDirSourceItem.Content = L("fluent_output_source");
        OutputDirDesktopItem.Content = L("fluent_desktop");
        OutputDirDownloadsItem.Content = L("fluent_downloads");
        OutputDirCustomItem.Content = L("fluent_custom");
        OfficeEngineTitle.Text = L("fluent_office_engine");
        OfficeEngineDesc.Text = L("fluent_office_engine_desc");
        EngineAutoItem.Content = L("fluent_auto");
        EngineMicrosoftItem.Content = "Microsoft Office";
        EngineLibreOfficeItem.Content = "LibreOffice";
        LanguageTitle.Text = L("fluent_default_language");
        LanguageDesc.Text = L("fluent_default_language_desc");
        PdfTargetTitle.Text = L("fluent_pdf_target");
        BehaviorTitle.Text = L("fluent_behavior");
        QuietModeTitle.Text = L("fluent_quiet_mode");
        QuietModeDesc.Text = L("fluent_quiet_mode_desc");
        NotificationTitle.Text = L("fluent_notifications");
        NotificationDesc.Text = L("fluent_notifications_desc");
        PdfCompressionTitle.Text = L("fluent_pdf_compression");
        StripFontsTitle.Text = L("fluent_strip_fonts");
        MinifyContentTitle.Text = L("fluent_minify_content");
        LibreOfficeBrowseButton.Content = L("setting_libreoffice_browse");
        LibreOfficeDownloadButton.Content = L("setting_libreoffice_download");
        LibreOfficeUninstallButton.Content = L("setting_libreoffice_uninstall");
        UpdateCompressionLabel();

        AboutDescription.Text = L("fluent_about_desc");
        GitHubText.Text = L("about_btn_github");
        OpenDataDirText.Text = L("about_btn_open_data_dir");
        GmailText.Text = L("about_btn_gmail");
        PlatformLabel.Text = L("fluent_platform");
        AppModelLabel.Text = L("fluent_app_model");
        RefreshLibreOfficeStatus();
    }

    private void UpdateCompressionLabel()
    {
        CompressionLabel.Text = CompressionLevel() switch
        {
            "small" => L("setting_pdf_compress_level_small"),
            "high" => L("setting_pdf_compress_level_high"),
            _ => L("setting_pdf_compress_level_std")
        };
    }

    private void SaveSettings()
    {
        if (_loadingSettings) return;
        ClickraStorage.SaveSetting("OfficeEngine", EngineCombo.SelectedIndex switch { 1 => "microsoft", 2 => "libreoffice", _ => "auto" });
        ClickraStorage.SaveSetting("Language", LanguageCombo.SelectedIndex switch { 1 => "zh-CN", 2 => "en-US", 3 => "ja-JP", 4 => "ko-KR", _ => "zh-TW" });
        ClickraStorage.SaveSetting("TranslateTargetLang", PdfLangCombo.SelectedIndex switch { 1 => "en", 2 => "zh-CN", 3 => "ja", 4 => "ko", _ => "zh-TW" });
        ClickraStorage.SaveSetting("PdfCompressImageLevel", ((int)CompressionSlider.Value).ToString());
        ClickraStorage.SaveSetting("PdfCompressStripFonts", StripFontsToggle.IsOn ? "true" : "false");
        ClickraStorage.SaveSetting("PdfCompressMinifyContent", MinifyContentToggle.IsOn ? "true" : "false");
        ClickraStorage.SaveSetting("QuietMode", QuietModeToggle.IsOn ? "true" : "false");
        ClickraStorage.SaveSetting("Notification", NotificationToggle.IsOn ? "true" : "false");
        RefreshLibreOfficeStatus();
    }

    private async Task SaveOutputDirAsync()
    {
        if (_loadingSettings) return;
        if (OutputDirCombo.SelectedIndex == 3)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            if (App.MainWindow is not null)
            {
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                _loadingSettings = true;
                OutputDirCombo.SelectedIndex = ClickraStorage.GetSetting("OutputDir") switch { "desktop" => 1, "downloads" => 2, var s when !string.IsNullOrWhiteSpace(s) && s != "source" => 3, _ => 0 };
                _loadingSettings = false;
                return;
            }
            ClickraStorage.SaveSetting("OutputDir", folder.Path);
            return;
        }

        ClickraStorage.SaveSetting("OutputDir", OutputDirCombo.SelectedIndex switch { 1 => "desktop", 2 => "downloads", _ => "source" });
    }

    private void RefreshHistory()
    {
        _historyEntries = ClickraStorage.GetHistory(20);
        StatTotal.Text = _historyEntries.Count.ToString();
        StatSuccess.Text = _historyEntries.Count(h => h.IsSuccess).ToString();
        StatFailed.Text = _historyEntries.Count(h => !h.IsSuccess).ToString();
        HistoryTotalText.Text = _historyEntries.Count.ToString();
        HistorySuccessText.Text = _historyEntries.Count(h => h.IsSuccess).ToString();
        HistoryFailedText.Text = _historyEntries.Count(h => !h.IsSuccess).ToString();
        RenderOverviewHistory(_historyEntries);

        EmptyHistoryState.Visibility = _historyEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryListContainer.Visibility = _historyEntries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        HistoryListContainer.Children.Clear();

        if (_historyEntries.Count == 0)
        {
            _selectedHistoryIndex = -1;
            RenderEmptyHistoryDetail();
            return;
        }

        if (_selectedHistoryIndex < 0 || _selectedHistoryIndex >= _historyEntries.Count)
        {
            _selectedHistoryIndex = 0;
        }

        for (int i = 0; i < _historyEntries.Count; i++)
        {
            HistoryListContainer.Children.Add(CreateHistoryListItem(_historyEntries[i], i));
        }

        RenderHistoryDetail(_historyEntries[_selectedHistoryIndex]);
    }

    private void RenderOverviewHistory(IReadOnlyList<ClickraStorage.HistoryEntry> history)
    {
        OverviewRecentContainer.Children.Clear();
        OverviewNoHistoryText.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var entry in history.Take(3))
        {
            var statusBrush = new SolidColorBrush(entry.IsSuccess ? Colors.LimeGreen : Colors.IndianRed);
            var row = new Grid
            {
                ColumnSpacing = 10,
                Padding = new Thickness(12, 10, 12, 10),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                CornerRadius = new CornerRadius(8)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = statusBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            var title = new StackPanel { Spacing = 2 };
            title.Children.Add(new TextBlock
            {
                Text = CommandLabel(entry.Command),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            title.Children.Add(new TextBlock
            {
                Text = entry.Time,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var elapsed = new TextBlock
            {
                Text = FormatElapsed(entry.ElapsedMs),
                FontSize = 12,
                Foreground = statusBrush,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(dot, 0);
            Grid.SetColumn(title, 1);
            Grid.SetColumn(elapsed, 2);
            row.Children.Add(dot);
            row.Children.Add(title);
            row.Children.Add(elapsed);
            OverviewRecentContainer.Children.Add(row);
        }
    }

    private Button CreateHistoryListItem(ClickraStorage.HistoryEntry entry, int index)
    {
        var selected = index == _selectedHistoryIndex;
        var statusBrush = new SolidColorBrush(entry.IsSuccess ? Colors.LimeGreen : Colors.IndianRed);

        var row = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(12, 10, 12, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var status = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = statusBrush,
            VerticalAlignment = VerticalAlignment.Center
        };

        var title = new StackPanel { Spacing = 3 };
        title.Children.Add(new TextBlock
        {
            Text = CommandLabel(entry.Command),
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        title.Children.Add(new TextBlock
        {
            Text = $"{entry.Time} · {entry.FileCount} {L("fluent_file_count")}",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var result = new StackPanel
        {
            Spacing = 3,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        result.Children.Add(new TextBlock
        {
            Text = entry.IsSuccess ? L("fluent_success") : L("fluent_failed"),
            FontSize = 13,
            Foreground = statusBrush,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        result.Children.Add(new TextBlock
        {
            Text = FormatElapsed(entry.ElapsedMs),
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Right
        });

        Grid.SetColumn(status, 0);
        Grid.SetColumn(title, 1);
        Grid.SetColumn(result, 2);
        row.Children.Add(status);
        row.Children.Add(title);
        row.Children.Add(result);

        var button = new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources[selected ? "CardBackgroundFillColorSecondaryBrush" : "CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources[selected ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(selected ? 2 : 1),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8)
        };
        button.Click += (_, _) => SelectHistoryEntry(index);
        return button;
    }

    private void SelectHistoryEntry(int index)
    {
        if (index < 0 || index >= _historyEntries.Count) return;
        _selectedHistoryIndex = index;
        RefreshHistory();
    }

    private void RenderEmptyHistoryDetail()
    {
        HistoryDetailContainer.Children.Clear();
        HistoryDetailContainer.Children.Add(new FontIcon
        {
            Glyph = "\uE81C",
            FontSize = 42,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 120, 0, 0)
        });
        HistoryDetailContainer.Children.Add(new TextBlock
        {
            Text = L("fluent_select_history"),
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Center
        });
    }

    private void RenderHistoryDetail(ClickraStorage.HistoryEntry entry)
    {
        HistoryDetailContainer.Children.Clear();
        var statusBrush = new SolidColorBrush(entry.IsSuccess ? Colors.LimeGreen : Colors.IndianRed);

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel { Spacing = 4 };
        title.Children.Add(new TextBlock
        {
            Text = CommandLabel(entry.Command),
            FontSize = 26,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        title.Children.Add(new TextBlock
        {
            Text = entry.Time,
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        var status = new Border
        {
            Background = new SolidColorBrush(entry.IsSuccess ? Color.FromArgb(36, 57, 211, 83) : Color.FromArgb(40, 255, 107, 107)),
            BorderBrush = statusBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = entry.IsSuccess ? L("fluent_success") : L("fluent_failed"),
                FontSize = 13,
                Foreground = statusBrush
            }
        };

        Grid.SetColumn(status, 1);
        header.Children.Add(title);
        header.Children.Add(status);
        HistoryDetailContainer.Children.Add(header);

        var facts = new Grid
        {
            ColumnSpacing = 12,
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14)
        };
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddFact(facts, 0, L("fluent_files"), entry.FileCount.ToString());
        AddFact(facts, 1, L("fluent_elapsed"), FormatElapsed(entry.ElapsedMs));
        AddFact(facts, 2, L("fluent_result"), entry.IsSuccess ? L("fluent_success") : L("fluent_failed"), statusBrush);
        HistoryDetailContainer.Children.Add(facts);

        AddDetailSection(L("fluent_input_paths"), SplitPaths(entry.InputPaths));
        AddDetailSection(L("fluent_output_paths"), SplitPaths(entry.OutputPath));
        if (!entry.IsSuccess && !string.IsNullOrWhiteSpace(entry.ErrorMessage))
        {
            AddDetailSection(L("fluent_error_message"), entry.ErrorMessage, statusBrush, true);
        }
    }

    private static void AddFact(Grid grid, int column, string label, string value, Brush? valueBrush = null)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = valueBrush ?? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
            TextWrapping = TextWrapping.Wrap
        });

        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
    }

    private void AddDetailSection(string label, string value, Brush? valueBrush = null, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        HistoryDetailContainer.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Margin = new Thickness(0, 4, 0, -6)
        });

        HistoryDetailContainer.Children.Add(new Border
        {
            Background = isError ? new SolidColorBrush(Color.FromArgb(32, 255, 107, 107)) : (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            BorderBrush = isError ? valueBrush : (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = new TextBlock
            {
                Text = value,
                FontSize = 13,
                Foreground = valueBrush ?? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                TextWrapping = TextWrapping.Wrap
            }
        });
    }

    private async Task ClearHistoryAsync()
    {
        if (!await ConfirmAsync(L("history_clear_confirm"))) return;
        ClickraStorage.ClearHistory();
        _selectedHistoryIndex = -1;
        RefreshHistory();
    }

    private void RefreshLibreOfficeStatus()
    {
        string resolvedPath = LibreOfficeHelper.GetResolvedExecutablePath();
        bool removalPending = ClickraStorage.GetSetting("LibreOfficeRemovalPendingRestart").Equals("true", StringComparison.OrdinalIgnoreCase);
        bool ready = !string.IsNullOrWhiteSpace(resolvedPath);
        string installedVersion = LibreOfficeEngineInstaller.GetInstalledSystemVersion();

        LibreOfficeStatusText.Text = _libreOfficeSetupInProgress
            ? LibreOfficeStatusText.Text
            : removalPending
                ? L("setting_libreoffice_removal_pending")
                : ready
                    ? string.IsNullOrWhiteSpace(installedVersion)
                        ? L("setting_libreoffice_ready")
                        : $"{L("setting_libreoffice_ready")} · {installedVersion}"
                    : L("setting_libreoffice_missing");
        LibreOfficePathText.Text = ready ? resolvedPath : "";
        LibreOfficeSetupProgress.Visibility = _libreOfficeSetupInProgress ? Visibility.Visible : Visibility.Collapsed;
        LibreOfficeBrowseButton.IsEnabled = !_libreOfficeSetupInProgress;
        LibreOfficeDownloadButton.IsEnabled = !_libreOfficeSetupInProgress;
        LibreOfficeUninstallButton.IsEnabled = !_libreOfficeSetupInProgress && (ready || removalPending);
    }

    private async Task BrowseLibreOfficeAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add(".com");
        if (App.MainWindow is not null)
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        if (!LibreOfficeHelper.LooksLikeLibreOfficeExecutable(file.Path))
        {
            await ShowErrorAsync(L("setting_libreoffice_invalid"));
            return;
        }

        ClickraStorage.SaveSetting("LibreOfficePath", file.Path);
        ClickraStorage.SaveSetting("LibreOfficeRemovalPendingRestart", "false");
        RefreshLibreOfficeStatus();
    }

    private async Task InstallLibreOfficeAsync()
    {
        if (_libreOfficeSetupInProgress)
        {
            await ShowErrorAsync(L("setting_libreoffice_download_in_progress"));
            return;
        }

        bool removalPending = ClickraStorage.GetSetting("LibreOfficeRemovalPendingRestart").Equals("true", StringComparison.OrdinalIgnoreCase);
        var package = LibreOfficeEngineInstaller.RecommendedPackage;
        string installedVersion = LibreOfficeEngineInstaller.GetInstalledSystemVersion();
        if (!removalPending && !string.IsNullOrWhiteSpace(installedVersion) && LibreOfficeEngineInstaller.IsRecommendedVersionInstalled())
        {
            string resolvedPath = LibreOfficeEngineInstaller.ResolveSystemSofficePath();
            if (!string.IsNullOrWhiteSpace(resolvedPath))
                ClickraStorage.SaveSetting("LibreOfficePath", resolvedPath);
            await ShowErrorAsync(string.Format(L("setting_libreoffice_already_current"), installedVersion));
            RefreshLibreOfficeStatus();
            return;
        }

        string prompt = string.Format(
            L("setting_libreoffice_download_prompt"),
            package.Version,
            package.Edition,
            FormatBytes(package.DownloadBytes),
            LibreOfficeEngineInstaller.GetDefaultInstallRoot(),
            package.Sha256);
        if (!await ConfirmAsync(prompt)) return;

        _libreOfficeSetupInProgress = true;
        LibreOfficeSetupProgress.Value = 0;
        LibreOfficeStatusText.Text = L(removalPending ? "setting_libreoffice_reinstall_starting" : "setting_libreoffice_download_starting");
        RefreshLibreOfficeStatus();

        try
        {
            string downloadDir = Path.Combine(ClickraStorage.GetDataDir(), "downloads");
            var progress = new Progress<int>(percent =>
            {
                int displayPercent = Math.Min(80, Math.Max(1, percent * 80 / 100));
                LibreOfficeSetupProgress.Value = displayPercent;
                LibreOfficeStatusText.Text = percent >= 100
                    ? L("setting_libreoffice_verifying")
                    : string.Format(L("setting_libreoffice_download_progress"), percent);
            });

            string installerPath = await LibreOfficeEngineInstaller.DownloadAndVerifyAsync(
                package,
                downloadDir,
                progress,
                CancellationToken.None);

            LibreOfficeSetupProgress.Value = 85;
            LibreOfficeStatusText.Text = L("setting_libreoffice_installing");
            LibreOfficeInstallResult result = await LibreOfficeEngineInstaller.InstallMsiPackageAsync(installerPath, CancellationToken.None);
            string sofficePath = result.SofficePath;
            if (!result.RestartRequired && !LibreOfficeHelper.LooksLikeLibreOfficeExecutable(sofficePath))
                throw new InvalidOperationException(L("setting_libreoffice_validation_failed"));

            if (!string.IsNullOrWhiteSpace(sofficePath))
                ClickraStorage.SaveSetting("LibreOfficePath", sofficePath);
            ClickraStorage.SaveSetting("LibreOfficeInstalledByClickra", "true");
            ClickraStorage.SaveSetting("LibreOfficeRemovalPendingRestart", "false");
            LibreOfficeSetupProgress.Value = 100;

            await ShowErrorAsync(string.Format(
                L(result.RestartRequired ? "setting_libreoffice_install_restart_required" : "setting_libreoffice_download_ready"),
                string.IsNullOrWhiteSpace(sofficePath) ? LibreOfficeEngineInstaller.GetDefaultInstallRoot() : sofficePath));
        }
        catch (Exception ex)
        {
            ClickraStorage.SaveSetting("LibreOfficePath", "");
            await ShowErrorAsync(string.Format(L("setting_libreoffice_download_failed"), ex.Message));
        }
        finally
        {
            _libreOfficeSetupInProgress = false;
            RefreshLibreOfficeStatus();
        }
    }

    private async Task UninstallLibreOfficeAsync()
    {
        if (_libreOfficeSetupInProgress)
        {
            await ShowErrorAsync(L("setting_libreoffice_download_in_progress"));
            return;
        }
        if (ClickraStorage.GetSetting("LibreOfficeRemovalPendingRestart").Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            await ShowErrorAsync(L("setting_libreoffice_removal_pending"));
            return;
        }
        if (!await ConfirmAsync(L("setting_libreoffice_uninstall_confirm"))) return;

        _libreOfficeSetupInProgress = true;
        LibreOfficeSetupProgress.Value = 60;
        LibreOfficeStatusText.Text = L("setting_libreoffice_uninstalling");
        RefreshLibreOfficeStatus();
        try
        {
            LibreOfficeUninstallResult result = await LibreOfficeEngineInstaller.UninstallSystemLibreOfficeAsync(CancellationToken.None);
            ClickraStorage.SaveSetting("LibreOfficePath", "");
            ClickraStorage.SaveSetting("LibreOfficeInstalledByClickra", "false");
            ClickraStorage.SaveSetting("LibreOfficeRemovalPendingRestart", result.RestartRequired ? "true" : "false");
            ClickraStorage.SaveSetting("OfficeEngine", "auto");
            _loadingSettings = true;
            EngineCombo.SelectedIndex = 0;
            _loadingSettings = false;
            await ShowErrorAsync(L(result.RestartRequired ? "setting_libreoffice_uninstall_restart_required" : "setting_libreoffice_uninstall_ready"));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(string.Format(L("setting_libreoffice_uninstall_failed"), ex.Message));
        }
        finally
        {
            _libreOfficeSetupInProgress = false;
            RefreshLibreOfficeStatus();
        }
    }

    private async Task OpenUriAsync(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async Task OpenDataDirAsync()
    {
        try
        {
            string logPath;
            try
            {
                // 使用官方 WinRT API 直接獲取硬碟實體路徑
                string localPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                logPath = Path.Combine(localPath, "history.log");
            }
            catch
            {
                logPath = Path.Combine(ClickraStorage.GetDataDir(), "history.log");
            }

            string? dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(logPath))
                File.WriteAllText(logPath, "");

            // 喚醒檔案總管並使用 /select 自動高亮選中 history.log 檔案
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{logPath}\"",
                UseShellExecute = true
            })?.Dispose();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async Task OpenDiagnosticsEmailAsync()
    {
        await OpenDataDirAsync();
        var version = typeof(MainPage).Assembly.GetName().Version;
        string versionText = version is null ? "Unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
        string subject = Uri.EscapeDataString("Clickra Diagnostics Report");
        string body = Uri.EscapeDataString(
            "感謝您提交 Clickra 診斷回報！\r\n\r\n" +
            "請直接將已為您選取好的「history.log」拖曳到此郵件中作為附件。\r\n\r\n" +
            "[系統資訊]\r\n" +
            "作業系統: Windows\r\n" +
            $"Clickra 版本: {versionText}\r\n" +
            $"時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n\r\n" +
            "[問題描述]\r\n" +
            "（請在此處填寫您遇到的問題...）");
        await OpenUriAsync($"https://mail.google.com/mail/?view=cm&fs=1&to=jiangyouchen%40gmail.com&su={subject}&body={body}");
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Clickra",
            Content = message,
            PrimaryButtonText = L("fluent_ok"),
            CloseButtonText = L("fluent_cancel"),
            XamlRoot = XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private static string FormatElapsed(long elapsedMs) => elapsedMs >= 0 ? $"{elapsedMs / 1000.0:F2}s" : "-";

    private static string SplitPaths(string paths) => string.Join(Environment.NewLine, paths.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private string CompressionLevel() => ((int)CompressionSlider.Value) switch
    {
        0 => "small",
        2 or 3 => "high",
        _ => "balanced"
    };

    private Dictionary<string, object> CompressionOptions() => new()
    {
        ["level"] = CompressionLevel(),
        ["strip_fonts"] = StripFontsToggle.IsOn,
        ["minify_content"] = MinifyContentToggle.IsOn
    };

    private async Task<string> PromptPasswordAsync()
    {
        var box = new PasswordBox { PlaceholderText = L("fluent_pdf_password") };
        var dialog = new ContentDialog
        {
            Title = L("fluent_pdf_password"),
            Content = box,
            PrimaryButtonText = L("fluent_ok"),
            CloseButtonText = L("fluent_cancel"),
            XamlRoot = XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Password : "";
    }

    private async Task<string> PromptSplitPagesAsync()
    {
        var box = new TextBox { Text = "all", PlaceholderText = L("pdf_split_prompt") };
        var dialog = new ContentDialog
        {
            Title = L("pdf_split_title"),
            Content = box,
            PrimaryButtonText = L("fluent_ok"),
            CloseButtonText = L("fluent_cancel"),
            XamlRoot = XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? (string.IsNullOrWhiteSpace(box.Text) ? "all" : box.Text.Trim()) : "";
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Clickra",
            Content = message,
            CloseButtonText = L("fluent_ok"),
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private static void ShowToast(string title, string body)
    {
        if (ClickraStorage.GetSetting("Notification").Equals("false", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            static string Escape(string value) => value.Replace("'", "''").Replace("`", "``").Replace("\"", "`\"");
            var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
$textNodes = $template.GetElementsByTagName('text')
$textNodes.Item(0).AppendChild($template.CreateTextNode('{Escape(title)}')) | Out-Null
$textNodes.Item(1).AppendChild($template.CreateTextNode('{Escape(body)}')) | Out-Null
$toast = [Windows.UI.Notifications.ToastNotification]::new($template)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Clickra').Show($toast)";

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.Dispose();
        }
        catch { }
    }

    private string CommandLabel(string command) => command switch
    {
        "ppt2pdf" => L("cmd_ppt_to_pdf"),
        "word2pdf" => L("cmd_word_to_pdf"),
        "excel2pdf" => L("cmd_excel_to_pdf"),
        "merge-pdf" => L("cmd_merge_pdf"),
        "compress-pdf" => L("cmd_compress_pdf"),
        "translate-pdf" => L("cmd_translate_pdf"),
        "decrypt-pdf" => L("cmd_decrypt_pdf"),
        "split-pdf" => L("cmd_split_pdf"),
        "img2pdf" => L("cmd_img_to_pdf"),
        "img-merge" => L("cmd_merge_img"),
        "img-stitch" => L("cmd_stitch_img"),
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

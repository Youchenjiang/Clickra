using Clickra.Core.Modules.Settings.Application;
using Clickra.Core;
using Clickra.Core.Presentation.Convert;
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
    private const string GitHubUrl = "https://github.com/Youchenjiang/Clickra"; // NOSONAR:S1075 — the project's canonical repository URL.
    private const string SimplifiedChineseLanguage = "zh-CN";
    private const string SuccessLocalizationKey = "fluent_success";
    private const string FailedLocalizationKey = "fluent_failed";
    private const string ChooseCommandLocalizationKey = "fluent_choose_command";
    // 歷史清單顯示幾筆已完成紀錄（與 Core 的 HistoryFeed.Load 預設一致）。
    private const string SecondaryCardBrushResource = "CardBackgroundFillColorSecondaryBrush";
    private const string SecondaryTextBrushResource = "TextFillColorSecondaryBrush";
    private readonly DashboardPresentationController _dashboardPresentation = new();
    private readonly ConvertWorkspaceController _convertWorkspace = new();
    private readonly Dictionary<string, Button> _commandButtons = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private bool _loadingSettings;
    private bool _startupCommandHandled;
    private string _startupArguments = "";
    // 一份清單三個切片（Core 的 HistoryFeed，與 Win32 dashboard 共用）：進行中、待繼續、
    // 已完成。這頁只是它的另一個呈現層，所以狀態用字、檔案描述與剩餘期限都讀模型的欄位。
    private bool _settingsReloadHooked;
    private bool _syncingParkedRetention;
    private readonly DynamicSettingsController _dynamicSettings;

    public MainPage()
    {
        InitializeComponent();
        _dynamicSettings = new DynamicSettingsController(
            SettingsLayout,
            ParkedRetentionCard,
            L,
            () => _loadingSettings,
            () => ApplySettingsResponsiveLayout(ActualWidth < 1000));
        Loaded += async (_, _) =>
        {
            ApplyResponsiveLayout();
            HookMainWindowActivatedForParkedRefresh();
            HookExternalSettingsReload();
            await RunStartupCommandAsync();
        };
        Unloaded += (_, _) => UnhookExternalSettingsReload();
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        NavView.SelectionChanged += NavView_SelectionChanged;
        DropZone.Tapped += DropZone_Tapped;
        DropZone.PointerEntered += (_, _) => SetDropZoneHot(DropZone, DropZoneIcon, true);
        DropZone.PointerExited += (_, _) => SetDropZoneHot(DropZone, DropZoneIcon, false);
        DropZone.DragOver += DropZone_DragOver;
        DropZone.DragLeave += (_, _) => SetDropZoneHot(DropZone, DropZoneIcon, false);
        DropZone.Drop += DropZone_Drop;
        ClearFilesButton.Click += (_, _) => { _convertWorkspace.ClearFiles(); RefreshFiles(); };
        StartButton.Click += async (_, _) => await StartConversionAsync();
        CancelButton.Click += (_, _) => _cts?.Cancel();
        ClearHistoryButton.Click += async (_, _) => await ClearHistoryAsync();
        OpenConvertButton.Click += (_, _) => SelectNavItem("Convert");
        ViewHistoryButton.Click += (_, _) => SelectNavItem("History");
        LibreOfficeBrowseButton.Click += async (_, _) => await BrowseLibreOfficeAsync();
        LibreOfficeDownloadButton.Click += async (_, _) => await InstallLibreOfficeAsync();
        LibreOfficeUninstallButton.Click += async (_, _) => await UninstallLibreOfficeAsync();
        LibreOfficeAdoptButton.Click += async (_, _) => await AdoptLibreOfficeAsync();
        GitHubButton.Click += async (_, _) => await OpenUriAsync(GitHubUrl);
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

    private static string L(string key) => Localization.T(key);

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            ShowPanel(tag);
        }
    }

    private void ShowPanel(string name)
    {
        if (!_dashboardPresentation.TrySelect(name)) return;
        DashboardSection section = _dashboardPresentation.ActiveSection;
        OverviewPanel.Visibility = section == DashboardSection.Overview ? Visibility.Visible : Visibility.Collapsed;
        ConvertPanel.Visibility = section == DashboardSection.Convert ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = section == DashboardSection.History ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = section == DashboardSection.Settings ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = section == DashboardSection.About ? Visibility.Visible : Visibility.Collapsed;
        if (section is DashboardSection.History or DashboardSection.Overview) RefreshHistory();
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        var narrow = ActualWidth < 1000;

        SetTwoPaneLayout(OverviewSidePane, OverviewMainColumn, OverviewSideColumn, 1.4, 0.85, narrow);
        ApplyConvertResponsiveLayout(this, narrow);
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

    private static void ApplyConvertResponsiveLayout(MainPage page, bool narrow)
    {
        page.ConvertMainColumn.Width = new GridLength(1, GridUnitType.Star);
        page.ConvertSideColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(page.ConvertRunCard, narrow ? 0 : 1);
        Grid.SetRow(page.ConvertRunCard, narrow ? 3 : 1);

        SetActiveColumns(page.OfficeCommandGrid, narrow ? 2 : 3);
        SetActiveColumns(page.PdfCommandGrid, narrow ? 2 : 5);
        SetActiveColumns(page.ImageCommandGrid, narrow ? 2 : 4);

        if (narrow)
        {
            SetGridCell(page.BtnWord2Pdf, 0, 0);
            SetGridCell(page.BtnExcel2Pdf, 0, 1);
            SetGridCell(page.BtnPpt2Pdf, 1, 0, 2);
            SetGridCell(page.BtnMd2Pdf, 2, 0);
            SetGridCell(page.BtnMd2Word, 2, 1);

            SetGridCell(page.BtnMergePdf, 0, 0);
            SetGridCell(page.BtnCompressPdf, 0, 1);
            SetGridCell(page.BtnTranslatePdf, 1, 0);
            SetGridCell(page.BtnDecryptPdf, 1, 1);
            SetGridCell(page.BtnSplitPdf, 2, 0, 2);

            SetGridCell(page.BtnImg2Pdf, 0, 0);
            SetGridCell(page.BtnImgMerge, 0, 1);
            SetGridCell(page.BtnImgStitch, 1, 0);
            SetGridCell(page.BtnImgToPng, 1, 1);
            SetGridCell(page.BtnImgToJpg, 2, 0);
            SetGridCell(page.BtnImgToWebp, 2, 1);
            SetGridCell(page.BtnImgToGif, 3, 0);
            SetGridCell(page.BtnImgToHeic, 3, 1);
            return;
        }

        SetGridCell(page.BtnWord2Pdf, 0, 0);
        SetGridCell(page.BtnExcel2Pdf, 0, 1);
        SetGridCell(page.BtnPpt2Pdf, 0, 2);
        SetGridCell(page.BtnMd2Pdf, 1, 0, 2);
        SetGridCell(page.BtnMd2Word, 1, 2);

        SetGridCell(page.BtnMergePdf, 0, 0);
        SetGridCell(page.BtnCompressPdf, 0, 1);
        SetGridCell(page.BtnTranslatePdf, 0, 2);
        SetGridCell(page.BtnDecryptPdf, 0, 3);
        SetGridCell(page.BtnSplitPdf, 0, 4);

        SetGridCell(page.BtnImg2Pdf, 0, 0);
        SetGridCell(page.BtnImgMerge, 0, 1);
        SetGridCell(page.BtnImgStitch, 0, 2);
        SetGridCell(page.BtnImgToPng, 0, 3);
        SetGridCell(page.BtnImgToJpg, 1, 0);
        SetGridCell(page.BtnImgToWebp, 1, 1);
        SetGridCell(page.BtnImgToGif, 1, 2);
        SetGridCell(page.BtnImgToHeic, 1, 3);
    }

    private static void SetActiveColumns(Grid grid, int activeColumns)
    {
        for (int i = 0; i < grid.ColumnDefinitions.Count; i++)
        {
            grid.ColumnDefinitions[i].Width = i < activeColumns
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
        }
    }

    private static void SetGridCell(FrameworkElement element, int row, int column, int columnSpan = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
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

        // 兩欄排列；宣告 ColumnSpan=2 的卡片（暫存保留、LibreOffice）各獨占一整列。
        int row = 0;
        int column = 0;
        foreach (var card in cards)
        {
            bool fullWidth = Grid.GetColumnSpan(card) > 1;
            if (fullWidth && column != 0)
            {
                row++;
                column = 0;
            }

            while (SettingsLayout.RowDefinitions.Count <= row)
            {
                SettingsLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            Grid.SetRow(card, row);
            Grid.SetColumn(card, column);
            if (fullWidth || ++column == 2)
            {
                row++;
                column = 0;
            }
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
        foreach (var button in new[] { BtnWord2Pdf, BtnExcel2Pdf, BtnPpt2Pdf, BtnMd2Pdf, BtnMd2Word, BtnMergePdf, BtnCompressPdf, BtnTranslatePdf, BtnDecryptPdf, BtnSplitPdf, BtnImg2Pdf, BtnImgMerge, BtnImgStitch, BtnImgToPng, BtnImgToJpg, BtnImgToWebp, BtnImgToGif, BtnImgToHeic })
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
        if (_convertWorkspace.IsRunning) return;
        var picker = new FileOpenPicker();
        foreach (var extension in _convertWorkspace.GetPickerExtensions())
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
        e.AcceptedOperation = _convertWorkspace.IsRunning ? DataPackageOperation.None : DataPackageOperation.Copy;
        SetDropZoneHot(DropZone, DropZoneIcon, !_convertWorkspace.IsRunning);
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (_convertWorkspace.IsRunning) return;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        AddFiles(items.OfType<StorageFile>().Select(f => f.Path));
        SetDropZoneHot(DropZone, DropZoneIcon, false);
    }

    private static void SetDropZoneHot(Border dropZone, FrameworkElement dropZoneIcon, bool isHot)
    {
        dropZone.Background = (Brush)Application.Current.Resources[isHot ? SecondaryCardBrushResource : "CardBackgroundFillColorDefaultBrush"];
        dropZone.BorderBrush = (Brush)Application.Current.Resources[isHot ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush"];
        dropZoneIcon.Opacity = isHot ? 1 : 0.85;
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        _convertWorkspace.AddFiles(paths.Where(File.Exists));
        CommandStatusText.Text = _convertWorkspace.SelectedCommand is null
            ? L(ChooseCommandLocalizationKey)
            : string.Format(L("fluent_selected_command"), L(ConvertWorkspaceController.GetCommandLabelKey(_convertWorkspace.SelectedCommand)));
        RefreshFiles();
    }

    private async Task RunStartupCommandAsync()
    {
        if (_startupCommandHandled) return;
        _startupCommandHandled = true;

        var args = ConvertWorkspaceController.SplitLaunchArguments(_startupArguments);
        if (args.Count < 2 || !ConvertWorkspaceController.IsKnownCommand(args[0])) return;

        SelectNavItem("Convert");
        string command = args[0];
        AddFiles(ConvertWorkspaceController.ExpandLaunchArguments(command, args.Skip(1)));
        SelectCommand(command);
        await StartConversionAsync();
    }

    private void SelectCommand(string command)
    {
        if (!_convertWorkspace.TrySelectCommand(command)) return;
        UpdateCommandAvailability();
        CommandStatusText.Text = string.Format(L("fluent_selected_command"), L(ConvertWorkspaceController.GetCommandLabelKey(command)));
        UpdateStartState();
    }

    private void RefreshFiles()
    {
        SelectedFilesCard.Visibility = _convertWorkspace.SelectedFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FileListContainer.Children.Clear();
        if (_convertWorkspace.SelectedFiles.Count == 0)
        {
            FileListContainer.Children.Add(EmptyFileMessage);
        }
        else
        {
            foreach (var file in _convertWorkspace.SelectedFiles)
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
        ConvertRunCard.Visibility = _convertWorkspace.SelectedFiles.Count > 0 || _convertWorkspace.IsRunning
            ? Visibility.Visible
            : Visibility.Collapsed;
        StartButton.Visibility = _convertWorkspace.IsRunning ? Visibility.Collapsed : Visibility.Visible;
        StartButton.IsEnabled = _convertWorkspace.CanStart;
        CancelButton.Visibility = _convertWorkspace.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        UpdateInteractiveState();
    }

    private void UpdateCommandAvailability()
    {
        foreach (var pair in _commandButtons)
        {
            bool compatible = _convertWorkspace.IsCommandCompatible(pair.Key);
            pair.Value.IsEnabled = !_convertWorkspace.IsRunning && compatible;
            pair.Value.Style = compatible && pair.Key.Equals(_convertWorkspace.SelectedCommand, StringComparison.OrdinalIgnoreCase)
                ? (Style)Application.Current.Resources["AccentButtonStyle"]
                : null;
        }

        if (_convertWorkspace.SelectedCommand is null)
            CommandStatusText.Text = L(ChooseCommandLocalizationKey);
    }

    private void UpdateInteractiveState()
    {
        DropZone.AllowDrop = !_convertWorkspace.IsRunning;
        DropZone.Opacity = _convertWorkspace.IsRunning ? 0.55 : 1.0;
        ClearFilesButton.IsEnabled = !_convertWorkspace.IsRunning && _convertWorkspace.SelectedFiles.Count > 0;
        UpdateCommandAvailability();
    }

    private async Task StartConversionAsync()
    {
        string? selectedCommand = _convertWorkspace.SelectedCommand;
        if (selectedCommand is null || !_convertWorkspace.CanStart) return;
        ConvertWorkspaceValidation validation = _convertWorkspace.ValidateSelection();
        if (!validation.IsValid)
        {
            await ShowErrorAsync(DescribeConvertValidation(validation));
            return;
        }
        string error;
        if (!OfficeEnginePreflight.TryValidate(selectedCommand, L, out error))
        {
            await ShowErrorAsync(error);
            return;
        }

        string command = selectedCommand;
        var files = _convertWorkspace.SelectedFiles.ToList();
        Dictionary<string, object>? commandOptions = await PromptCommandOptionsAsync(command);
        if (ConversionExecutionController.RequiresOptions(command) && commandOptions is null) return;
        var execution = new ConversionExecutionController();
        try
        {
            execution.Prepare(command, files, commandOptions);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
            return;
        }

        _convertWorkspace.BeginRun();
        _cts = new CancellationTokenSource();
        UpdateStartState();
        ConversionProgressSection.Visibility = Visibility.Visible;
        SetProgress(0, L("fluent_progress_starting"));

        try
        {
            var callbacks = new ConversionExecutionCallbacks(
                PasswordPrompt: (index, inputPath, isRetry, token) =>
                    DispatcherQueue.EnqueueAsync(() => FluentDialogs.PromptPasswordAsync(XamlRoot, L)),
                SplitPrompt: (index, inputPath, token) =>
                    DispatcherQueue.EnqueueAsync(() => SplitOverlay.ShowForAsync(inputPath)));
            var progress = new Progress<TaskProgressReport>(state => SetProgress(state.Percent, state.Message));
            ConversionExecutionPresentationResult result = await execution.ExecutePreparedAsync(
                callbacks,
                progress,
                _cts.Token);
            await HandleConversionResultAsync(command, files, result);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _convertWorkspace.EndRun();
            UpdateStartState();
            RefreshHistory();
        }
    }

    private async Task<Dictionary<string, object>?> PromptCommandOptionsAsync(string command)
    {
        if (!ConversionExecutionController.RequiresOptions(command)) return null;
        return await FluentDialogs.PromptMarkdownPdfOptionsAsync(XamlRoot, L, App.MainWindow);
    }

    private async Task HandleConversionResultAsync(
        string command,
        IReadOnlyCollection<string> files,
        ConversionExecutionPresentationResult result)
    {
        switch (result.Status)
        {
            case ConversionExecutionPresentationStatus.Succeeded:
                SetProgress(100, L("fluent_progress_completed"));
                ToastHelper.Show(
                    L("fluent_toast_done_title"),
                    string.Format(L("fluent_toast_done_body"), L(ConvertWorkspaceController.GetCommandLabelKey(command)), files.Count));
                _convertWorkspace.ClearFiles();
                RefreshFiles();
                break;
            case ConversionExecutionPresentationStatus.Canceled:
                SetProgress(0, L("fluent_progress_canceled"));
                ToastHelper.Show(
                    L("fluent_toast_canceled_title"),
                    string.Format(L("fluent_toast_canceled_body"), L(ConvertWorkspaceController.GetCommandLabelKey(command))));
                break;
            default:
                SetProgress(0, string.Format(L("fluent_progress_failed"), result.Error));
                ToastHelper.Show(L("fluent_toast_failed_title"), result.Error ?? "");
                await ShowErrorAsync(result.Error ?? "");
                break;
        }
    }

    private void SetProgress(int percent, string message)
    {
        ConversionProgressBar.Value = percent;
        ConversionProgressText.Text = string.IsNullOrWhiteSpace(message) ? $"{percent}%" : $"{message}  {percent}%";
        ActiveJobSection.Visibility = _convertWorkspace.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        ActiveJobText.Text = ConversionProgressText.Text;
    }

    private static string DescribeConvertValidation(ConvertWorkspaceValidation validation)
    {
        string label = string.IsNullOrEmpty(validation.Command)
            ? ""
            : L(ConvertWorkspaceController.GetCommandLabelKey(validation.Command));
        return validation.Kind switch
        {
            ConvertWorkspaceValidationKind.TooFewFiles =>
                string.Format(L("fluent_validate_min_files"), label, validation.MinimumFiles),
            ConvertWorkspaceValidationKind.InvalidExtension =>
                string.Format(L("fluent_validate_bad_ext"), Path.GetFileName(validation.InvalidFile), label),
            _ => L(ChooseCommandLocalizationKey)
        };
    }

    private void LoadSettings()
    {
        _dynamicSettings.Build();
        SyncSettingsToUi();

        OutputDirCombo.SelectionChanged += async (_, _) => await SaveOutputDirAsync();
        EngineCombo.SelectionChanged += (_, _) => SaveSettings();
        LanguageCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingSettings) return;
            SaveSettings();
            ApplyLanguage();
            RefreshFiles();
            RefreshHistory();
        };
        PdfLangCombo.SelectionChanged += (_, _) => SaveSettings();
        CompressionSlider.ValueChanged += (_, _) => { SaveSettings(); UpdateCompressionLabel(CompressionLabel, CompressionSlider); };
        StripFontsToggle.Toggled += (_, _) => SaveSettings();
        MinifyContentToggle.Toggled += (_, _) => SaveSettings();
        QuietModeToggle.Toggled += (_, _) => SaveSettings();
        NotificationToggle.Toggled += (_, _) => SaveSettings();
        ParkedRetentionBox.ValueChanged += OnParkedRetentionChanged;
    }

    private void HookExternalSettingsReload()
    {
        if (_settingsReloadHooked) return;
        SettingsPresentationController.Shared.Reloaded += OnExternalSettingsReloaded;
        _settingsReloadHooked = true;
    }

    private void UnhookExternalSettingsReload()
    {
        if (!_settingsReloadHooked) return;
        SettingsPresentationController.Shared.Reloaded -= OnExternalSettingsReloaded;
        _settingsReloadHooked = false;
    }

    private void OnExternalSettingsReloaded()
    {
        try
        {
            DispatcherQueue?.TryEnqueue(() => SyncSettingsToUi(refreshDynamicLanguageContent: true));
        }
        catch
        {
            // Ignore teardown races after the page's dispatcher has become unavailable.
        }
    }

    private void SyncSettingsToUi(bool refreshDynamicLanguageContent = false)
    {
        int previousLanguageIndex = LanguageCombo.SelectedIndex;
        _loadingSettings = true;
        try
        {
            OutputDirCombo.SelectedIndex = SettingsPresentationController.Shared.OutputDirectory.Kind switch { OutputDirectoryKind.Desktop => 1, OutputDirectoryKind.Downloads => 2, OutputDirectoryKind.Custom => 3, _ => 0 };
            EngineCombo.SelectedIndex = SettingsPresentationController.Shared.OfficeEngine switch { OfficeEnginePreference.Microsoft => 1, OfficeEnginePreference.LibreOffice => 2, _ => 0 };
            LanguageCombo.SelectedIndex = SettingsPresentationController.Shared.Language switch { SimplifiedChineseLanguage => 1, "en-US" => 2, "ja-JP" => 3, "ko-KR" => 4, _ => 0 };
            QuietModeToggle.IsOn = SettingsPresentationController.Shared.QuietMode;
            NotificationToggle.IsOn = SettingsPresentationController.Shared.NotificationsEnabled;
            PdfLangCombo.SelectedIndex = SettingsPresentationController.Shared.TranslationTargetLanguage switch { "en" => 1, SimplifiedChineseLanguage => 2, "ja" => 3, "ko" => 4, _ => 0 };
            CompressionSlider.Minimum = SettingsPresentationController.Shared.MinPdfCompressionLevel;
            CompressionSlider.Maximum = SettingsPresentationController.Shared.MaxPdfCompressionLevel;
            CompressionSlider.Value = SettingsPresentationController.Shared.PdfCompressionImageLevel;
            UpdateCompressionLabel(CompressionLabel, CompressionSlider);
            StripFontsToggle.IsOn = SettingsPresentationController.Shared.PdfCompressionStripFonts;
            MinifyContentToggle.IsOn = SettingsPresentationController.Shared.PdfCompressionMinifyContent;
            ParkedRetentionBox.Minimum = SettingsPresentationController.Shared.MinParkedRetentionDays;
            ParkedRetentionBox.Maximum = SettingsPresentationController.Shared.MaxParkedRetentionDays;
            ParkedRetentionBox.Value = SettingsPresentationController.Shared.ParkedTaskRetentionDays;

            _dynamicSettings.Sync();
        }
        finally
        {
            _loadingSettings = false;
        }
        ApplyLanguage();
        if (refreshDynamicLanguageContent && previousLanguageIndex != LanguageCombo.SelectedIndex)
        {
            RefreshFiles();
            RefreshHistory();
        }
        RefreshLibreOfficeStatus();
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
        // 與 dashboard 的 History 頁同一句話：清單共用，措辭也共用。
        OverviewNoHistoryText.Text = L("history_empty");
        OverviewActivityTitle.Text = L("fluent_activity_summary");
        OverviewTotalLabel.Text = L("fluent_total");
        OverviewOkLabel.Text = L(SuccessLocalizationKey);
        OverviewFailLabel.Text = L(FailedLocalizationKey);
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
        ClearFilesButton.Content = L("convert_clear");
        EmptyFileMessage.Text = L("fluent_no_files");
        CommandTitle.Text = L("fluent_command");
        CommandStatusText.Text = _convertWorkspace.SelectedCommand is null
            ? L(ChooseCommandLocalizationKey)
            : string.Format(L("fluent_selected_command"), L(ConvertWorkspaceController.GetCommandLabelKey(_convertWorkspace.SelectedCommand)));
        OfficeCommandLabel.Text = L("convert_group_office");
        PdfCommandLabel.Text = "PDF";
        ImageCommandLabel.Text = L("fluent_images");
        BtnWord2Pdf.Content = "Word";
        BtnExcel2Pdf.Content = "Excel";
        BtnPpt2Pdf.Content = "PPT";
        BtnMd2Pdf.Content = L("cmd_md_to_pdf");
        BtnMd2Word.Content = L("cmd_md_to_word");
        BtnMergePdf.Content = L("cmd_merge_pdf");
        BtnCompressPdf.Content = L("cmd_compress_pdf");
        BtnTranslatePdf.Content = L("cmd_translate_pdf");
        BtnDecryptPdf.Content = L("cmd_decrypt_pdf");
        BtnSplitPdf.Content = L("cmd_split_pdf");
        BtnImg2Pdf.Content = "PDF";
        BtnImgMerge.Content = L("cmd_merge_img");
        BtnImgStitch.Content = L("cmd_stitch_img");
        BtnImgToPng.Content = L("cmd_img_to_png");
        BtnImgToJpg.Content = L("cmd_img_to_jpg");
        BtnImgToWebp.Content = L("cmd_img_to_webp");
        BtnImgToGif.Content = L("cmd_img_to_gif");
        BtnImgToHeic.Content = L("cmd_img_to_heic");
        RunTitle.Text = L("fluent_run");
        StartButton.Content = L("fluent_start");
        CancelButton.Content = L("dialog_cancel");
        if (!_convertWorkspace.IsRunning) ConversionProgressText.Text = L("fluent_ready");

        HistoryTitle.Text = L("fluent_nav_history");
        HistorySubtitle.Text = L("fluent_history_subtitle");
        ClearHistoryButton.Content = L("convert_clear");
        HistoryTotalLabel.Text = L("fluent_total");
        HistorySuccessLabel.Text = L(SuccessLocalizationKey);
        HistoryFailedLabel.Text = L(FailedLocalizationKey);
        ActiveJobTitle.Text = L("fluent_run");
        ActiveJobText.Text = L("status_converting") + "...";
        EmptyHistoryText.Text = L("history_empty");
        ParkedTasksTitle.Text = L("task_parked_title");
        ParkedTasksDesc.Text = L("task_parked_desc");
        ActiveTasksTitle.Text = L("task_active_title");

        SettingsTitle.Text = L("fluent_nav_settings");
        SettingsSubtitle.Text = L("fluent_settings_subtitle");
        OutputDirTitle.Text = L("fluent_output_dir");
        OutputDirDesc.Text = L("fluent_output_dir_desc");
        OutputDirSourceItem.Content = L("fluent_output_source");
        OutputDirDesktopItem.Content = L("setting_output_desktop");
        OutputDirDownloadsItem.Content = L("setting_output_downloads");
        OutputDirCustomItem.Content = L("fluent_custom");
        OfficeEngineTitle.Text = L("fluent_office_engine");
        OfficeEngineDesc.Text = L("fluent_office_engine_desc");
        EngineAutoItem.Content = L("setting_engine_auto");
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
        PdfCompressImageGroupTitle.Text = L("setting_pdf_compress_group_image");
        PdfCompressOtherGroupTitle.Text = L("setting_pdf_compress_group_other");
        StripFontsTitle.Text = L("fluent_strip_fonts");
        MinifyContentTitle.Text = L("fluent_minify_content");
        ParkedRetentionTitle.Text = L("setting_parked_ttl_title");
        ParkedRetentionDesc.Text = L("setting_parked_ttl_desc");
        LibreOfficeBrowseButton.Content = L("setting_libreoffice_browse");
        LibreOfficeDownloadButton.Content = L("setting_libreoffice_download");
        LibreOfficeUninstallButton.Content = L("setting_libreoffice_uninstall");
        LibreOfficeAdoptButton.Content = L("setting_libreoffice_adopt");
        UpdateCompressionLabel(CompressionLabel, CompressionSlider);

        _dynamicSettings.ApplyLanguage();

        AboutDescription.Text = L("fluent_about_desc");
        GitHubText.Text = L("about_btn_github");
        OpenDataDirText.Text = L("about_btn_open_data_dir");
        GmailText.Text = L("about_btn_gmail");
        PlatformLabel.Text = L("fluent_platform");
        AppModelLabel.Text = L("fluent_app_model");
        RefreshLibreOfficeStatus();
    }

    private static void UpdateCompressionLabel(TextBlock compressionLabel, Slider compressionSlider)
    {
        compressionLabel.Text = L(SettingsPresentationController.Shared.GetPdfCompressionSliderLabelKey((int)compressionSlider.Value));
    }

    private void SaveSettings()
    {
        if (_loadingSettings) return;
        SettingsPresentationController.Shared.OfficeEngine = EngineCombo.SelectedIndex switch { 1 => OfficeEnginePreference.Microsoft, 2 => OfficeEnginePreference.LibreOffice, _ => OfficeEnginePreference.Auto };
        SettingsPresentationController.Shared.Language = LanguageCombo.SelectedIndex switch { 1 => SimplifiedChineseLanguage, 2 => "en-US", 3 => "ja-JP", 4 => "ko-KR", _ => "zh-TW" };
        SettingsPresentationController.Shared.TranslationTargetLanguage = PdfLangCombo.SelectedIndex switch { 1 => "en", 2 => SimplifiedChineseLanguage, 3 => "ja", 4 => "ko", _ => SettingsPresentationController.Shared.DefaultTranslationTargetLanguage };
        SettingsPresentationController.Shared.PdfCompressionImageLevel = (int)CompressionSlider.Value;
        SettingsPresentationController.Shared.PdfCompressionStripFonts = StripFontsToggle.IsOn;
        SettingsPresentationController.Shared.PdfCompressionMinifyContent = MinifyContentToggle.IsOn;
        SettingsPresentationController.Shared.QuietMode = QuietModeToggle.IsOn;
        SettingsPresentationController.Shared.NotificationsEnabled = NotificationToggle.IsOn;
        RefreshLibreOfficeStatus();
    }

    /// <summary>Persists how long a parked conversion is kept, in days (0 = unlimited).
    /// An empty or invalid entry (NumberBox reports NaN) restores the stored value instead of
    /// silently changing the setting, and the saved value is clamped to the control's range.</summary>
    private void OnParkedRetentionChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingSettings || _syncingParkedRetention) return;

        if (double.IsNaN(args.NewValue))
        {
            _syncingParkedRetention = true;
            sender.Value = SettingsPresentationController.Shared.ParkedTaskRetentionDays;
            _syncingParkedRetention = false;
            return;
        }

        int days = Math.Clamp(
            (int)Math.Round(args.NewValue, MidpointRounding.AwayFromZero),
            SettingsPresentationController.Shared.MinParkedRetentionDays,
            SettingsPresentationController.Shared.MaxParkedRetentionDays);
        SettingsPresentationController.Shared.ParkedTaskRetentionDays = days;
        if (Math.Abs(sender.Value - days) > 0.0001)
        {
            _syncingParkedRetention = true;
            sender.Value = days;
            _syncingParkedRetention = false;
        }
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
                OutputDirCombo.SelectedIndex = SettingsPresentationController.Shared.OutputDirectory.Kind switch { OutputDirectoryKind.Desktop => 1, OutputDirectoryKind.Downloads => 2, OutputDirectoryKind.Custom => 3, _ => 0 };
                _loadingSettings = false;
                return;
            }
            SettingsPresentationController.Shared.OutputDirectory = new(OutputDirectoryKind.Custom, folder.Path);
            return;
        }

        SettingsPresentationController.Shared.OutputDirectory = OutputDirCombo.SelectedIndex switch
        {
            1 => new(OutputDirectoryKind.Desktop),
            2 => new(OutputDirectoryKind.Downloads),
            _ => new(OutputDirectoryKind.Source)
        };
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
                logPath = _historyPresentation.GetLogPath(localPath);
            }
            catch
            {
                logPath = _historyPresentation.GetLogPath();
            }

            string? dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(logPath))
                await File.WriteAllTextAsync(logPath, "", _cts?.Token ?? CancellationToken.None);

            // 喚醒檔案總管並使用 /select 自動高亮選中 history.log 檔案
            Process.Start(new ProcessStartInfo
            {
                FileName = Clickra.Core.SystemPaths.Explorer,
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
        string timeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); // skipcq: CS-W1091 — user-facing local timestamp in the report body.
        var (subjectText, bodyText) = Localization.BuildDiagnosticsEmail(versionText, timeText);
        string subject = Uri.EscapeDataString(subjectText);
        string body = Uri.EscapeDataString(bodyText);
        await OpenUriAsync($"https://mail.google.com/mail/?view=cm&fs=1&to=jiangyouchen%40gmail.com&su={subject}&body={body}");
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Clickra",
            Content = message,
            PrimaryButtonText = L("fluent_ok"),
            CloseButtonText = L("dialog_cancel"),
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

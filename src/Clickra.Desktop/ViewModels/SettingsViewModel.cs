using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using Clickra.Core;
using Clickra.Core.Modules.Sys.Settings.Application;
using Clickra.Core.Storage;
using Clickra.Desktop.L10n;

namespace Clickra.Desktop.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private int _themeIndex; // 0 = 淺色 (default per v4 spec), 1 = 深色, 2 = 跟隨系統
    private int _pdfCompressionLevel = 1;
    private int _imageCompressionLevel = 2;
    private int _outputDirKind = 0;
    private string _customOutputDir = "";
    private readonly string _startupLanguage = Strings.Language;

    private static readonly string[] Themes = [ClickraSettings.DesktopThemeLight, ClickraSettings.DesktopThemeDark, ClickraSettings.DesktopThemeSystem];
    private static readonly string[] Languages = ["", "zh-TW", "zh-CN", "en-US", "ja-JP", "ko-KR"];
    private static readonly string[] PdfImageFormats = ["png", "jpg", "webp"];
    private static readonly string[] MarkdownThemes = ["default", "minimal", "academic", ClickraSettings.MarkdownThemeWordTemplate];
    private static readonly string[] MarkdownPapers = ["a4", "letter"];
    private static readonly string[] MarkdownTextSizes = ["small", "standard", "large"];

    public SettingsViewModel()
    {
        LoadSettings();
        _themeIndex = Math.Max(0, Array.IndexOf(Themes, SettingsController.DesktopTheme));
        ApplyTheme(_themeIndex);
    }

    /// <summary>外觀: 0 淺色, 1 深色, 2 跟隨系統. Saved, and applied at start-up.</summary>
    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (_themeIndex == value || value is < 0 or > 2) return;
            _themeIndex = value;
            OnPropertyChanged();
            ApplyTheme(value);
            SettingsController.DesktopTheme = Themes[value];
        }
    }

    // ----- 一般 -----

    /// <summary>介面語言: 0 跟隨系統, then 繁體中文, 简体中文, English, 日本語, 한국어.</summary>
    public int LanguageIndex
    {
        get => Math.Max(0, Array.FindIndex(Languages, l => l.Equals(SettingsController.Language, StringComparison.OrdinalIgnoreCase)));
        set
        {
            if (value < 0 || value >= Languages.Length) return;
            SettingsController.Language = Languages[value];
            OnPropertyChanged();
            OnPropertyChanged(nameof(LanguageNeedsRestart));
            OnPropertyChanged(nameof(CanRestartNow));
        }
    }

    /// <summary>
    /// The chosen language differs from the one this window opened in. Text is read once at start-up, so the
    /// change applies after Clickra reopens (localization plan 3.2).
    /// </summary>
    public bool LanguageNeedsRestart =>
        !string.Equals(Clickra.Core.Localization.NormalizeLanguageCode(SettingsController.Language), _startupLanguage, StringComparison.OrdinalIgnoreCase);

    /// <summary>Set by the window: true while files are being processed, so reopening waits until they finish.</summary>
    public Func<bool>? IsBusy { get; set; }

    public bool CanRestartNow => IsBusy?.Invoke() != true;

    public string RestartHint => Strings.T(CanRestartNow ? "ui_settings_language_restart_hint" : "ui_settings_language_restart_busy");

    /// <summary>Raised by 立即重新開啟; the window starts a new Clickra and closes this one.</summary>
    public event EventHandler? RestartRequested;

    public void RestartNow()
    {
        OnPropertyChanged(nameof(CanRestartNow));
        OnPropertyChanged(nameof(RestartHint));
        if (CanRestartNow) RestartRequested?.Invoke(this, EventArgs.Empty);
    }

    // ----- 行為 -----

    public bool QuietMode
    {
        get => SettingsController.QuietMode;
        set { SettingsController.QuietMode = value; OnPropertyChanged(); }
    }

    public bool NotifyWhenDone
    {
        get => SettingsController.NotificationsEnabled;
        set { SettingsController.NotificationsEnabled = value; OnPropertyChanged(); }
    }

    // ----- 功能預設值 -----

    public int TranslateLanguageIndex
    {
        get => Math.Max(0, Array.FindIndex(ActionOptionsViewModel.TranslateLanguageCodes,
            c => c.Equals(SettingsController.TranslationTargetLanguage, StringComparison.OrdinalIgnoreCase)));
        set
        {
            if (value < 0 || value >= ActionOptionsViewModel.TranslateLanguageCodes.Length) return;
            SettingsController.TranslationTargetLanguage = ActionOptionsViewModel.TranslateLanguageCodes[value];
            OnPropertyChanged();
        }
    }

    public int PdfImageFormatIndex
    {
        get => Math.Max(0, Array.IndexOf(PdfImageFormats, SettingsController.PdfToImageFormat));
        set
        {
            if (value < 0 || value >= PdfImageFormats.Length) return;
            SettingsController.PdfToImageFormat = PdfImageFormats[value];
            OnPropertyChanged();
        }
    }

    public int MarkdownThemeIndex
    {
        get => Math.Max(0, Array.IndexOf(MarkdownThemes, SettingsController.MarkdownTheme));
        set
        {
            if (value < 0 || value >= MarkdownThemes.Length) return;
            // A Word template is chosen on the convert page (選擇檔案), so here it can only be kept, not picked.
            if (value == 3 && string.IsNullOrWhiteSpace(SettingsController.MarkdownTemplatePath)) { OnPropertyChanged(); return; }
            SettingsController.MarkdownTheme = MarkdownThemes[value];
            OnPropertyChanged();
        }
    }

    public int MarkdownPaperIndex
    {
        get => Math.Max(0, Array.IndexOf(MarkdownPapers, SettingsController.MarkdownPaper));
        set
        {
            if (value < 0 || value >= MarkdownPapers.Length) return;
            SettingsController.MarkdownPaper = MarkdownPapers[value];
            OnPropertyChanged();
        }
    }

    /// <summary>文字大小 小／標準／大 maps to Core's small / standard / large.</summary>
    public int MarkdownTextSizeIndex
    {
        get => Math.Max(0, Array.IndexOf(MarkdownTextSizes, SettingsController.MarkdownTextSize));
        set
        {
            if (value < 0 || value >= MarkdownTextSizes.Length) return;
            SettingsController.MarkdownTextSize = MarkdownTextSizes[value];
            OnPropertyChanged();
        }
    }

    public int TranslateEngineIndex
    {
        get => SettingsController.TranslateEngine == Clickra.Core.Storage.ClickraSettings.TranslateEngineCustomAi ? 1 : 0;
        set
        {
            SettingsController.TranslateEngine = value == 1
                ? Clickra.Core.Storage.ClickraSettings.TranslateEngineCustomAi
                : Clickra.Core.Storage.ClickraSettings.TranslateEngineGoogle;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomAi));
        }
    }

    public bool IsCustomAi => TranslateEngineIndex == 1;
    public string AiEndpoint { get => SettingsController.TranslateAiEndpoint; set { SettingsController.TranslateAiEndpoint = value ?? ""; OnPropertyChanged(); } }
    public string AiModel { get => SettingsController.TranslateAiModel; set { SettingsController.TranslateAiModel = value ?? ""; OnPropertyChanged(); } }
    public string AiApiKey { get => SettingsController.TranslateAiApiKey; set { SettingsController.TranslateAiApiKey = value ?? ""; OnPropertyChanged(); } }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S2325", Justification = "Avalonia XAML compiled bindings require instance properties on ViewModel DataContext.")]
    public IReadOnlyList<string> DpiChoices => ActionOptionsViewModel.DpiLabelsPublic;
    public int PdfImageDpiIndex
    {
        get
        {
            int i = Array.IndexOf(ActionOptionsViewModel.DpiValues, SettingsController.PdfToImageDpi);
            return i >= 0 ? i : Array.IndexOf(ActionOptionsViewModel.DpiValues, 150);
        }
        set
        {
            if (value < 0 || value >= ActionOptionsViewModel.DpiValues.Length) return;
            SettingsController.PdfToImageDpi = ActionOptionsViewModel.DpiValues[value];
            OnPropertyChanged();
        }
    }

    public bool PdfImageStitch
    {
        get => SettingsController.PdfToImageStitch;
        set { SettingsController.PdfToImageStitch = value; OnPropertyChanged(); }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S2325", Justification = "Avalonia XAML compiled bindings require instance properties on ViewModel DataContext.")]
    public IReadOnlyList<string> MaxDimChoices => ActionOptionsViewModel.MaxDimLabels;
    public int ImageMaxDimIndex
    {
        get => Math.Max(0, Array.IndexOf(ActionOptionsViewModel.MaxDimValues, SettingsController.ImageCompressionMaxDimension));
        set
        {
            if (value < 0 || value >= ActionOptionsViewModel.MaxDimValues.Length) return;
            SettingsController.ImageCompressionMaxDimension = ActionOptionsViewModel.MaxDimValues[value];
            OnPropertyChanged();
        }
    }

    public int MarkdownCodeIndex
    {
        get => SettingsController.MarkdownCodeTheme == Clickra.Core.Modules.Doc.Shared.Application.MarkdownPdfOptions.CodeLight ? 1 : 0;
        set
        {
            SettingsController.MarkdownCodeTheme = value == 1
                ? Clickra.Core.Modules.Doc.Shared.Application.MarkdownPdfOptions.CodeLight
                : Clickra.Core.Modules.Doc.Shared.Application.MarkdownPdfOptions.CodeDark;
            OnPropertyChanged();
        }
    }

    // ----- 列印：印表機清單在打開設定頁時讀一次；第一項「系統預設印表機」存成空字串 -----
    private IReadOnlyList<string>? _printerChoices;
    public IReadOnlyList<string> PrinterChoices => _printerChoices ??= LoadPrinterChoices();
    private static IReadOnlyList<string> LoadPrinterChoices()
    {
        var list = new List<string> { Strings.T("ui_settings_defaults_system_default_printer") };
        try { list.AddRange(new Clickra.Core.Modules.Pdf.Print.Ports.SystemPrinterDiscoveryService().GetInstalledPrinters()); }
        catch { /* no printing support */ }
        return list;
    }
    public int PrintPrinterIndex
    {
        get
        {
            string saved = SettingsController.PrintPrinter;
            if (saved.Length == 0) return 0;
            for (int idx = 0; idx < PrinterChoices.Count; idx++)
            {
                if (PrinterChoices[idx].Equals(saved, StringComparison.OrdinalIgnoreCase))
                {
                    return idx;
                }
            }
            return 0;
        }
        set
        {
            if (value < 0 || value >= PrinterChoices.Count) return;
            SettingsController.PrintPrinter = value == 0 ? "" : PrinterChoices[value];
            OnPropertyChanged();
        }
    }
    public decimal PrintCopies
    {
        get => SettingsController.PrintCopies;
        set { SettingsController.PrintCopies = (int)Math.Clamp(value, 1, 99); OnPropertyChanged(); }
    }
    public int PrintDuplexIndex
    {
        get => Enum.TryParse(SettingsController.PrintDuplex, out Clickra.Core.Modules.Pdf.Print.Application.PrintDuplexMode d) ? (int)d : 0;
        set { SettingsController.PrintDuplex = ((Clickra.Core.Modules.Pdf.Print.Application.PrintDuplexMode)Math.Clamp(value, 0, 2)).ToString(); OnPropertyChanged(); }
    }
    public int PrintOrientationIndex
    {
        get => Enum.TryParse(SettingsController.PrintOrientation, out Clickra.Core.Modules.Pdf.Print.Application.PrintOrientation o) ? (int)o : 0;
        set { SettingsController.PrintOrientation = ((Clickra.Core.Modules.Pdf.Print.Application.PrintOrientation)Math.Clamp(value, 0, 2)).ToString(); OnPropertyChanged(); }
    }
    public bool PrintFitToPage
    {
        get => SettingsController.PrintFitToPage;
        set { SettingsController.PrintFitToPage = value; OnPropertyChanged(); }
    }
    public bool PrintDarkenText
    {
        get => SettingsController.PrintDarkenText;
        set { SettingsController.PrintDarkenText = value; OnPropertyChanged(); }
    }

    public string CustomOutputDirText => string.IsNullOrWhiteSpace(_customOutputDir) ? "" : _customOutputDir;
    public bool HasCustomOutputDir => _outputDirKind == 3 && !string.IsNullOrWhiteSpace(_customOutputDir);

    /// <summary>行為 › 處理中關閉視窗時: 0 每次詢問, 1 在背景繼續, 2 停止處理 (spec 4.5).</summary>
    public int CloseRunningIndex
    {
        get => (int)CloseWhileRunningSetting.Value;
        set
        {
            if (value is < 0 or > 2 || value == CloseRunningIndex) return;
            CloseWhileRunningSetting.Value = (CloseWhileRunning)value;
            OnPropertyChanged();
        }
    }

    /// <summary>Re-reads settings that other places can change (the close dialog's 「下次不再詢問」).</summary>
    public void Reload() => OnPropertyChanged(nameof(CloseRunningIndex));

    public int PdfCompressionLevel
    {
        get => _pdfCompressionLevel;
        set
        {
            if (_pdfCompressionLevel == value) return;
            _pdfCompressionLevel = value;
            OnPropertyChanged();
            SettingsController.PdfCompressionImageLevel = value;
        }
    }

    public int ImageCompressionLevel
    {
        get => _imageCompressionLevel;
        set
        {
            if (_imageCompressionLevel == value) return;
            _imageCompressionLevel = value;
            OnPropertyChanged();
            SettingsController.ImageCompressionLevel = value;
        }
    }

    public int OutputDirKind
    {
        get => _outputDirKind;
        set
        {
            if (_outputDirKind == value) return;
            _outputDirKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCustomOutputDir));
            SaveOutputDirectory();
        }
    }

    public string CustomOutputDir
    {
        get => _customOutputDir;
        set
        {
            if (_customOutputDir == value) return;
            _customOutputDir = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CustomOutputDirText));
            OnPropertyChanged(nameof(HasCustomOutputDir));
            SaveOutputDirectory();
        }
    }

    public static void OpenLogsFolder()
    {
        try
        {
            string logsDir = Path.Combine(ClickraStorage.GetDataDir(), "logs");
            if (!Directory.Exists(logsDir))
            {
                Directory.CreateDirectory(logsDir);
            }
            OpenPath(logsDir);
        }
        catch
        {
            // Graceful fallback
        }
    }

    public static void SendDiagnosticsEmail()
    {
        try
        {
            string timeText = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            var (subjectText, bodyText) = Localization.BuildDiagnosticsEmail(ExternalOpen.AppVersion, timeText);
            ExternalOpen.Mail(subjectText, bodyText);
        }
        catch
        {
            // Graceful fallback
        }
    }

    private void LoadSettings()
    {
        _pdfCompressionLevel = Math.Clamp(SettingsController.PdfCompressionImageLevel, 0, 2);
        _imageCompressionLevel = Math.Clamp(SettingsController.ImageCompressionLevel, 0, 3);

        var pref = SettingsController.OutputDirectory;
        _outputDirKind = pref.Kind switch
        {
            OutputDirectoryKind.Desktop => 1,
            OutputDirectoryKind.Downloads => 2,
            OutputDirectoryKind.Custom => 3,
            _ => 0
        };
        _customOutputDir = pref.CustomPath;
    }

    private void SaveOutputDirectory()
    {
        var kind = _outputDirKind switch
        {
            1 => OutputDirectoryKind.Desktop,
            2 => OutputDirectoryKind.Downloads,
            3 => OutputDirectoryKind.Custom,
            _ => OutputDirectoryKind.Source
        };
        SettingsController.OutputDirectory = new OutputDirectoryPreference(kind, _customOutputDir);
    }

    /// <summary>
    /// Index order matches the 外觀 dropdown on the Settings page: 0 = 淺色, 1 = 深色, 2 = 跟隨系統.
    /// This is the only place that changes the app theme.
    /// </summary>
    private static void ApplyTheme(int index)
    {
        var variant = index switch
        {
            1 => ThemeVariant.Dark,
            2 => ThemeVariant.Default,
            _ => ThemeVariant.Light
        };
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = variant;
        }
    }

    private static void OpenPath(string path) => ExternalOpen.Path(path);
}

using System;
using System.IO;
using Avalonia;
using Avalonia.Styling;
using Clickra.Core;
using Clickra.Core.Modules.Sys.Settings.Application;
using Clickra.Core.Storage;

namespace Clickra.Desktop.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private int _themeIndex = 1; // Default Light per v4 spec
    private int _pdfCompressionLevel = 1;
    private int _imageCompressionLevel = 2;
    private int _outputDirKind = 0;
    private string _customOutputDir = "";

    public SettingsViewModel()
    {
        LoadSettings();
    }

    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (_themeIndex == value) return;
            _themeIndex = value;
            OnPropertyChanged();
            ApplyTheme(value);
        }
    }

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
            SaveOutputDirectory();
        }
    }

    public void OpenLogsFolder()
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

    public void SendDiagnosticsEmail()
    {
        try
        {
            var version = typeof(SettingsViewModel).Assembly.GetName().Version;
            string versionText = version is null ? "4.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
            string timeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var (subjectText, bodyText) = Localization.BuildDiagnosticsEmail(versionText, timeText);
            string subject = Uri.EscapeDataString(subjectText);
            string body = Uri.EscapeDataString(bodyText);
            OpenPath($"https://mail.google.com/mail/?view=cm&fs=1&to=jiangyouchen%40gmail.com&su={subject}&body={body}");
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

    private static void ApplyTheme(int index)
    {
        var variant = index switch
        {
            0 => ThemeVariant.Default,
            2 => ThemeVariant.Dark,
            _ => ThemeVariant.Light
        };
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = variant;
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true })?.Dispose();
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", $"\"{path}\"")?.Dispose();
            }
            else if (OperatingSystem.IsLinux())
            {
                System.Diagnostics.Process.Start("xdg-open", $"\"{path}\"")?.Dispose();
            }
        }
        catch
        {
            // Best effort
        }
    }
}

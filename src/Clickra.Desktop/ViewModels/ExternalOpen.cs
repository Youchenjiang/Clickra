using System;
using System.Diagnostics;

namespace Clickra.Desktop.ViewModels;

/// <summary>
/// Opens folders, links and the feedback mail outside the app (設定 › 寄送問題回報, 動作目錄 › 建議新功能).
/// Best effort: a machine without a browser or file manager just does nothing.
/// </summary>
public static class ExternalOpen
{
    private const string FeedbackAddress = "jiangyouchen%40gmail.com";

    public static string AppVersion
    {
        get
        {
            Version? version = typeof(ExternalOpen).Assembly.GetName().Version;
            return version is null ? "4.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    /// <summary>A Gmail compose window to the developer with the subject and body filled in.</summary>
    public static void Mail(string subject, string body) =>
        Path($"https://mail.google.com/mail/?view=cm&fs=1&to={FeedbackAddress}&su={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}");

    public static void Path(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true })?.Dispose();
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(Clickra.Core.SystemPaths.MacOpen, $"\"{path}\"")?.Dispose();
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start(Clickra.Core.SystemPaths.LinuxXdgOpen, $"\"{path}\"")?.Dispose();
            }
        }
        catch
        {
            // Best effort
        }
    }
}

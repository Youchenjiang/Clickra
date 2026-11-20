using System;

namespace Clickra.Desktop.ViewModels;

/// <summary>The conversion this window is running, as seen by the history page, the close dialog and the tray.</summary>
public interface IConversionSession
{
    bool IsRunning { get; }
    /// <summary>History task id, known once Core has recorded the task.</summary>
    string? TaskId { get; }
    int FileIndex { get; }
    int FileCount { get; }
    double Percent { get; }
    /// <summary>Files not finished yet (the one in progress included).</summary>
    int RemainingCount { get; }
    /// <summary>Pause only parks between files, so it needs another file after the current one.</summary>
    bool CanPause { get; }
    void RequestPause();
    void Stop();

    /// <summary>Started, moved to another file, or progress changed. Raised on the UI thread.</summary>
    event EventHandler? Changed;
    /// <summary>The run ended (done, failed, paused or stopped). Raised on the UI thread.</summary>
    event EventHandler? Finished;
}

/// <summary>Settings › 行為 › 處理中關閉視窗時 (spec 4.5).</summary>
public enum CloseWhileRunning
{
    Ask,
    Background,
    Stop,
}

public static class CloseWhileRunningSetting
{
    private static readonly string SettingsFilePath = System.IO.Path.Combine(Clickra.Core.Storage.ClickraStorage.GetDataDir(), "desktop_close_running.txt");
    private static CloseWhileRunning? _cached;

    public static CloseWhileRunning Value
    {
        get
        {
            if (_cached.HasValue) return _cached.Value;
            string text = "";
            try
            {
                if (System.IO.File.Exists(SettingsFilePath))
                    text = System.IO.File.ReadAllText(SettingsFilePath).Trim();
            }
            catch { /* best effort */ }

            _cached = text switch
            {
                "background" => CloseWhileRunning.Background,
                "stop" => CloseWhileRunning.Stop,
                _ => CloseWhileRunning.Ask,
            };
            return _cached.Value;
        }
        set
        {
            _cached = value;
            string text = value switch
            {
                CloseWhileRunning.Background => "background",
                CloseWhileRunning.Stop => "stop",
                _ => "ask",
            };
            try
            {
                string dir = System.IO.Path.GetDirectoryName(SettingsFilePath)!;
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(SettingsFilePath, text);
            }
            catch { /* best effort */ }
        }
    }
}

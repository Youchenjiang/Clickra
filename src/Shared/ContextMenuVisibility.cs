using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clickra.Shared;

/// <summary>
/// Which Clickra entries the Explorer context menu shows (spec 4.4: 設定 › 檔案總管右鍵選單).
/// Dependency-free so the NativeAOT shell extension (ClickraShell.dll, loaded by Explorer) can read the
/// same settings.conf the app writes without referencing Core. The keys are registered in
/// ClickraSettings, which aliases the constants below.
/// </summary>
public static class ContextMenuVisibility
{
    /// <summary>Master switch: "false" hides the whole Clickra menu.</summary>
    public const string EnabledKey = "ContextMenuEnabled";

    /// <summary>Commands hidden from the menu, comma-separated (empty = all shown).</summary>
    public const string HiddenKey = "ContextMenuHidden";

    private const char Separator = ',';
    private const string SettingsFileName = "settings.conf";
    private const string DataFolderName = "Clickra";

    /// <summary>The five image-format commands share one switch (轉換圖片格式).</summary>
    public static readonly IReadOnlyList<string> ImageFormatCommands =
        ["img-to-png", "img-to-jpg", "img-to-webp", "img-to-gif", "img-to-heic"];

    public static string JoinHidden(IEnumerable<string> commands) =>
        string.Join(Separator, commands.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase));

    public static HashSet<string> ParseHidden(string? value) =>
        new((value ?? string.Empty).Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>The state read from settings.conf.</summary>
    public sealed record State(bool Enabled, HashSet<string> Hidden)
    {
        public static readonly State Default = new(true, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public bool IsShown(string command) => Enabled && !Hidden.Contains(command);
    }

    private static readonly object CacheLock = new();
    private static string? _cachedPath;
    private static DateTime _cachedWrite;
    private static State _cached = State.Default;

    /// <summary>
    /// Reads the current state for the shell. <paramref name="packageFamilyName"/> is given when the shell
    /// runs from the MSIX package: a packaged app's writes to %LOCALAPPDATA% may be redirected into the
    /// package's LocalCache, so both places are checked and the newer file wins. Cached by file time, so a
    /// right-click costs two file-time checks; a change in Settings applies on the next right-click.
    /// </summary>
    public static State Read(string? packageFamilyName)
    {
        try
        {
            string? path = FindSettingsFile(packageFamilyName);
            if (path is null) return State.Default;
            DateTime write = File.GetLastWriteTimeUtc(path);
            lock (CacheLock)
            {
                if (path == _cachedPath && write == _cachedWrite) return _cached;
            }

            State state = Parse(File.ReadAllLines(path));
            lock (CacheLock)
            {
                _cachedPath = path;
                _cachedWrite = write;
                _cached = state;
            }
            return state;
        }
        catch
        {
            // Never break the menu because of a settings problem: show everything.
            return State.Default;
        }
    }

    public static State Parse(IEnumerable<string> lines)
    {
        bool enabled = true;
        string hidden = string.Empty;
        foreach (string line in lines)
        {
            int index = line.IndexOf('=');
            if (index <= 0) continue;
            string key = line[..index].Trim();
            string value = line[(index + 1)..].Trim();
            if (key.Equals(EnabledKey, StringComparison.OrdinalIgnoreCase))
                enabled = !value.Equals("false", StringComparison.OrdinalIgnoreCase);
            else if (key.Equals(HiddenKey, StringComparison.OrdinalIgnoreCase))
                hidden = value;
        }
        return new State(enabled, ParseHidden(hidden));
    }

    private static string? FindSettingsFile(string? packageFamilyName)
    {
        string? overrideDir = Environment.GetEnvironmentVariable("CLICKRA_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            string overridden = Path.Combine(overrideDir, SettingsFileName);
            return File.Exists(overridden) ? overridden : null;
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new List<string> { Path.Combine(local, DataFolderName, SettingsFileName) };
        if (!string.IsNullOrWhiteSpace(packageFamilyName))
        {
            candidates.Add(Path.Combine(local, "Packages", packageFamilyName, "LocalCache", "Local", DataFolderName, SettingsFileName));
        }

        return candidates
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}

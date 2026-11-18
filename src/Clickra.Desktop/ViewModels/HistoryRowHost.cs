using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Clickra.Desktop.ViewModels;

/// <summary>A finished task to run again: the same action on the given files (spec 4.2 再轉一次).</summary>
public sealed class RerunRequestEventArgs(string command, IReadOnlyList<string> files) : EventArgs
{
    public string Command { get; } = command;
    public IReadOnlyList<string> Files { get; } = files;
}

/// <summary>
/// What the history page and 最近處理 share: the two row buttons and the missing-file notices (spec 4.2).
/// Files are checked only when a button is pressed; a notice stays under that row until the user acts,
/// presses another row, or presses the same button again.
/// </summary>
public abstract class HistoryRowHostBase : ViewModelBase, IHistoryRowHost
{
    private enum NoticeSource { None, Folder, Rerun }

    private HistoryRowViewModel? _noticeRow;
    private NoticeSource _noticeSource;

    /// <summary>Raised when 再轉一次 found every file (or the user chose to skip the missing ones).</summary>
    public event EventHandler<RerunRequestEventArgs>? RerunRequested;

    /// <summary>Set by the window: true while a conversion is running, so a second one is not started.</summary>
    public Func<bool>? IsBusy { get; set; }

    public abstract void ToggleExpand(HistoryRowViewModel row);
    public virtual void Resume(HistoryRowViewModel row) { }
    public virtual void Stop(HistoryRowViewModel row) { }
    public virtual void Pause(HistoryRowViewModel row) { }
    public virtual bool CanPauseRow(HistoryRowViewModel row) => false;
    public virtual bool CanStopRow(HistoryRowViewModel row) => false;

    /// <summary>Clears the notice of any other row: pressing a row or a button elsewhere dismisses it.</summary>
    protected void DismissNoticeExcept(HistoryRowViewModel? row)
    {
        if (_noticeRow is not null && !ReferenceEquals(_noticeRow, row))
        {
            _noticeRow.ClearNotice();
            _noticeRow = null;
            _noticeSource = NoticeSource.None;
        }
    }

    /// <summary>Pressing the same button again closes its notice. Returns true when it did.</summary>
    private bool ToggledOff(HistoryRowViewModel row, NoticeSource source)
    {
        DismissNoticeExcept(row);
        if (ReferenceEquals(_noticeRow, row) && _noticeSource == source && row.HasNotice)
        {
            row.ClearNotice();
            _noticeRow = null;
            _noticeSource = NoticeSource.None;
            return true;
        }
        row.ClearNotice();
        return false;
    }

    private void Show(HistoryRowViewModel row, NoticeSource source, string text, string actionText = "", Action? action = null)
    {
        _noticeRow = row;
        _noticeSource = source;
        row.ShowNotice(text, actionText, action);
    }

    // ----- 開啟所在資料夾／開啟來源資料夾 -----

    public void OpenFolder(HistoryRowViewModel row)
    {
        if (ToggledOff(row, NoticeSource.Folder)) return;

        // Success: the first output. Failure or canceled: the first source file.
        string target = row.IsSuccess
            ? FirstPath(row.OutputPath) ?? row.InputPaths.FirstOrDefault() ?? ""
            : row.InputPaths.FirstOrDefault() ?? FirstPath(row.OutputPath) ?? "";
        if (target.Length == 0) return;

        if (File.Exists(target))
        {
            RevealFile(target);
            return;
        }
        if (Directory.Exists(target))
        {
            OpenPath(target);
            return;
        }

        string? root = Path.GetPathRoot(target);
        if (!string.IsNullOrEmpty(root) && !Directory.Exists(root))
        {
            Show(row, NoticeSource.Folder, MissingDriveText(root));
            return;
        }

        string? folder = Path.GetDirectoryName(target);
        if (folder is not null && Directory.Exists(folder))
        {
            OpenPath(folder);
            Show(row, NoticeSource.Folder, $"找不到 {Path.GetFileName(target)}，可能已被移動或刪除，已開啟原本的資料夾");
            return;
        }

        string? nearest = NearestExistingParent(folder);
        if (nearest is not null) OpenPath(nearest);
        Show(row, NoticeSource.Folder, nearest is null
            ? $"找不到資料夾 {folder}"
            : $"找不到資料夾 {Path.GetFileName(folder?.TrimEnd('\\', '/'))}，已開啟上一層的 {nearest}");
    }

    // ----- 再轉一次 -----

    public void Rerun(HistoryRowViewModel row)
    {
        if (ToggledOff(row, NoticeSource.Rerun)) return;

        if (IsBusy?.Invoke() == true)
        {
            Show(row, NoticeSource.Rerun, "正在處理其他檔案，做完後再試一次");
            return;
        }

        string command = row.Item.Entry.Command ?? "";
        IReadOnlyList<string> files = row.InputPaths;
        if (command.Length == 0 || files.Count == 0) return;

        List<string> missing = files.Where(f => !File.Exists(f)).ToList();
        if (missing.Count == 0)
        {
            RerunRequested?.Invoke(this, new RerunRequestEventArgs(command, files));
            return;
        }

        // Everything missing on one drive that is not plugged in: say that, not "file not found".
        string? root = Path.GetPathRoot(missing[0]);
        bool driveMissing = !string.IsNullOrEmpty(root) && !Directory.Exists(root) &&
                            missing.All(f => string.Equals(Path.GetPathRoot(f), root, StringComparison.OrdinalIgnoreCase));

        if (missing.Count == files.Count)
        {
            string text = driveMissing
                ? MissingDriveText(root!)
                : files.Count == 1
                    ? $"找不到 {Path.GetFileName(files[0])}，可能已被移動或刪除"
                    : $"{files.Count} 個檔案都找不到，可能已被移動或刪除";
            Show(row, NoticeSource.Rerun, text);
            return;
        }

        List<string> present = files.Where(File.Exists).ToList();
        string names = string.Join("、", missing.Take(2).Select(Path.GetFileName)) + (missing.Count > 2 ? " 等" : "");
        Show(row, NoticeSource.Rerun,
            $"{files.Count} 個檔案中有 {missing.Count} 個找不到：{names}",
            $"只處理其他 {present.Count} 個",
            () =>
            {
                _noticeRow = null;
                _noticeSource = NoticeSource.None;
                RerunRequested?.Invoke(this, new RerunRequestEventArgs(command, present));
            });
    }

    private static string MissingDriveText(string root) =>
        $"找不到 {root.TrimEnd('\\', '/')}，可能是隨身碟或網路磁碟沒有接上";

    private static string? FirstPath(string joined) =>
        joined.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    private static string? NearestExistingParent(string? folder)
    {
        string? current = folder;
        while (!string.IsNullOrEmpty(current))
        {
            current = Path.GetDirectoryName(current);
            if (current is not null && Directory.Exists(current)) return current;
        }
        return null;
    }

    /// <summary>Opens the folder with the file selected (Windows); elsewhere just opens the folder.</summary>
    protected static void RevealFile(string file)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(Clickra.Core.SystemPaths.Explorer, $"/select,\"{file}\"") { UseShellExecute = true })?.Dispose();
                return;
            }
            if (OperatingSystem.IsMacOS())
            {
                Process.Start(Clickra.Core.SystemPaths.MacOpen, $"-R \"{file}\"")?.Dispose();
                return;
            }
        }
        catch
        {
            // fall through to opening the folder
        }
        if (Path.GetDirectoryName(file) is { } dir) OpenPath(dir);
    }

    protected static void OpenPath(string path) => ExternalOpen.Path(path);
}

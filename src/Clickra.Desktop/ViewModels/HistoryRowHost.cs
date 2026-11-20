using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Clickra.Desktop.L10n;

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
    private enum NoticeSource { None, Folder, Rerun, Resume }
    private const string CountParam = "count";

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
            Show(row, NoticeSource.Folder, Strings.F("ui_history_missing_opened_folder", ("name", Path.GetFileName(target))));
            return;
        }

        string? nearest = NearestExistingParent(folder);
        if (nearest is not null) OpenPath(nearest);
        string folderName = Path.GetFileName(folder?.TrimEnd('\\', '/')) ?? folder ?? "";
        if (nearest is null)
        {
            Show(row, NoticeSource.Folder, Strings.F("ui_history_missing_folder", ("name", folderName)));
        }
        else
        {
            string parent = Path.GetFileName(nearest.TrimEnd('\\', '/'));
            string parentName = string.IsNullOrEmpty(parent) ? nearest : parent;
            Show(row, NoticeSource.Folder, Strings.F("ui_history_missing_folder_opened_parent",
                ("name", folderName),
                ("parent", parentName)));
        }
    }

    // ----- 再轉一次 -----

    public void Rerun(HistoryRowViewModel row)
    {
        if (ToggledOff(row, NoticeSource.Rerun)) return;

        if (IsBusy?.Invoke() == true)
        {
            Show(row, NoticeSource.Rerun, Strings.T("ui_history_missing_busy_with_other_files_try"));
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
            string text;
            if (driveMissing)
            {
                text = MissingDriveText(root!);
            }
            else if (files.Count == 1)
            {
                text = Strings.F("ui_history_missing_file", ("name", Path.GetFileName(files[0])));
            }
            else
            {
                text = Strings.F("ui_history_missing_all", (CountParam, files.Count));
            }
            Show(row, NoticeSource.Rerun, text);
            return;
        }

        List<string> present = files.Where(File.Exists).ToList();
        string names = Strings.ShortList(missing.Select(f => Path.GetFileName(f)).ToList());
        Show(row, NoticeSource.Rerun,
            Strings.F("ui_history_missing_some", ("total", files.Count), (CountParam, missing.Count), ("names", names)),
            Strings.F("ui_history_missing_process_others", (CountParam, present.Count)),
            () =>
            {
                _noticeRow = null;
                _noticeSource = NoticeSource.None;
                if (IsBusy?.Invoke() == true)
                {
                    Show(row, NoticeSource.Rerun, Strings.T("ui_history_missing_busy_with_other_files_try"));
                    return;
                }
                RerunRequested?.Invoke(this, new RerunRequestEventArgs(command, present));
            });
    }

    // ----- ▶ 繼續 -----

    /// <summary>
    /// ▶ 繼續 checks the files that are still to do before the task leaves 待繼續 (spec 4.2, 2026-11-20).
    /// All there: <paramref name="resume"/> runs with <see langword="null"/> (keep the task's own list).
    /// Some missing: the row offers 只處理其他 N 個, which resumes with the missing ones dropped.
    /// All missing: the row offers 移除這個工作. Nothing is claimed until the user chooses.
    /// </summary>
    protected void CheckFilesBeforeResume(
        HistoryRowViewModel row,
        Action<IReadOnlyList<string>?> resume,
        Action remove)
    {
        if (ToggledOff(row, NoticeSource.Resume)) return;

        IReadOnlyList<string> files = row.InputPaths;
        int start = Math.Clamp(row.Item.Entry.CurrentIndex, 0, files.Count);
        List<string> remaining = files.Skip(start).ToList();
        List<string> missing = remaining.Where(f => !File.Exists(f)).ToList();
        if (missing.Count == 0)
        {
            resume(null);
            return;
        }

        if (missing.Count == remaining.Count)
        {
            string? root = Path.GetPathRoot(missing[0]);
            bool driveMissing = !string.IsNullOrEmpty(root) && !Directory.Exists(root) &&
                                missing.All(f => string.Equals(Path.GetPathRoot(f), root, StringComparison.OrdinalIgnoreCase));
            string text;
            if (driveMissing)
            {
                text = MissingDriveText(root!);
            }
            else if (remaining.Count == 1)
            {
                text = Strings.F("ui_history_missing_file", ("name", Path.GetFileName(remaining[0])));
            }
            else
            {
                text = Strings.F("ui_history_resume_missing_all", (CountParam, remaining.Count));
            }
            Show(row, NoticeSource.Resume, text, Strings.T("ui_history_resume_remove_task"), () =>
            {
                _noticeRow = null;
                _noticeSource = NoticeSource.None;
                remove();
            });
            return;
        }

        List<string> keep = files.Take(start).Concat(remaining.Where(File.Exists)).ToList();
        int present = remaining.Count - missing.Count;
        string names = Strings.ShortList(missing.Select(f => Path.GetFileName(f)).ToList());
        Show(row, NoticeSource.Resume,
            Strings.F("ui_history_resume_missing_some", ("total", remaining.Count), (CountParam, missing.Count), ("names", names)),
            Strings.F("ui_history_missing_process_others", (CountParam, present)),
            () =>
            {
                _noticeRow = null;
                _noticeSource = NoticeSource.None;
                if (IsBusy?.Invoke() == true)
                {
                    Show(row, NoticeSource.Resume, Strings.T("ui_history_missing_busy_with_other_files_try"));
                    return;
                }
                resume(keep);
            });
    }

    private static string MissingDriveText(string root) =>
        Strings.F("ui_history_missing_drive", ("drive", root.TrimEnd('\\', '/')));

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

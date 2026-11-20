using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Clickra.Core.Modules.File.BatchRename.Application;
using Clickra.Core.Modules.File.BatchRename.Domain;
using Clickra.Desktop.L10n;

namespace Clickra.Desktop.ViewModels;

/// <summary>One line of the rename preview: 「原檔名 → 新檔名」, or why it cannot be renamed.</summary>
public sealed class RenamePreviewRow(RenameItem item)
{
    public string OldName { get; } = item.OriginalName;
    public string NewName { get; } = item.NewName;
    public bool IsChanged => item.Status == RenameItemStatus.Valid;
    public bool IsUnchanged => item.Status == RenameItemStatus.Unchanged;
    public bool IsProblem => !IsChanged && !IsUnchanged;
    public string Problem { get; } = item.Status switch
    {
        RenameItemStatus.ConflictExistingFile => Strings.T("ui_rename_a_file_with_this_name"),
        RenameItemStatus.ConflictDuplicateTarget => Strings.T("ui_rename_same_name_as_another_file"),
        RenameItemStatus.InvalidFilename => Strings.T("ui_rename_contains_characters_that_arent_allowed"),
        RenameItemStatus.MissingSourceFile => Strings.T("ui_rename_cant_find_this_file"),
        _ => "",
    };
}

/// <summary>
/// 批次重新命名的工作區 (spec 4.6): rules on the left, live preview on the right. The plan shown in the
/// preview is exactly the one that runs, so what the user sees is what happens.
/// </summary>
public sealed class RenameWorkspaceViewModel : ViewModelBase
{
    private IReadOnlyList<string> _files = Array.Empty<string>();
    private BatchRenamePlan? _plan;

    private bool _useReplace = true, _useNumbering, _useDate, _useAffix;
    private string _findText = "", _replaceWith = "", _startNumber = "01", _prefix = "", _suffix = "", _customExtension = "";
    private int _numberPosition = 1, _dateSource, _datePosition, _caseIndex, _extensionIndex;
    private bool _useRegex, _matchCase;

    public ObservableCollection<RenamePreviewRow> Preview { get; } = [];

    public void SetFiles(IEnumerable<string> files)
    {
        _files = BatchRenameUseCase.ResolveTargetFiles(files);
        Update();
    }

    // ----- rules (each checkbox opens its fields) -----
    public bool UseReplace { get => _useReplace; set => Set(value, ref _useReplace); }
    public string FindText { get => _findText; set => Set(value ?? "", ref _findText); }
    public string ReplaceWith { get => _replaceWith; set => Set(value ?? "", ref _replaceWith); }

    public bool UseNumbering { get => _useNumbering; set => Set(value, ref _useNumbering); }
    /// <summary>The digit count follows how the start is written: 「01」 gives two digits.</summary>
    public string StartNumber { get => _startNumber; set => Set(value ?? "", ref _startNumber); }
    /// <summary>0 = 檔名前面, 1 = 檔名後面.</summary>
    public int NumberPosition { get => _numberPosition; set => Set(value, ref _numberPosition); }

    public bool UseDate { get => _useDate; set => Set(value, ref _useDate); }
    /// <summary>0 = 修改日期, 1 = 建立日期.</summary>
    public int DateSource { get => _dateSource; set => Set(value, ref _dateSource); }
    public int DatePosition { get => _datePosition; set => Set(value, ref _datePosition); }

    public bool UseAffix { get => _useAffix; set => Set(value, ref _useAffix); }
    public string Prefix { get => _prefix; set => Set(value ?? "", ref _prefix); }
    public string Suffix { get => _suffix; set => Set(value ?? "", ref _suffix); }

    /// <summary>0 = 不變, 1 = 全部小寫, 2 = 全部大寫, 3 = 每個字首大寫.</summary>
    public int CaseIndex { get => _caseIndex; set => Set(value, ref _caseIndex); }
    /// <summary>0 = 保持原樣, 1 = 小寫, 2 = 大寫, 3 = 改成….</summary>
    public int ExtensionIndex { get => _extensionIndex; set { Set(value, ref _extensionIndex); OnPropertyChanged(nameof(IsCustomExtension)); } }
    public bool IsCustomExtension => _extensionIndex == 3;
    public string CustomExtension { get => _customExtension; set => Set(value ?? "", ref _customExtension); }
    public bool UseRegex { get => _useRegex; set => Set(value, ref _useRegex); }
    public bool MatchCase { get => _matchCase; set => Set(value, ref _matchCase); }

    // ----- result of the preview -----
    public int ChangeCount => _plan?.ValidCount ?? 0;
    public int ProblemCount => (_plan?.ConflictCount ?? 0) + (_plan?.InvalidCount ?? 0);
    public bool CanRename => _plan?.CanExecute == true;
    public bool HasProblems => ProblemCount > 0;

    public string PreviewSummary
    {
        get
        {
            if (_plan is null || _files.Count == 0) return "";
            if (ProblemCount > 0) return Strings.F("ui_rename_problems", ("count", ProblemCount));
            if (ChangeCount == 0) return Strings.T("ui_rename_no_names_have_changed_yet");
            if (ChangeCount == _files.Count) return Strings.F("ui_rename_all_change", ("count", ChangeCount));
            return Strings.F("ui_rename_some_change", ("count", ChangeCount), ("unchanged", _files.Count - ChangeCount));
        }
    }

    public string RenameButtonText => ChangeCount > 0 ? Strings.Count("ui_rename_button", ChangeCount) : Strings.T("ui_catalog_rename");

    /// <summary>Options and the exact plan for Core, so the run matches the preview.</summary>
    public Dictionary<string, object> BuildExecutionOptions()
    {
        BatchRenameOptions options = BuildOptions();
        BatchRenamePlan plan = BatchRenameUseCase.Plan(_files, options);
        return new Dictionary<string, object> { ["options"] = options, ["plan"] = plan };
    }

    private NumberingOptions? BuildNumberingOptions()
    {
        if (!_useNumbering) return null;
        string digits = new(_startNumber.Where(char.IsDigit).ToArray());
        int start = int.TryParse(digits, out int n) ? n : 1;
        return new NumberingOptions(
            StartIndex: start,
            PaddingDigits: Math.Max(1, digits.Length),
            Position: _numberPosition == 0 ? InsertPosition.Prefix : InsertPosition.Suffix);
    }

    private TimestampOptions? BuildTimestampOptions()
    {
        if (!_useDate) return null;
        var dateSource = _dateSource == 1 ? TimestampSource.CreatedTime : TimestampSource.ModifiedTime;
        var datePosition = _datePosition == 0 ? InsertPosition.Prefix : InsertPosition.Suffix;
        return new TimestampOptions(Source: dateSource, Position: datePosition);
    }

    private BatchRenameOptions BuildOptions()
    {
        ReplaceOptions? replace = _useReplace && _findText.Length > 0
            ? new ReplaceOptions(_findText, _replaceWith, IgnoreCase: !_matchCase, UseRegex: _useRegex)
            : null;

        return new BatchRenameOptions(
            Prefix: _useAffix && _prefix.Length > 0 ? _prefix : null,
            Suffix: _useAffix && _suffix.Length > 0 ? _suffix : null,
            Numbering: BuildNumberingOptions(),
            Replace: replace,
            Timestamp: BuildTimestampOptions(),
            CaseTransform: _caseIndex switch
            {
                1 => CaseTransformMode.LowerCase,
                2 => CaseTransformMode.UpperCase,
                3 => CaseTransformMode.TitleCase,
                _ => CaseTransformMode.None,
            },
            ExtensionMode: _extensionIndex switch
            {
                1 => ExtensionTransformMode.LowerCase,
                2 => ExtensionTransformMode.UpperCase,
                3 when _customExtension.Trim().TrimStart('.').Length > 0 => ExtensionTransformMode.Custom,
                _ => ExtensionTransformMode.KeepOriginal,
            },
            CustomExtension: _customExtension.Trim().TrimStart('.'));
    }

    private void Update()
    {
        try
        {
            _plan = _files.Count == 0 ? null : BatchRenameUseCase.Plan(_files, BuildOptions());
        }
        catch
        {
            _plan = null; // e.g. an unfinished regular expression
        }
        Preview.Clear();
        if (_plan is not null)
        {
            foreach (RenameItem item in _plan.Items) Preview.Add(new RenamePreviewRow(item));
        }
        OnPropertyChanged(nameof(ChangeCount));
        OnPropertyChanged(nameof(ProblemCount));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(CanRename));
        OnPropertyChanged(nameof(PreviewSummary));
        OnPropertyChanged(nameof(RenameButtonText));
    }

    private void Set<T>(T value, ref T field, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
        Update();
    }
}

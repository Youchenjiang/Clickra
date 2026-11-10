using System;
using System.Collections.Generic;
using System.Linq;

namespace Clickra.Core.Modules.File.BatchRename.Domain;

/// <summary>Status of an individual file within a rename plan.</summary>
public enum RenameItemStatus
{
    Valid,
    Unchanged,
    ConflictExistingFile,
    ConflictDuplicateTarget,
    InvalidFilename,
    MissingSourceFile
}

/// <summary>Represents a single file planned for renaming.</summary>
public sealed record RenameItem(
    string OriginalPath,
    string OriginalName,
    string NewName,
    string NewPath,
    RenameItemStatus Status,
    string? StatusMessage = null);

/// <summary>An evaluated rename plan containing validated items and safety metrics.</summary>
public sealed record BatchRenamePlan(IReadOnlyList<RenameItem> Items)
{
    public int TotalCount => Items.Count;
    public int ValidCount => Items.Count(i => i.Status == RenameItemStatus.Valid);
    public int UnchangedCount => Items.Count(i => i.Status == RenameItemStatus.Unchanged);
    public int ConflictCount => Items.Count(i => i.Status is RenameItemStatus.ConflictExistingFile
                                               or RenameItemStatus.ConflictDuplicateTarget);
    public int InvalidCount => Items.Count(i => i.Status is RenameItemStatus.InvalidFilename
                                              or RenameItemStatus.MissingSourceFile);

    public bool CanExecute => ValidCount > 0 && ConflictCount == 0 && InvalidCount == 0;

    /// <summary>
    /// Indicates whether any target path collides with another item's source path,
    /// requiring two-phase intermediate renaming to prevent overwriting.
    /// </summary>
    public bool RequiresTwoPhaseSwap
    {
        get
        {
            var sourceSet = new HashSet<string>(
                Items.Where(i => i.Status == RenameItemStatus.Valid).Select(i => i.OriginalPath),
                StringComparer.OrdinalIgnoreCase);

            return Items.Any(i => i.Status == RenameItemStatus.Valid
                               && sourceSet.Contains(i.NewPath)
                               && !string.Equals(i.OriginalPath, i.NewPath, StringComparison.OrdinalIgnoreCase));
        }
    }
}

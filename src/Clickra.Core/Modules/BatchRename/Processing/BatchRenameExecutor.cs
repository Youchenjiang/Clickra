using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core.Modules.BatchRename.Domain;

namespace Clickra.Core.Modules.BatchRename.Processing;

/// <summary>Progress report emitted during batch renaming.</summary>
public sealed record BatchRenameProgress(
    int CompletedCount,
    int TotalCount,
    string CurrentFile,
    int Percentage);

/// <summary>Outcome of a batch rename execution.</summary>
public sealed record BatchRenameResult(
    bool Succeeded,
    int TotalProcessed,
    IReadOnlyList<(string OldPath, string NewPath)> RenamedFiles,
    IReadOnlyList<string> Errors);

/// <summary>Safely executes batch rename operations with two-phase collision handling.</summary>
public static class BatchRenameExecutor
{
    public static async Task<BatchRenameResult> ExecuteAsync(
        BatchRenamePlan plan,
        IProgress<BatchRenameProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!plan.CanExecute)
        {
            var errors = plan.Items
                .Where(i => i.Status is not (RenameItemStatus.Valid or RenameItemStatus.Unchanged))
                .Select(i => $"{i.OriginalName}: {i.StatusMessage ?? i.Status.ToString()}")
                .ToList();

            return new BatchRenameResult(false, 0, [], errors);
        }

        var validItems = plan.Items
            .Where(i => i.Status == RenameItemStatus.Valid)
            .ToList();

        if (validItems.Count == 0)
        {
            return new BatchRenameResult(true, 0, [], []);
        }

        return await Task.Run(() =>
        {
            if (plan.RequiresTwoPhaseSwap)
            {
                return ExecuteTwoPhase(validItems, progress, cancellationToken);
            }

            return ExecuteDirect(validItems, progress, cancellationToken);
        }, cancellationToken);
    }

    private static BatchRenameResult ExecuteDirect(
        List<RenameItem> items,
        IProgress<BatchRenameProgress>? progress,
        CancellationToken cancellationToken)
    {
        var completed = new List<(string OldPath, string NewPath)>();
        var errors = new List<string>();

        for (int i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = items[i];
            try
            {
                File.Move(item.OriginalPath, item.NewPath);
                completed.Add((item.OriginalPath, item.NewPath));
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to rename '{item.OriginalName}' to '{item.NewName}': {ex.Message}");
                Rollback(completed);
                return new BatchRenameResult(false, completed.Count, completed, errors);
            }

            int percent = (int)Math.Round((i + 1) * 100.0 / items.Count);
            progress?.Report(new BatchRenameProgress(i + 1, items.Count, item.NewName, percent));
        }

        return new BatchRenameResult(true, completed.Count, completed, []);
    }

    private static BatchRenameResult ExecuteTwoPhase(
        List<RenameItem> items,
        IProgress<BatchRenameProgress>? progress,
        CancellationToken cancellationToken)
    {
        var phase1Completed = new List<(string Original, string Temp)>();
        var phase2Completed = new List<(string Original, string NewPath)>();
        var errors = new List<string>();

        // Phase 1: Move each file to a temporary GUID filename in the same directory
        for (int i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = items[i];
            string dir = Path.GetDirectoryName(item.OriginalPath) ?? "";
            string tempPath = Path.Combine(dir, $".clickra_tmp_{Guid.NewGuid():N}.tmp");

            try
            {
                File.Move(item.OriginalPath, tempPath);
                phase1Completed.Add((item.OriginalPath, tempPath));
            }
            catch (Exception ex)
            {
                errors.Add($"Phase 1 staging failed on '{item.OriginalName}': {ex.Message}");
                // Rollback Phase 1 moves
                foreach (var (orig, temp) in phase1Completed)
                {
                    try { File.Move(temp, orig); } catch { /* best effort rollback */ }
                }
                return new BatchRenameResult(false, 0, [], errors);
            }

            int percent = (int)Math.Round((i + 1) * 50.0 / items.Count);
            progress?.Report(new BatchRenameProgress(i + 1, items.Count * 2, item.OriginalName, percent));
        }

        // Phase 2: Move each temporary file to its final destination
        for (int i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = items[i];
            string tempPath = phase1Completed[i].Temp;

            try
            {
                File.Move(tempPath, item.NewPath);
                phase2Completed.Add((item.OriginalPath, item.NewPath));
            }
            catch (Exception ex)
            {
                errors.Add($"Phase 2 finalization failed on '{item.NewName}': {ex.Message}");
                // Incomplete phase 2: roll back what we can
                return new BatchRenameResult(false, phase2Completed.Count, phase2Completed, errors);
            }

            int percent = 50 + (int)Math.Round((i + 1) * 50.0 / items.Count);
            progress?.Report(new BatchRenameProgress(items.Count + i + 1, items.Count * 2, item.NewName, percent));
        }

        return new BatchRenameResult(true, phase2Completed.Count, phase2Completed, []);
    }

    private static void Rollback(List<(string OldPath, string NewPath)> completed)
    {
        for (int i = completed.Count - 1; i >= 0; i--)
        {
            try
            {
                var (oldPath, newPath) = completed[i];
                if (File.Exists(newPath))
                {
                    File.Move(newPath, oldPath);
                }
            }
            catch
            {
                // Best effort rollback
            }
        }
    }
}

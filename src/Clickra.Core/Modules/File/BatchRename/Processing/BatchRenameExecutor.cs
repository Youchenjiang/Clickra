using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core.Modules.File.BatchRename.Domain;

namespace Clickra.Core.Modules.File.BatchRename.Processing;

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
            if (cancellationToken.IsCancellationRequested)
            {
                Rollback(completed);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var item = items[i];
            try
            {
                System.IO.File.Move(item.OriginalPath, item.NewPath);
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
        var (phase1Success, phase1Completed, phase1Errors) = StageTwoPhaseFiles(items, progress, cancellationToken);
        if (!phase1Success)
        {
            return new BatchRenameResult(false, 0, [], phase1Errors);
        }

        var (phase2Success, phase2Completed, phase2Errors) = FinalizeTwoPhaseFiles(items, phase1Completed, progress, cancellationToken);
        if (!phase2Success)
        {
            return new BatchRenameResult(false, phase2Completed.Count, phase2Completed, phase2Errors);
        }

        return new BatchRenameResult(true, phase2Completed.Count, phase2Completed, []);
    }

    private static (bool Success, List<(string Original, string Temp)> Completed, List<string> Errors) StageTwoPhaseFiles(
        List<RenameItem> items,
        IProgress<BatchRenameProgress>? progress,
        CancellationToken cancellationToken)
    {
        var phase1Completed = new List<(string Original, string Temp)>();
        var errors = new List<string>();

        for (int i = 0; i < items.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                RollbackStagedFiles(phase1Completed);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var item = items[i];
            string dir = Path.GetDirectoryName(item.OriginalPath) ?? "";
            string tempPath = Path.Combine(dir, $".clickra_tmp_{Guid.NewGuid():N}.tmp");

            try
            {
                System.IO.File.Move(item.OriginalPath, tempPath);
                phase1Completed.Add((item.OriginalPath, tempPath));
            }
            catch (Exception ex)
            {
                errors.Add($"Phase 1 staging failed on '{item.OriginalName}': {ex.Message}");
                RollbackStagedFiles(phase1Completed);
                return (false, phase1Completed, errors);
            }

            int percent = (int)Math.Round((i + 1) * 50.0 / items.Count);
            progress?.Report(new BatchRenameProgress(i + 1, items.Count * 2, item.OriginalName, percent));
        }

        return (true, phase1Completed, errors);
    }

    private static (bool Success, List<(string Original, string NewPath)> Completed, List<string> Errors) FinalizeTwoPhaseFiles(
        List<RenameItem> items,
        List<(string Original, string Temp)> phase1Completed,
        IProgress<BatchRenameProgress>? progress,
        CancellationToken cancellationToken)
    {
        var phase2Completed = new List<(string Original, string NewPath)>();
        var errors = new List<string>();

        for (int i = 0; i < items.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                RollbackFinalizedFiles(phase1Completed, phase2Completed);
                RollbackStagedFiles(phase1Completed);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var item = items[i];
            string tempPath = phase1Completed[i].Temp;

            try
            {
                System.IO.File.Move(tempPath, item.NewPath);
                phase2Completed.Add((item.OriginalPath, item.NewPath));
            }
            catch (Exception ex)
            {
                errors.Add($"Phase 2 finalization failed on '{item.NewName}': {ex.Message}");
                return (false, phase2Completed, errors);
            }

            int percent = 50 + (int)Math.Round((i + 1) * 50.0 / items.Count);
            progress?.Report(new BatchRenameProgress(items.Count + i + 1, items.Count * 2, item.NewName, percent));
        }

        return (true, phase2Completed, errors);
    }

    private static void RollbackStagedFiles(List<(string Original, string Temp)> staged)
    {
        foreach (var (orig, temp) in staged)
        {
            try { System.IO.File.Move(temp, orig); } catch { /* best effort rollback */ }
        }
    }

    private static void RollbackFinalizedFiles(
        List<(string Original, string Temp)> staged,
        List<(string Original, string NewPath)> finalized)
    {
        foreach (var (orig, newP) in finalized)
        {
            string tempP = staged.First(p => p.Original == orig).Temp;
            try { System.IO.File.Move(newP, tempP); } catch { /* best effort rollback */ }
        }
    }

    private static void Rollback(List<(string OldPath, string NewPath)> completed)
    {
        for (int i = completed.Count - 1; i >= 0; i--)
        {
            try
            {
                var (oldPath, newPath) = completed[i];
                if (System.IO.File.Exists(newPath))
                {
                    System.IO.File.Move(newPath, oldPath);
                }
            }
            catch
            {
                // Best effort rollback
            }
        }
    }
}

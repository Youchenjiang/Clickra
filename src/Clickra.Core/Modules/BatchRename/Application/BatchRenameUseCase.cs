using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core.Modules.BatchRename.Domain;
using Clickra.Core.Modules.BatchRename.Processing;

namespace Clickra.Core.Modules.BatchRename.Application;

/// <summary>Application service orchestrating batch file renaming.</summary>
public sealed class BatchRenameUseCase
{
    public const string CommandName = "batch-rename";

    /// <summary>Evaluates and produces a validated rename plan without modifying files.</summary>
    public static BatchRenamePlan Plan(IReadOnlyList<string> filePaths, BatchRenameOptions options)
    {
        return BatchRenameEngine.GeneratePlan(filePaths, options);
    }

    /// <summary>Executes the rename plan with cancellation and progress reporting.</summary>
    public static Task<BatchRenameResult> ExecuteAsync(
        BatchRenamePlan plan,
        IProgress<BatchRenameProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return BatchRenameExecutor.ExecuteAsync(plan, progress, cancellationToken);
    }

    /// <summary>Expands a directory or file list into candidate file paths for renaming.</summary>
    public static List<string> ResolveTargetFiles(IEnumerable<string> inputs, bool recursive = false)
    {
        var result = new List<string>();

        foreach (string input in inputs)
        {
            if (File.Exists(input))
            {
                result.Add(Path.GetFullPath(input));
            }
            else if (Directory.Exists(input))
            {
                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                result.AddRange(Directory.EnumerateFiles(input, "*", searchOption).Select(Path.GetFullPath));
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();
    }
}

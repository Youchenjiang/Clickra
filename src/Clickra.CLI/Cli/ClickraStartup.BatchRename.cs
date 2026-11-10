using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core.Application;
using Clickra.Core.Modules.BatchRename.Application;
using Clickra.Core.Modules.BatchRename.Domain;

namespace Clickra;

internal static partial class ClickraStartup
{
    private sealed record BatchRenameParsedCli(
        List<string> Files,
        BatchRenameOptions Options,
        bool ForceGui,
        bool Quiet,
        bool DryRun,
        bool HasRuleFlags);

    internal static bool TryHandleBatchRenameArgs(string[] args)
    {
        if (args.Length == 0) return false;

        string command = args[0];
        if (!command.Equals("batch-rename", StringComparison.OrdinalIgnoreCase) &&
            !command.Equals("rename", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var argList = args.Skip(1).ToList();

        if (argList.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("help", StringComparison.OrdinalIgnoreCase)))
        {
            PrintBatchRenameUsage();
            return true;
        }

        var parsed = ParseBatchRenameCli(argList);

        if (parsed.ForceGui || (!parsed.Quiet && !parsed.HasRuleFlags))
        {
            Clickra.UI.ProgressWindow.Show(BatchRenameUseCase.CommandName, parsed.Files);
            return true;
        }

        if (parsed.Files.Count == 0)
        {
            Console.WriteLine("[Error] No files or directories specified for batch rename.");
            Console.WriteLine("Run 'Clickra batch-rename --help' for usage instructions.");
            Environment.ExitCode = 1;
            return true;
        }

        var plan = BatchRenameUseCase.Plan(parsed.Files, parsed.Options);

        Console.WriteLine($"Found {parsed.Files.Count} file(s) for batch rename.");
        Console.WriteLine($"Plan summary: {plan.ValidCount} to rename, {plan.UnchangedCount} unchanged, {plan.ConflictCount + plan.InvalidCount} error(s).");

        PrintPlanItems(plan);

        if (plan.ConflictCount > 0 || plan.InvalidCount > 0)
        {
            Console.WriteLine("[Error] Conflicts or invalid names detected in rename plan. Operation aborted.");
            Environment.ExitCode = 1;
            return true;
        }

        if (parsed.DryRun)
        {
            Console.WriteLine($"[Dry Run] Preview complete. {plan.ValidCount} file(s) would be renamed.");
            return true;
        }

        if (plan.ValidCount == 0)
        {
            Console.WriteLine("No files require renaming.");
            return true;
        }

        ExecuteCliPlan(plan);
        return true;
    }

    private static BatchRenameParsedCli ParseBatchRenameCli(List<string> argList)
    {
        bool forceGui = ExtractFlag(argList, "--gui");
        bool quiet = ExtractFlag(argList, "--quiet", "--no-ui");
        bool recursive = ExtractFlag(argList, "--recursive", "-r");
        bool dryRun = ExtractFlag(argList, "--dry-run", "-n");
        bool regex = ExtractFlag(argList, "--regex");
        bool matchCase = ExtractFlag(argList, "--match-case");

        string? prefix = ExtractOption(argList, "--prefix");
        string? suffix = ExtractOption(argList, "--suffix");

        string? startStr = ExtractOption(argList, "--start");
        string? stepStr = ExtractOption(argList, "--step");
        string? padStr = ExtractOption(argList, "--pad");
        string? numPosStr = ExtractOption(argList, "--num-pos");

        string? find = ExtractOption(argList, "--find");
        string? replace = ExtractOption(argList, "--replace") ?? "";

        string? dateSourceStr = ExtractOption(argList, "--date-source");
        string? dateFormat = ExtractOption(argList, "--date-format");
        string? datePosStr = ExtractOption(argList, "--date-pos");

        string? caseStr = ExtractOption(argList, "--case");
        string? extStr = ExtractOption(argList, "--ext");
        string? customExt = ExtractOption(argList, "--custom-ext");

        var inputPaths = argList.Where(a => !a.StartsWith('-')).ToList();
        var files = inputPaths.Count > 0
            ? BatchRenameUseCase.ResolveTargetFiles(inputPaths, recursive)
            : new List<string>();

        var options = new BatchRenameOptions(
            Prefix: prefix,
            Suffix: suffix,
            Numbering: BuildNumberingOptions(startStr, stepStr, padStr, numPosStr),
            Replace: BuildReplaceOptions(find, replace, matchCase, regex),
            Timestamp: BuildTimestampOptions(dateSourceStr, dateFormat, datePosStr),
            CaseTransform: BuildCaseMode(caseStr),
            ExtensionMode: BuildExtensionMode(extStr, customExt),
            CustomExtension: customExt);

        bool hasRuleFlags = HasAnyRuleFlag(options, dryRun);

        return new BatchRenameParsedCli(files, options, forceGui, quiet, dryRun, hasRuleFlags);
    }

    private static bool HasAnyRuleFlag(BatchRenameOptions options, bool dryRun)
    {
        return dryRun ||
               options.Prefix != null ||
               options.Suffix != null ||
               options.Numbering != null ||
               options.Replace != null ||
               options.Timestamp != null ||
               options.CaseTransform != CaseTransformMode.None ||
               options.ExtensionMode != ExtensionTransformMode.KeepOriginal;
    }

    private static void ExecuteCliPlan(BatchRenamePlan plan)
    {
        if (plan.RequiresTwoPhaseSwap)
        {
            Console.WriteLine("[Info] Cyclic rename dependency detected. Executing 2-phase atomic swap...");
        }

        var progress = new Progress<ConversionProgress>(p =>
        {
            Console.WriteLine($"  [{p.Current / 100}/{p.Total / 100}] {p.Message}");
        });

        try
        {
            var conversionPlan = ConversionExecutionService.Plan(
                BatchRenameUseCase.CommandName,
                plan.Items.Select(i => i.OriginalPath).ToList(),
                options: new Dictionary<string, object> { ["plan"] = plan },
                TrackTaskLifecycle: false);

            var result = ConversionExecutionService.Execute(
                conversionPlan,
                progress: progress,
                cancellationToken: CancellationToken.None);

            Console.WriteLine($"Batch rename completed: {result.Outputs.Count} succeeded.");
            if (result.Status != ConversionResultStatus.Succeeded)
            {
                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    Console.WriteLine($"  [Error] {result.Error}");
                }
                Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Batch rename execution failed: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static InsertPosition ParseInsertPosition(string? str, InsertPosition fallback)
    {
        return str?.ToLowerInvariant() switch
        {
            "prefix" => InsertPosition.Prefix,
            "suffix" => InsertPosition.Suffix,
            "replace" => InsertPosition.ReplaceName,
            _ => fallback
        };
    }

    private static string? ExtractOption(List<string> args, params string[] optionNames)
    {
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            foreach (var opt in optionNames)
            {
                if (arg.Equals(opt, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Count)
                    {
                        string val = args[i + 1];
                        args.RemoveAt(i + 1);
                        args.RemoveAt(i);
                        return val;
                    }
                    args.RemoveAt(i);
                    return null;
                }
                string prefix = opt + "=";
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string val = arg.Substring(prefix.Length);
                    args.RemoveAt(i);
                    return val;
                }
            }
        }
        return null;
    }

    private static bool ExtractFlag(List<string> args, params string[] flagNames)
    {
        bool found = false;
        for (int i = args.Count - 1; i >= 0; i--)
        {
            string arg = args[i];
            if (flagNames.Any(f => arg.Equals(f, StringComparison.OrdinalIgnoreCase)))
            {
                found = true;
                args.RemoveAt(i);
            }
        }
        return found;
    }

    private static void PrintBatchRenameUsage()
    {
        Console.WriteLine("Usage: Clickra batch-rename [options] <files or directories...>");
        Console.WriteLine("Options:");
        Console.WriteLine("  --gui                    Launch interactive visual window with live preview");
        Console.WriteLine("  --quiet, --no-ui         Run headlessly without interactive GUI window");
        Console.WriteLine("  --prefix <text>          Add text prefix to filenames");
        Console.WriteLine("  --suffix <text>          Add text suffix to filenames");
        Console.WriteLine("  --start <number>         Starting sequence number (default: 1)");
        Console.WriteLine("  --step <number>          Sequence increment step (default: 1)");
        Console.WriteLine("  --pad <number>           Padding digit width (default: 2, e.g. 01, 02)");
        Console.WriteLine("  --num-pos <position>     Numbering position: prefix, suffix (default), or replace");
        Console.WriteLine("  --find <text>            Text or pattern to search for");
        Console.WriteLine("  --replace <text>         Replacement text for matching search");
        Console.WriteLine("  --regex                  Treat --find pattern as a regular expression");
        Console.WriteLine("  --match-case             Match case sensitively during text search");
        Console.WriteLine("  --case <mode>            Transform filename case: none, lower, upper, title");
        Console.WriteLine("  --ext <mode>             Transform extension: keep, lower, upper, custom");
        Console.WriteLine("  --custom-ext <ext>       New extension to set (e.g. .pdf or pdf)");
        Console.WriteLine("  --date-source <source>   Timestamp source: modified (default) or created");
        Console.WriteLine("  --date-format <format>   Date format string (default: yyyy-MM-dd)");
        Console.WriteLine("  --date-pos <position>    Timestamp position: prefix (default), suffix, or replace");
        Console.WriteLine("  --recursive, -r          Scan subdirectories recursively");
        Console.WriteLine("  --dry-run, -n            Preview renamed files without modifying disk");
        Console.WriteLine("  --help, -h               Show this help message");
    }

    private static NumberingOptions? BuildNumberingOptions(string? startStr, string? stepStr, string? padStr, string? numPosStr)
    {
        if (startStr is null && stepStr is null && padStr is null && numPosStr is null) return null;
        int start = int.TryParse(startStr, out int sVal) ? sVal : 1;
        int step = int.TryParse(stepStr, out int stVal) ? stVal : 1;
        int pad = int.TryParse(padStr, out int pVal) ? pVal : 2;
        var pos = ParseInsertPosition(numPosStr, InsertPosition.Suffix);
        return new NumberingOptions(start, step, pad, pos);
    }

    private static ReplaceOptions? BuildReplaceOptions(string? find, string? replace, bool matchCase, bool regex)
    {
        if (string.IsNullOrEmpty(find)) return null;
        return new ReplaceOptions(
            FindText: find,
            ReplaceWith: replace ?? "",
            IgnoreCase: !matchCase,
            UseRegex: regex);
    }

    private static TimestampOptions? BuildTimestampOptions(string? dateSourceStr, string? dateFormat, string? datePosStr)
    {
        if (dateSourceStr is null && dateFormat is null && datePosStr is null) return null;
        var src = (dateSourceStr?.Equals("created", StringComparison.OrdinalIgnoreCase) == true)
            ? TimestampSource.CreatedTime
            : TimestampSource.ModifiedTime;
        string fmt = string.IsNullOrWhiteSpace(dateFormat) ? "yyyy-MM-dd" : dateFormat;
        var pos = ParseInsertPosition(datePosStr, InsertPosition.Prefix);
        return new TimestampOptions(src, fmt, pos);
    }

    private static CaseTransformMode BuildCaseMode(string? caseStr) =>
        caseStr?.ToLowerInvariant() switch
        {
            "lower" or "lowercase" => CaseTransformMode.LowerCase,
            "upper" or "uppercase" => CaseTransformMode.UpperCase,
            "title" or "titlecase" => CaseTransformMode.TitleCase,
            _ => CaseTransformMode.None
        };

    private static ExtensionTransformMode BuildExtensionMode(string? extStr, string? customExt) =>
        extStr?.ToLowerInvariant() switch
        {
            "lower" or "lowercase" => ExtensionTransformMode.LowerCase,
            "upper" or "uppercase" => ExtensionTransformMode.UpperCase,
            "custom" => ExtensionTransformMode.Custom,
            _ => (!string.IsNullOrEmpty(customExt) ? ExtensionTransformMode.Custom : ExtensionTransformMode.KeepOriginal)
        };

    private static void PrintPlanItems(BatchRenamePlan plan)
    {
        foreach (var item in plan.Items)
        {
            switch (item.Status)
            {
                case RenameItemStatus.Valid:
                    Console.WriteLine($"  [OK]       \"{item.OriginalName}\" -> \"{item.NewName}\"");
                    break;
                case RenameItemStatus.ConflictExistingFile:
                case RenameItemStatus.ConflictDuplicateTarget:
                    Console.WriteLine($"  [CONFLICT] \"{item.OriginalName}\" -> \"{item.NewName}\" ({item.StatusMessage})");
                    break;
                case RenameItemStatus.InvalidFilename:
                case RenameItemStatus.MissingSourceFile:
                    Console.WriteLine($"  [INVALID]  \"{item.OriginalName}\" -> \"{item.NewName}\" ({item.StatusMessage})");
                    break;
                case RenameItemStatus.Unchanged:
                    Console.WriteLine($"  [SKIP]     \"{item.OriginalName}\" (unchanged)");
                    break;
                default:
                    // Unrecognized or unhandled status: no console output needed.
                    break;
            }
        }
    }

}

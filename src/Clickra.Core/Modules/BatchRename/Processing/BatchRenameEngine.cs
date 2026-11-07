using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core.Modules.BatchRename.Application;
using Clickra.Core.Modules.BatchRename.Domain;

namespace Clickra.Core.Modules.BatchRename.Processing;

/// <summary>Generates and validates proposed rename plans based on specified options.</summary>
public static class BatchRenameEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Generates a validated rename plan for the supplied file paths.
    /// </summary>
    public static BatchRenamePlan GeneratePlan(IReadOnlyList<string> filePaths, BatchRenameOptions options)
    {
        if (filePaths == null || filePaths.Count == 0)
        {
            return new BatchRenamePlan([]);
        }

        var sourcePathSet = new HashSet<string>(filePaths, StringComparer.OrdinalIgnoreCase);
        var targetCountMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var preliminaryItems = new List<(string Source, string Directory, string OriginalName, string NewName, string NewPath)>(filePaths.Count);

        int currentNumber = options.Numbering?.StartIndex ?? 1;
        int step = options.Numbering?.Step ?? 1;

        for (int i = 0; i < filePaths.Count; i++)
        {
            string sourcePath = filePaths[i];
            string dir = Path.GetDirectoryName(sourcePath) ?? "";
            string origName = Path.GetFileName(sourcePath);

            string newName = TransformName(sourcePath, options, currentNumber);
            string newPath = Path.Combine(dir, newName);

            preliminaryItems.Add((sourcePath, dir, origName, newName, newPath));
            targetCountMap[newPath] = targetCountMap.TryGetValue(newPath, out int count) ? count + 1 : 1;

            currentNumber += step;
        }

        var finalItems = new List<RenameItem>(preliminaryItems.Count);

        foreach (var (source, _, origName, newName, newPath) in preliminaryItems)
        {
            if (!File.Exists(source))
            {
                finalItems.Add(new RenameItem(source, origName, newName, newPath,
                    RenameItemStatus.MissingSourceFile, "Source file does not exist."));
                continue;
            }

            if (string.Equals(source, newPath, StringComparison.OrdinalIgnoreCase))
            {
                finalItems.Add(new RenameItem(source, origName, newName, newPath,
                    RenameItemStatus.Unchanged, "Filename is unchanged."));
                continue;
            }

            string? validationError = RenameSafetyGuard.ValidateFilename(newName);
            if (validationError != null)
            {
                finalItems.Add(new RenameItem(source, origName, newName, newPath,
                    RenameItemStatus.InvalidFilename, validationError));
                continue;
            }

            if (targetCountMap[newPath] > 1)
            {
                finalItems.Add(new RenameItem(source, origName, newName, newPath,
                    RenameItemStatus.ConflictDuplicateTarget, "Multiple files in this batch target the same filename."));
                continue;
            }

            if (File.Exists(newPath) && !sourcePathSet.Contains(newPath))
            {
                finalItems.Add(new RenameItem(source, origName, newName, newPath,
                    RenameItemStatus.ConflictExistingFile, "A file with this name already exists in the target directory."));
                continue;
            }

            finalItems.Add(new RenameItem(source, origName, newName, newPath,
                RenameItemStatus.Valid, null));
        }

        return new BatchRenamePlan(finalItems);
    }

    /// <summary>Transforms a single file name according to the options.</summary>
    public static string TransformName(string filePath, BatchRenameOptions options, int sequenceNumber)
    {
        string baseName = Path.GetFileNameWithoutExtension(filePath);
        string ext = Path.GetExtension(filePath);

        // 1. Find and replace
        if (options.Replace != null && !string.IsNullOrEmpty(options.Replace.FindText))
        {
            baseName = ApplyReplace(baseName, options.Replace);
        }

        // 2. Prefix and suffix
        if (!string.IsNullOrEmpty(options.Prefix))
        {
            baseName = options.Prefix + baseName;
        }

        if (!string.IsNullOrEmpty(options.Suffix))
        {
            baseName += options.Suffix;
        }

        // 3. Timestamp insertion
        if (options.Timestamp != null)
        {
            baseName = ApplyTimestamp(filePath, baseName, options.Timestamp);
        }

        // 4. Sequential numbering
        if (options.Numbering != null)
        {
            baseName = ApplyNumbering(baseName, sequenceNumber, options.Numbering);
        }

        // 5. Case conversion on baseName
        baseName = ApplyCaseTransform(baseName, options.CaseTransform);

        // 6. Extension transformation
        ext = ApplyExtensionTransform(ext, options.ExtensionMode, options.CustomExtension);

        return baseName + ext;
    }

    private static string ApplyReplace(string text, ReplaceOptions options)
    {
        if (options.UseRegex)
        {
            var regexOptions = RegexOptions.None;
            if (options.IgnoreCase) regexOptions |= RegexOptions.IgnoreCase;
            return Regex.Replace(text, options.FindText, options.ReplaceWith, regexOptions, RegexTimeout);
        }

        var comparison = options.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return text.Replace(options.FindText, options.ReplaceWith, comparison);
    }

    private static string ApplyTimestamp(string filePath, string baseName, TimestampOptions options)
    {
        DateTime dt = DateTime.Now;
        try
        {
            if (File.Exists(filePath))
            {
                dt = options.Source == TimestampSource.CreatedTime
                    ? File.GetCreationTime(filePath)
                    : File.GetLastWriteTime(filePath);
            }
        }
        catch
        {
            // Fall back to current time if filesystem read fails
        }

        string timeStr = dt.ToString(options.Format, CultureInfo.InvariantCulture);

        return options.Position switch
        {
            InsertPosition.Prefix => timeStr + options.Separator + baseName,
            InsertPosition.Suffix => baseName + options.Separator + timeStr,
            InsertPosition.ReplaceName => timeStr,
            _ => baseName
        };
    }

    private static string ApplyNumbering(string baseName, int number, NumberingOptions options)
    {
        string numStr = number.ToString("D" + Math.Max(1, options.PaddingDigits), CultureInfo.InvariantCulture);

        return options.Position switch
        {
            InsertPosition.Prefix => numStr + options.Separator + baseName,
            InsertPosition.Suffix => baseName + options.Separator + numStr,
            InsertPosition.ReplaceName => numStr,
            _ => baseName
        };
    }

    private static string ApplyCaseTransform(string text, CaseTransformMode mode) => mode switch
    {
        CaseTransformMode.LowerCase => text.ToLowerInvariant(),
        CaseTransformMode.UpperCase => text.ToUpperInvariant(),
        CaseTransformMode.TitleCase => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant()),
        _ => text
    };

    private static string ApplyExtensionTransform(string currentExt, ExtensionTransformMode mode, string? customExt) => mode switch
    {
        ExtensionTransformMode.LowerCase => currentExt.ToLowerInvariant(),
        ExtensionTransformMode.UpperCase => currentExt.ToUpperInvariant(),
        ExtensionTransformMode.Custom when !string.IsNullOrWhiteSpace(customExt) =>
            customExt.StartsWith('.') ? customExt : "." + customExt,
        _ => currentExt
    };
}

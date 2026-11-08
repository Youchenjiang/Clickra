using System;

namespace Clickra.Core.Modules.BatchRename.Application;

/// <summary>Position where sequential numbers or timestamps are inserted.</summary>
public enum InsertPosition
{
    Prefix,
    Suffix,
    ReplaceName
}

/// <summary>Case conversion modes for renamed filenames.</summary>
public enum CaseTransformMode
{
    None,
    LowerCase,
    UpperCase,
    TitleCase
}

/// <summary>Extension modification modes.</summary>
public enum ExtensionTransformMode
{
    KeepOriginal,
    LowerCase,
    UpperCase,
    Custom
}

/// <summary>Timestamp source for datetime-based naming.</summary>
public enum TimestampSource
{
    CreatedTime,
    ModifiedTime
}

/// <summary>Options for sequential numbering.</summary>
public sealed record NumberingOptions(
    int StartIndex = 1,
    int Step = 1,
    int PaddingDigits = 2,
    InsertPosition Position = InsertPosition.Suffix,
    string Separator = "_");

/// <summary>Options for text search and replace within filenames.</summary>
public sealed record ReplaceOptions(
    string FindText,
    string ReplaceWith = "",
    bool IgnoreCase = true,
    bool UseRegex = false);

/// <summary>Options for inserting file date/time into filename.</summary>
public sealed record TimestampOptions(
    TimestampSource Source = TimestampSource.ModifiedTime,
    string Format = "yyyy-MM-dd",
    InsertPosition Position = InsertPosition.Prefix,
    string Separator = "_");

/// <summary>Comprehensive options for configuring batch rename operations.</summary>
public sealed record BatchRenameOptions(
    string? Prefix = null,
    string? Suffix = null,
    NumberingOptions? Numbering = null,
    ReplaceOptions? Replace = null,
    TimestampOptions? Timestamp = null,
    CaseTransformMode CaseTransform = CaseTransformMode.None,
    ExtensionTransformMode ExtensionMode = ExtensionTransformMode.KeepOriginal,
    string? CustomExtension = null);

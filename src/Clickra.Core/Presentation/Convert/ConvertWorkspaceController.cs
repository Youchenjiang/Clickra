using Clickra.Core.Application;

namespace Clickra.Core.Presentation.Convert;

public enum ConvertWorkspaceValidationKind
{
    None,
    NoCommand,
    TooFewFiles,
    InvalidExtension
}

public readonly record struct ConvertWorkspaceValidation(
    ConvertWorkspaceValidationKind Kind,
    string Command = "",
    int MinimumFiles = 0,
    string InvalidFile = "")
{
    public bool IsValid => Kind == ConvertWorkspaceValidationKind.None;
}

public sealed record ConvertCommandPresentation(
    string Command,
    string LabelKey,
    string GroupKey,
    int Group,
    bool RequiresOffice);

/// <summary>
/// Authoritative presentation owner for the Convert screen. Native and Fluent render this
/// state and forward user intent here instead of maintaining separate command/selection policy.
/// </summary>
public sealed class ConvertWorkspaceController
{
    public const string BatchRenameCommand = "batch-rename";
    private const string ConvertGroup = "convert_group_convert";
    private const string OrganizeGroup = "convert_group_organize";
    private const string OptimizeGroup = "convert_group_optimize";
    private const string ProcessGroup = "convert_group_process";
    private static readonly ConvertCommandPresentation[] CommandCatalogValue =
    [
        new("word2pdf", "cmd_word_to_pdf", ConvertGroup, 0, true),
        new("excel2pdf", "cmd_excel_to_pdf", ConvertGroup, 0, true),
        new("ppt2pdf", "cmd_ppt_to_pdf", ConvertGroup, 0, true),
        new("md2pdf", "cmd_md_to_pdf", ConvertGroup, 0, false),
        new("md2word", "cmd_md_to_word", ConvertGroup, 0, false),
        new("img2pdf", "cmd_img_to_pdf", ConvertGroup, 0, false),
        new("img-to-png", "cmd_img_to_png", ConvertGroup, 0, false),
        new("img-to-jpg", "cmd_img_to_jpg", ConvertGroup, 0, false),
        new("img-to-webp", "cmd_img_to_webp", ConvertGroup, 0, false),
        new("img-to-gif", "cmd_img_to_gif", ConvertGroup, 0, false),
        new("img-to-heic", "cmd_img_to_heic", ConvertGroup, 0, false),
        new("pdf2img", "cmd_pdf_to_img", ConvertGroup, 0, false),
        new("merge-pdf", "cmd_merge_pdf", OrganizeGroup, 1, false),
        new("split-pdf", "cmd_split_pdf", OrganizeGroup, 1, false),
        new("img-merge", "cmd_merge_img", OrganizeGroup, 1, false),
        new("img-stitch", "cmd_stitch_img", OrganizeGroup, 1, false),
        new(BatchRenameCommand, "cmd_batch_rename", OrganizeGroup, 1, false),
        new("compress-pdf", "cmd_compress_pdf", OptimizeGroup, 2, false),
        new("img-compress", "cmd_img_compress", OptimizeGroup, 2, false),
        new("translate-pdf", "cmd_translate_pdf", ProcessGroup, 3, false),
        new("decrypt-pdf", "cmd_decrypt_pdf", ProcessGroup, 3, false),
        new("print-pdf", "cmd_print_pdf", ProcessGroup, 3, false)
    ];

    private static readonly IReadOnlyDictionary<string, ConvertCommandPresentation> CommandByKey =
        CommandCatalogValue.ToDictionary(item => item.Command, StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _selectedFiles = new();
    private readonly IReadOnlyList<string> _selectedFilesView;

    public ConvertWorkspaceController()
    {
        _selectedFilesView = _selectedFiles.AsReadOnly();
    }

    public static IReadOnlyList<ConvertCommandPresentation> CommandCatalog => CommandCatalogValue;

    public static IReadOnlyList<string> AllSupportedExtensions => ConvertCommandRegistry.AllSupportedExtensions;

    public static string[] GetAllowedExtensions(string command) =>
        ConversionExecutionController.AcceptsAllFileExtensions(command)
            ? ["*.*"]
            : ConvertCommandRegistry.GetAllowedExtensions(command);

    public static List<string> SplitLaunchArguments(string value) => ConvertCommandRegistry.SplitCommandLine(value);

    public static bool IsKnownCommand(string command) =>
        CommandByKey.ContainsKey(command) &&
        (ConversionExecutionController.AcceptsAllFileExtensions(command) || ConvertCommandRegistry.IsKnownCommand(command));

    public static IEnumerable<string> ExpandLaunchArguments(string command, IEnumerable<string> inputs) =>
        ConversionExecutionController.AcceptsAllFileExtensions(command)
            ? inputs
            : ConvertCommandRegistry.ExpandDirectoryArguments(command, inputs);

    public static string GetCommandLabelKey(string command) =>
        CommandByKey.TryGetValue(command, out ConvertCommandPresentation? presentation)
            ? presentation.LabelKey
            : ConvertCommandRegistry.GetLabelKey(command);

    public IReadOnlyList<string> SelectedFiles => _selectedFilesView;

    public string? SelectedCommand { get; private set; }

    public bool IsRunning { get; private set; }

    public bool HasFiles => _selectedFiles.Count > 0;

    public bool CanStart => !IsRunning && ValidateSelection().IsValid;

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                _selectedFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            _selectedFiles.Add(path);
        }

        NormalizeOrRouteSelection();
    }

    public void ReplaceFiles(IEnumerable<string> paths)
    {
        _selectedFiles.Clear();
        AddFiles(paths);
    }

    public void ClearFiles()
    {
        _selectedFiles.Clear();
    }

    public bool TrySelectCommand(string command)
    {
        if (!CommandByKey.ContainsKey(command) || !IsCommandCompatible(command)) return false;
        SelectedCommand = command;
        return true;
    }

    public bool IsCommandCompatible(string command)
    {
        if (!CommandByKey.ContainsKey(command)) return false;
        if (!command.Equals(BatchRenameCommand, StringComparison.OrdinalIgnoreCase) && !ConvertCommandRegistry.IsKnownCommand(command)) return false;
        if (_selectedFiles.Count == 0) return true;
        return ValidateCommand(command).IsValid;
    }

    public ConvertWorkspaceValidation ValidateSelection() =>
        SelectedCommand is null
            ? new(ConvertWorkspaceValidationKind.NoCommand)
            : ValidateCommand(SelectedCommand);

    public ConvertWorkspaceValidation ValidateCommand(string command)
    {
        if (!CommandByKey.ContainsKey(command))
            return new(ConvertWorkspaceValidationKind.NoCommand);

        if (ConversionExecutionController.AcceptsAllFileExtensions(command))
        {
            const int minFiles = 1;
            if (_selectedFiles.Count < minFiles)
                return new(ConvertWorkspaceValidationKind.TooFewFiles, command, minFiles);
            return new(ConvertWorkspaceValidationKind.None, command, minFiles);
        }

        if (!ConvertCommandRegistry.IsKnownCommand(command))
            return new(ConvertWorkspaceValidationKind.NoCommand);

        int minimumFiles = ConvertCommandRegistry.GetMinFiles(command);
        if (_selectedFiles.Count < minimumFiles)
            return new(ConvertWorkspaceValidationKind.TooFewFiles, command, minimumFiles);

        string[] extensions = ConvertCommandRegistry.GetAllowedExtensions(command);
        string? invalid = _selectedFiles.FirstOrDefault(file =>
            !extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
        return invalid is null
            ? new(ConvertWorkspaceValidationKind.None, command, minimumFiles)
            : new(ConvertWorkspaceValidationKind.InvalidExtension, command, minimumFiles, invalid);
    }

    public IReadOnlyList<string> GetPickerExtensions()
    {
        if (SelectedCommand is not null && ConversionExecutionController.AcceptsAllFileExtensions(SelectedCommand))
            return [ "*" ];
        if (SelectedCommand is null)
            return ConvertCommandRegistry.AllSupportedExtensions;
        return ConvertCommandRegistry.GetAllowedExtensions(SelectedCommand);
    }

    public ConvertCommandPresentation? GetSelectedPresentation() =>
        SelectedCommand is not null && CommandByKey.TryGetValue(SelectedCommand, out ConvertCommandPresentation? presentation)
            ? presentation
            : null;

    public static ConvertCommandPresentation GetRequiredPresentation(string command) =>
        CommandByKey.TryGetValue(command, out ConvertCommandPresentation? presentation)
            ? presentation
            : throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown conversion command.");

    public void BeginRun()
    {
        if (!CanStart) throw new InvalidOperationException("The conversion workspace is not ready to start.");
        IsRunning = true;
    }

    public void EndRun() => IsRunning = false;

    private void NormalizeOrRouteSelection()
    {
        if (SelectedCommand is not null && IsCommandCompatible(SelectedCommand)) return;
        string? defaultCommand = ConvertCommandRegistry.GetDefaultCommandForFiles(_selectedFiles);
        SelectedCommand = defaultCommand is not null && CommandByKey.ContainsKey(defaultCommand)
            ? defaultCommand
            : null;
    }
}

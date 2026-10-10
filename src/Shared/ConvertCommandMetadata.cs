using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clickra.Shared;

/// <summary>Dependency-free conversion command metadata shared by Core and the NativeAOT shell.</summary>
public static class ConvertCommandMetadata
{
    private const string CmdMergePdf = "merge-pdf";
    private const string CmdCompressPdf = "compress-pdf";
    private const string CmdImg2Pdf = "img2pdf";
    private const string CmdImgMerge = "img-merge";

    private sealed record CommandDef(
        string FileType,
        string[] Extensions,
        int MinFiles,
        string LabelKey,
        string[]? ExcludedExtensions = null);

    private static readonly string[] PdfExtensions = [".pdf"];
    private static readonly string[] PptExtensions = [".ppt", ".pptx"];
    private static readonly string[] WordExtensions = [".doc", ".docx"];
    private static readonly string[] ExcelExtensions = [".xls", ".xlsx"];
    private static readonly string[] MarkdownExtensions = [".md", ".markdown"];
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp", ".heic"];
    private static readonly string[] ImageCompressionExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif", ".hif"];

    private static readonly Dictionary<string, string[]> FileTypeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"] = PdfExtensions,
        ["word"] = WordExtensions,
        ["excel"] = ExcelExtensions,
        ["ppt"] = PptExtensions,
        ["markdown"] = MarkdownExtensions,
        ["image"] = ImageExtensions,
    };

    private static readonly Dictionary<string, CommandDef> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ppt2pdf"] = new("ppt", PptExtensions, 1, "cmd_ppt_to_pdf"),
        ["word2pdf"] = new("word", WordExtensions, 1, "cmd_word_to_pdf"),
        ["excel2pdf"] = new("excel", ExcelExtensions, 1, "cmd_excel_to_pdf"),
        ["md2pdf"] = new("markdown", MarkdownExtensions, 1, "cmd_md_to_pdf"),
        ["md2word"] = new("markdown", MarkdownExtensions, 1, "cmd_md_to_word"),
        [CmdMergePdf] = new("pdf", PdfExtensions, 2, "cmd_merge_pdf"),
        [CmdCompressPdf] = new("pdf", PdfExtensions, 1, "cmd_compress_pdf"),
        ["translate-pdf"] = new("pdf", PdfExtensions, 1, "cmd_translate_pdf"),
        ["decrypt-pdf"] = new("pdf", PdfExtensions, 1, "cmd_decrypt_pdf"),
        ["split-pdf"] = new("pdf", PdfExtensions, 1, "cmd_split_pdf"),
        [CmdImg2Pdf] = new("image", ImageExtensions, 1, "cmd_img_to_pdf"),
        [CmdImgMerge] = new("image", ImageExtensions, 2, "cmd_merge_img"),
        ["img-stitch"] = new("image", ImageExtensions, 2, "cmd_stitch_img"),
        ["img-compress"] = new("image", ImageCompressionExtensions, 1, "cmd_img_compress"),
        ["img-to-png"] = new("image", ImageExtensions, 1, "cmd_img_to_png", [".png"]),
        ["img-to-jpg"] = new("image", ImageExtensions, 1, "cmd_img_to_jpg", [".jpg", ".jpeg"]),
        ["img-to-webp"] = new("image", ImageExtensions, 1, "cmd_img_to_webp", [".webp"]),
        ["img-to-gif"] = new("image", ImageExtensions, 1, "cmd_img_to_gif", [".gif"]),
        ["img-to-heic"] = new("image", ImageExtensions, 1, "cmd_img_to_heic", [".heic"]),
    };

    private static readonly string[] AllSupportedExtensionsValue = Commands.Values
        .SelectMany(definition => definition.Extensions)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static string[] AllSupportedExtensions => AllSupportedExtensionsValue;

    public static string[] GetAllowedExtensions(string? command)
    {
        if (command is null || !Commands.TryGetValue(command, out CommandDef? definition)) return [];
        if (definition.ExcludedExtensions is not { Length: > 0 } excluded) return definition.Extensions;
        return definition.Extensions
            .Where(extension => !excluded.Contains(extension, StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

    public static string[] GetExcludedExtensions(string? command) =>
        command is not null && Commands.TryGetValue(command, out CommandDef? definition)
            ? definition.ExcludedExtensions ?? []
            : [];

    public static bool IsKnownCommand(string command) => Commands.ContainsKey(command);

    public static int GetMinFiles(string command) =>
        Commands.TryGetValue(command, out CommandDef? definition) ? definition.MinFiles : 1;

    public static string GetLabelKey(string command) =>
        Commands.TryGetValue(command, out CommandDef? definition) ? definition.LabelKey : command;

    public static string[] GetCommandsForType(string type) => Commands
        .Where(pair => string.Equals(pair.Value.FileType, type, StringComparison.OrdinalIgnoreCase))
        .Select(pair => pair.Key)
        .ToArray();

    public static string[] GetAllowedExtensionsByType(string type) =>
        FileTypeExtensions.TryGetValue(type, out string[]? extensions) ? extensions : [];

    public static string GetFileTypeForCommand(string command) =>
        Commands.TryGetValue(command, out CommandDef? definition) ? definition.FileType : "pdf";

    public static string? GetDefaultCommandForFiles(IReadOnlyCollection<string> files)
    {
        if (files.Count == 0) return null;

        string[] extensions = files
            .Select(path => Path.GetExtension(path).ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (extensions.Any(string.IsNullOrEmpty)) return null;

        if (AllAllowed(extensions, "ppt2pdf")) return "ppt2pdf";
        if (AllAllowed(extensions, "word2pdf")) return "word2pdf";
        if (AllAllowed(extensions, "excel2pdf")) return "excel2pdf";
        if (AllAllowed(extensions, CmdCompressPdf)) return files.Count == 1 ? CmdCompressPdf : CmdMergePdf;
        if (AllAllowed(extensions, "md2pdf")) return "md2pdf";
        if (AllAllowed(extensions, CmdImg2Pdf)) return files.Count == 1 ? CmdImg2Pdf : CmdImgMerge;

        return null;
    }

    private static bool AllAllowed(IEnumerable<string> extensions, string command)
    {
        string[] allowed = GetAllowedExtensions(command);
        return extensions.All(extension => allowed.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }
}

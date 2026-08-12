using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clickra.Core.Processors
{
    /// <summary>Single source of truth for convert command metadata shared by the
    /// Fluent and NativeAOT UIs. Adding a command means editing this table once.</summary>
    public static class ConvertCommandRegistry
    {
        private sealed record CommandDef(string[] Extensions, int MinFiles, string LabelKey);

        private static readonly string[] PdfExtensions = { ".pdf" };
        private static readonly string[] PptExtensions = { ".ppt", ".pptx" };
        private static readonly string[] WordExtensions = { ".doc", ".docx" };
        private static readonly string[] ExcelExtensions = { ".xls", ".xlsx" };
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" };

        /// <summary>Every convert command and its metadata, in dashboard order.</summary>
        private static readonly Dictionary<string, CommandDef> Commands = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ppt2pdf"] = new(PptExtensions, 1, "cmd_ppt_to_pdf"),
            ["word2pdf"] = new(WordExtensions, 1, "cmd_word_to_pdf"),
            ["excel2pdf"] = new(ExcelExtensions, 1, "cmd_excel_to_pdf"),
            ["merge-pdf"] = new(PdfExtensions, 2, "cmd_merge_pdf"),
            ["compress-pdf"] = new(PdfExtensions, 1, "cmd_compress_pdf"),
            ["translate-pdf"] = new(PdfExtensions, 1, "cmd_translate_pdf"),
            ["decrypt-pdf"] = new(PdfExtensions, 1, "cmd_decrypt_pdf"),
            ["split-pdf"] = new(PdfExtensions, 1, "cmd_split_pdf"),
            ["img2pdf"] = new(ImageExtensions, 1, "cmd_img_to_pdf"),
            ["img-merge"] = new(ImageExtensions, 2, "cmd_merge_img"),
            ["img-stitch"] = new(ImageExtensions, 2, "cmd_stitch_img")
        };

        private static readonly string[] AllSupportedExtensionsValue =
            Commands.Values.SelectMany(def => def.Extensions).Distinct().ToArray();

        /// <summary>Every file type any convert command accepts, used for unfiltered pickers.</summary>
        public static string[] AllSupportedExtensions => AllSupportedExtensionsValue;

        /// <summary>File extensions a command accepts; empty when the command is unknown.</summary>
        public static string[] GetAllowedExtensions(string? command) =>
            command is not null && Commands.TryGetValue(command, out var def) ? def.Extensions : Array.Empty<string>();

        /// <summary>Whether the command key maps to a known conversion.</summary>
        public static bool IsKnownCommand(string command) => Commands.ContainsKey(command);

        /// <summary>Minimum number of files the command requires.</summary>
        public static int GetMinFiles(string command) =>
            Commands.TryGetValue(command, out var def) ? def.MinFiles : 1;

        /// <summary>Localization key for the command display name.</summary>
        public static string GetLabelKey(string command) =>
            Commands.TryGetValue(command, out var def) ? def.LabelKey : command;

        /// <summary>Predicts the output paths a command will produce for the given files.</summary>
        public static List<string> EstimateOutputs(string command, List<string> files)
        {
            string outputDir = ClickraStorage.GetOutputDir(files[0]);
            return command switch
            {
                "merge-pdf" => new() { Path.Combine(outputDir, "Merged_PDF.pdf") },
                "img-merge" => new() { Path.Combine(outputDir, "Merged_Images.pdf") },
                "img-stitch" => new() { Path.Combine(outputDir, "Stitched_Image.png") },
                "compress-pdf" => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + "_compressed.pdf")).ToList(),
                "translate-pdf" => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + "_translated.pdf")).ToList(),
                "decrypt-pdf" => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + "_decrypted.pdf")).ToList(),
                "split-pdf" => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + "_split.pdf")).ToList(),
                "img2pdf" => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + ".pdf")).ToList(),
                _ => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + ".pdf")).ToList()
            };
        }

        /// <summary>Current PDF compression settings as a parameter dictionary.</summary>
        public static Dictionary<string, object> CompressionOptions() => new()
        {
            ["level"] = ClickraStorage.GetSetting("PdfCompressImageLevel") switch { "0" => "small", "2" or "3" => "high", _ => "balanced" },
            ["strip_fonts"] = ClickraStorage.GetSetting("PdfCompressStripFonts").Equals("true", StringComparison.OrdinalIgnoreCase),
            ["minify_content"] = !ClickraStorage.GetSetting("PdfCompressMinifyContent").Equals("false", StringComparison.OrdinalIgnoreCase)
        };

        /// <summary>Splits a command line string into arguments, honoring double quotes.</summary>
        public static List<string> SplitCommandLine(string value)
        {
            var args = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuote = false;

            foreach (char ch in value)
            {
                if (ch == '"')
                {
                    inQuote = !inQuote;
                    continue;
                }

                if (char.IsWhiteSpace(ch) && !inQuote)
                {
                    if (current.Length > 0)
                    {
                        args.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0) args.Add(current.ToString());
            return args;
        }

        /// <summary>Expands directory arguments into the command's convertible files.</summary>
        public static IEnumerable<string> ExpandDirectoryArguments(string command, IEnumerable<string> inputs)
        {
            string[] allowed = GetAllowedExtensions(command);
            foreach (var input in inputs)
            {
                if (Directory.Exists(input))
                {
                    foreach (var file in Directory.EnumerateFiles(input).Where(file => allowed.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)))
                        yield return file;
                }
                else
                {
                    yield return input;
                }
            }
        }
    }
}

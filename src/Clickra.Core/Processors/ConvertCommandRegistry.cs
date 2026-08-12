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
        /// <summary>Every file type any convert command accepts, used for unfiltered pickers.</summary>
        public static readonly string[] AllSupportedExtensions = { ".pdf", ".ppt", ".pptx", ".doc", ".docx", ".xls", ".xlsx", ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" };

        /// <summary>File extensions a command accepts; empty when the command is unknown.</summary>
        public static string[] GetAllowedExtensions(string? command) => command switch
        {
            "ppt2pdf" => new[] { ".ppt", ".pptx" },
            "word2pdf" => new[] { ".doc", ".docx" },
            "excel2pdf" => new[] { ".xls", ".xlsx" },
            "merge-pdf" or "compress-pdf" or "translate-pdf" or "decrypt-pdf" or "split-pdf" => new[] { ".pdf" },
            "img2pdf" or "img-merge" or "img-stitch" => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" },
            _ => Array.Empty<string>()
        };

        /// <summary>Whether the command key maps to a known conversion.</summary>
        public static bool IsKnownCommand(string command) => GetAllowedExtensions(command).Length > 0;

        /// <summary>Minimum number of files the command requires.</summary>
        public static int GetMinFiles(string command) => command is "merge-pdf" or "img-merge" or "img-stitch" ? 2 : 1;

        /// <summary>Localization key for the command display name.</summary>
        public static string GetLabelKey(string command) => command switch
        {
            "ppt2pdf" => "cmd_ppt_to_pdf",
            "word2pdf" => "cmd_word_to_pdf",
            "excel2pdf" => "cmd_excel_to_pdf",
            "merge-pdf" => "cmd_merge_pdf",
            "compress-pdf" => "cmd_compress_pdf",
            "translate-pdf" => "cmd_translate_pdf",
            "decrypt-pdf" => "cmd_decrypt_pdf",
            "split-pdf" => "cmd_split_pdf",
            "img2pdf" => "cmd_img_to_pdf",
            "img-merge" => "cmd_merge_img",
            "img-stitch" => "cmd_stitch_img",
            _ => command
        };

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

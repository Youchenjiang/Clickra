using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clickra.Core.Processors;

/// <summary>Single source of truth for convert command metadata shared by the
/// Fluent and NativeAOT UIs. Adding a command means editing this table once.</summary>
public static class ConvertCommandRegistry
{
        private sealed record CommandDef(string[] Extensions, int MinFiles, string LabelKey, string[]? ExcludeExtensions = null);

        private static readonly string[] PdfExtensions = { ".pdf" };
        private static readonly string[] PptExtensions = { ".ppt", ".pptx" };
        private static readonly string[] WordExtensions = { ".doc", ".docx" };
        private static readonly string[] ExcelExtensions = { ".xls", ".xlsx" };
        private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic"];

        /// <summary>UI 檔案類型分類：先選類型再選命令，從源頭避免混雜類型。</summary>
        private static readonly (string Type, string[] Extensions, string[] Commands)[] FileTypes =
        {
            ("pdf", PdfExtensions, ["merge-pdf", "compress-pdf", "translate-pdf", "decrypt-pdf", "split-pdf"]),
            ("word", WordExtensions, ["word2pdf"]),
            ("excel", ExcelExtensions, ["excel2pdf"]),
            ("ppt", PptExtensions, ["ppt2pdf"]),
            ("image", ImageExtensions, ["img2pdf", "img-merge", "img-stitch", "img-to-png", "img-to-jpg", "img-to-webp", "img-to-gif", "img-to-heic", "img-compress"])
        };

        /// <summary>File extensions accepted by a UI file type ("pdf", "word", "excel", "ppt", "image").</summary>
        public static string[] GetAllowedExtensionsByType(string type)
        {
            var entry = Array.Find(FileTypes, e => string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase));
            return entry.Extensions ?? Array.Empty<string>();
        }

        /// <summary>Convert commands available for a UI file type.</summary>
        public static string[] GetCommandsForType(string type)
        {
            var entry = Array.Find(FileTypes, e => string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase));
            return entry.Commands ?? Array.Empty<string>();
        }

        /// <summary>The UI file type a command belongs to (defaults to "pdf" for unknown commands).</summary>
        public static string GetFileTypeForCommand(string command)
        {
            var entry = Array.Find(FileTypes, e => e.Commands.Contains(command, StringComparer.OrdinalIgnoreCase));
            return entry.Type ?? "pdf";
        }

        /// <summary>Converting to a format the file already has is a no-op, so format
        /// commands declare the source extensions they must exclude (jpg/jpeg are aliases
        /// and are always excluded together).</summary>
        private static readonly string[] PngExcluded = { ".png" };
        private static readonly string[] JpegExcluded = { ".jpg", ".jpeg" };
        private static readonly string[] WebpExcluded = { ".webp" };
        private static readonly string[] GifExcluded = { ".gif" };
        private static readonly string[] HeicExcluded = { ".heic" };

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
            ["img-stitch"] = new(ImageExtensions, 2, "cmd_stitch_img"),
            ["img-to-png"] = new(ImageExtensions, 1, "cmd_img_to_png", PngExcluded),
            ["img-to-jpg"] = new(ImageExtensions, 1, "cmd_img_to_jpg", JpegExcluded),
            ["img-to-webp"] = new(ImageExtensions, 1, "cmd_img_to_webp", WebpExcluded),
            ["img-to-gif"] = new(ImageExtensions, 1, "cmd_img_to_gif", GifExcluded),
            ["img-to-heic"] = new(ImageExtensions, 1, "cmd_img_to_heic", HeicExcluded),
            ["img-compress"] = new(ImageExtensions, 1, "cmd_img_compress")
        };

        private static readonly string[] AllSupportedExtensionsValue =
            Commands.Values.SelectMany(def => def.Extensions).Distinct().ToArray();

        /// <summary>Every file type any convert command accepts, used for unfiltered pickers.</summary>
        public static string[] AllSupportedExtensions => AllSupportedExtensionsValue;

        /// <summary>File extensions a command accepts; empty when the command is unknown.
        /// For format-conversion commands this already excludes the source extensions that
        /// would make the conversion a no-op (e.g. img-to-png does not accept .png).</summary>
        public static string[] GetAllowedExtensions(string? command)
        {
            if (command is null || !Commands.TryGetValue(command, out var def)) return Array.Empty<string>();
            if (def.ExcludeExtensions is not { Length: > 0 } excluded) return def.Extensions;
            return def.Extensions.Where(ext => !excluded.Contains(ext, StringComparer.OrdinalIgnoreCase)).ToArray();
        }

        /// <summary>Source extensions a command must exclude to avoid no-op conversions;
        /// empty when the command accepts everything it lists.</summary>
        public static string[] GetExcludedExtensions(string? command) =>
            command is not null && Commands.TryGetValue(command, out var def)
                ? def.ExcludeExtensions ?? Array.Empty<string>()
                : Array.Empty<string>();

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
                "img-compress" => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + "_compressed" + Path.GetExtension(f))).ToList(),
                "img-to-png" => ImageConvertOutputs(files, ".png"),
                "img-to-jpg" => ImageConvertOutputs(files, ".jpg"),
                "img-to-webp" => ImageConvertOutputs(files, ".webp"),
                "img-to-gif" => ImageConvertOutputs(files, ".gif"),
                "img-to-heic" => ImageConvertOutputs(files, ".heic"),
                _ => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + ".pdf")).ToList()
            };
        }

        /// <summary>Predicts one output path per input file for image format conversion
        /// commands: same directory and base name, target extension.</summary>
        private static List<string> ImageConvertOutputs(List<string> files, string extension)
            => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + extension)).ToList();

        /// <summary>Reads the current slider level from settings (0-3, default 1: balanced).</summary>
        public static int GetPdfCompressLevel()
        {
            string levelStr = ClickraStorage.GetSetting("PdfCompressImageLevel");
            if (int.TryParse(levelStr, out int lvl) && lvl >= 0 && lvl <= 3)
                return lvl;

            return ClickraStorage.GetSetting("PdfCompressTargetDpi") switch
            {
                "300" => 3,
                "150" => 1,
                "0" => 3,
                _ => 1
            };
        }

        /// <summary>Current PDF compression settings as a parameter dictionary.</summary>
        public static Dictionary<string, object> CompressionOptions()
        {
            int sliderLevel = GetPdfCompressLevel();
            var level = PdfCompressionOptions.FromSliderLevel(sliderLevel);
            return new Dictionary<string, object>
            {
                ["level"] = PdfCompressionOptions.ToOptionName(level),
                ["strip_fonts"] = ClickraStorage.GetSetting("PdfCompressStripFonts").Equals("true", StringComparison.OrdinalIgnoreCase),
                ["minify_content"] = !ClickraStorage.GetSetting("PdfCompressMinifyContent").Equals("false", StringComparison.OrdinalIgnoreCase)
            };
        }

        /// <summary>Current image compression settings as a parameter dictionary: a 0-3
        /// quality level (mirroring the PDF compression slider) and a max long-edge
        /// dimension (0 = keep original size).</summary>
        public static Dictionary<string, object> ImageCompressionOptions() => new()
        {
            ["level"] = GetImageCompressLevel(),
            ["max_dimension"] = GetImageCompressMaxDimension()
        };

        /// <summary>The saved image compression quality level, clamped to 0-3.</summary>
        public static int GetImageCompressLevel()
        {
            return int.TryParse(ClickraStorage.GetSetting("ImageCompressLevel"), out int level)
                ? Math.Clamp(level, 0, 3)
                : 1;
        }

        /// <summary>The saved max long-edge dimension for image compression (0 = original).</summary>
        public static int GetImageCompressMaxDimension()
        {
            return int.TryParse(ClickraStorage.GetSetting("ImageCompressMaxDimension"), out int dim)
                ? Math.Max(0, dim)
                : 0;
        }

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

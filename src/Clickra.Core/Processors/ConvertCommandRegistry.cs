using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clickra.Shared;

namespace Clickra.Core.Processors;

/// <summary>Single source of truth for convert command metadata shared by the
/// Fluent and NativeAOT UIs. Adding a command means editing this table once.</summary>
public static class ConvertCommandRegistry
{
        private const string CmdImgToPng = "img-to-png";
        private const string CmdImgToJpg = "img-to-jpg";
        private const string CmdImgToWebp = "img-to-webp";
        private const string CmdImgToGif = "img-to-gif";
        private const string CmdImgToHeic = "img-to-heic";
        private const string CmdMdToPdf = "md2pdf";
        private const string CmdMdToWord = "md2word";
        private const string CmdImgCompress = "img-compress";
        private const string CmdMergePdf = "merge-pdf";
        private const string CmdCompressPdf = "compress-pdf";
        private const string CmdImg2Pdf = "img2pdf";
        private const string CmdImgMerge = "img-merge";
        /// <summary>File extensions accepted by a UI file type ("pdf", "word", "excel", "ppt", "image").</summary>
        public static string[] GetAllowedExtensionsByType(string type) => ConvertCommandMetadata.GetAllowedExtensionsByType(type);

        /// <summary>Convert commands available for a UI file type.</summary>
        public static string[] GetCommandsForType(string type) => ConvertCommandMetadata.GetCommandsForType(type);

        /// <summary>Returns the product-wide default command for a homogeneous file selection.
        /// Explicit UI selections should take precedence when they remain compatible.</summary>
        public static string? GetDefaultCommandForFiles(IReadOnlyCollection<string> files) =>
            ConvertCommandMetadata.GetDefaultCommandForFiles(files);

        /// <summary>The UI file type a command belongs to (defaults to "pdf" for unknown commands).</summary>
        public static string GetFileTypeForCommand(string command) => ConvertCommandMetadata.GetFileTypeForCommand(command);

        /// <summary>Every file type any convert command accepts, used for unfiltered pickers.</summary>
        public static string[] AllSupportedExtensions => ConvertCommandMetadata.AllSupportedExtensions;

        /// <summary>File extensions a command accepts; empty when the command is unknown.
        /// For format-conversion commands this already excludes the source extensions that
        /// would make the conversion a no-op (e.g. img-to-png does not accept .png).</summary>
        public static string[] GetAllowedExtensions(string? command) => ConvertCommandMetadata.GetAllowedExtensions(command);

        /// <summary>Source extensions a command must exclude to avoid no-op conversions;
        /// empty when the command accepts everything it lists.</summary>
        public static string[] GetExcludedExtensions(string? command) => ConvertCommandMetadata.GetExcludedExtensions(command);

        /// <summary>Whether the command key maps to a known conversion.</summary>
        public static bool IsKnownCommand(string command) => ConvertCommandMetadata.IsKnownCommand(command);

        /// <summary>Minimum number of files the command requires.</summary>
        public static int GetMinFiles(string command) => ConvertCommandMetadata.GetMinFiles(command);

        /// <summary>Localization key for the command display name.</summary>
        public static string GetLabelKey(string command) => ConvertCommandMetadata.GetLabelKey(command);

        /// <summary>Predicts the output paths a command will produce for the given files.</summary>
        public static List<string> EstimateOutputs(string command, List<string> files, string? outputDirOverride = null)
        {
            return command switch
            {
                CmdMergePdf => throw new InvalidOperationException(
                    "merge-pdf output planning is owned by the application use case."),
                CmdImgMerge => throw new InvalidOperationException(
                    "img-merge output planning is owned by the application use case."),
                "img-stitch" => throw new InvalidOperationException(
                    "img-stitch output planning is owned by the application use case."),
                CmdCompressPdf => throw new InvalidOperationException(
                    "compress-pdf output planning is owned by the application use case."),
                "translate-pdf" => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + "_translated.pdf")).ToList(),
                "decrypt-pdf" => throw new InvalidOperationException(
                    "decrypt-pdf output planning is owned by the application use case."),
                "split-pdf" => throw new InvalidOperationException(
                    "split-pdf output planning is owned by the application use case."),
                CmdImg2Pdf => throw new InvalidOperationException(
                    "img2pdf output planning is owned by the application use case."),
                CmdMdToPdf => files.Select(f => Path.Combine(
                    string.IsNullOrWhiteSpace(outputDirOverride) ? ClickraStorage.GetOutputDir(f) : Path.GetFullPath(outputDirOverride),
                    Path.GetFileNameWithoutExtension(f) + ".pdf")).ToList(),
                CmdMdToWord => files.Select(f => Path.Combine(
                    string.IsNullOrWhiteSpace(outputDirOverride) ? ClickraStorage.GetOutputDir(f) : Path.GetFullPath(outputDirOverride),
                    Path.GetFileNameWithoutExtension(f) + ".docx")).ToList(),
                CmdImgCompress => throw new InvalidOperationException(
                    "img-compress output planning is owned by the application use case."),
                CmdImgToPng or CmdImgToJpg or CmdImgToWebp or CmdImgToGif or CmdImgToHeic
                    => throw new InvalidOperationException(
                        $"{command} output planning is owned by the application use case."),
                _ => files.Select(f => Path.Combine(ClickraStorage.GetOutputDir(f), Path.GetFileNameWithoutExtension(f) + ".pdf")).ToList()
            };
        }

        /// <summary>Reads the current slider level from settings (0-2). 未設定或超出範圍時採用
        /// 登錄表的預設值，所以叫位永遠一致（不再有多份換算表）。</summary>
        public static int GetPdfCompressLevel()
        {
            int level = ClickraStorage.GetSettingInt(ClickraSettings.PdfCompressImageLevel);
            if (level == 3)
            {
                return ClickraSettings.MaxPdfCompressLevel;
            }

            return ClickraSettings.IsNumericSettingInRange(ClickraSettings.PdfCompressImageLevel, level)
                ? level
                : ClickraSettings.GetDefaultInt(ClickraSettings.PdfCompressImageLevel);
        }

        /// <summary>Current PDF compression settings as a parameter dictionary.</summary>
        public static Dictionary<string, object> CompressionOptions()
        {
            int sliderLevel = GetPdfCompressLevel();
            var level = PdfCompressionOptions.FromSliderLevel(sliderLevel);
            return new Dictionary<string, object>
            {
                ["level"] = PdfCompressionOptions.ToOptionName(level),
                ["strip_fonts"] = ClickraStorage.GetSettingBool(ClickraSettings.PdfCompressStripFonts),
                ["minify_content"] = ClickraStorage.GetSettingBool(ClickraSettings.PdfCompressMinifyContent)
            };
        }

        /// <summary>Current image compression settings as a parameter dictionary: a 0-3
        /// quality level and a max long-edge dimension (0 = keep original size).</summary>
        public static Dictionary<string, object> ImageCompressionOptions()
        {
            var level = Clickra.Core.Processors.ImageCompressionOptions.FromSliderLevel(GetImageCompressLevel());
            return new Dictionary<string, object>
            {
                ["level"] = Clickra.Core.Processors.ImageCompressionOptions.ToOptionName(level),
                ["quality"] = Clickra.Core.Processors.ImageCompressionOptions.GetQuality(level),
                ["max_dimension"] = GetImageCompressMaxDimension()
            };
        }

        /// <summary>The saved image compression quality level, clamped to the declared range.</summary>
        public static int GetImageCompressLevel() =>
            ClickraSettings.ClampNumericSetting(
                ClickraSettings.ImageCompressLevel,
                ClickraStorage.GetSettingInt(ClickraSettings.ImageCompressLevel));

        /// <summary>The saved max long-edge dimension for image compression (0 = original), clamped to the declared range.</summary>
        public static int GetImageCompressMaxDimension() =>
            ClickraSettings.ClampNumericSetting(
                ClickraSettings.ImageCompressMaxDimension,
                ClickraStorage.GetSettingInt(ClickraSettings.ImageCompressMaxDimension));

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

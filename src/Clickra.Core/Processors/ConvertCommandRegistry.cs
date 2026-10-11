using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clickra.Shared;

namespace Clickra.Core.Processors;

/// <summary>Core facade for shared conversion metadata, saved image-compression option snapshots,
/// and launch-argument helpers. Command definitions are authoritative in
/// <see cref="ConvertCommandMetadata"/>.</summary>
public static class ConvertCommandRegistry
{
        /// <summary>Returns the product-wide default command for a homogeneous file selection.
        /// Explicit UI selections should take precedence when they remain compatible.</summary>
        public static string? GetDefaultCommandForFiles(IReadOnlyCollection<string> files) =>
            ConvertCommandMetadata.GetDefaultCommandForFiles(files);

        /// <summary>Every file type any convert command accepts, used for unfiltered pickers.</summary>
        public static string[] AllSupportedExtensions => ConvertCommandMetadata.AllSupportedExtensions;

        /// <summary>File extensions a command accepts; empty when the command is unknown.
        /// For format-conversion commands this already excludes the source extensions that
        /// would make the conversion a no-op (e.g. img-to-png does not accept .png).</summary>
        public static string[] GetAllowedExtensions(string? command) => ConvertCommandMetadata.GetAllowedExtensions(command);

        /// <summary>Whether the command key maps to a known conversion.</summary>
        public static bool IsKnownCommand(string command) => ConvertCommandMetadata.IsKnownCommand(command);

        /// <summary>Minimum number of files the command requires.</summary>
        public static int GetMinFiles(string command) => ConvertCommandMetadata.GetMinFiles(command);

        /// <summary>Localization key for the command display name.</summary>
        public static string GetLabelKey(string command) => ConvertCommandMetadata.GetLabelKey(command);

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
        private static int GetImageCompressLevel() =>
            ClickraSettings.ClampNumericSetting(
                ClickraSettings.ImageCompressLevel,
                ClickraStorage.GetSettingInt(ClickraSettings.ImageCompressLevel));

        /// <summary>The saved max long-edge dimension for image compression (0 = original), clamped to the declared range.</summary>
        private static int GetImageCompressMaxDimension() =>
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

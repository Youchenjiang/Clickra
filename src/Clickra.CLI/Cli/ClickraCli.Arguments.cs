using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Clickra.Core;
using Clickra.Core.Processors;

namespace Clickra
{
    partial class ClickraCli
    {
        private static int _lastProgressLineLength;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(int dwProcessId);

        const int ATTACH_PARENT_PROCESS = -1;

        private static string CurrentLanguage => ClickraStorage.GetSetting(ClickraSettings.Language);

        internal static string Loc(string key, params object[] args)
        {
            string t = Localization.T(key, CurrentLanguage);
            return args.Length > 0 ? string.Format(t, args) : t;
        }

        /// <summary>Filters out files whose extension is not allowed for the command,
        /// warning or failing depending on quiet mode.</summary>
        internal static void ValidateExtensions(List<string> files, string command, bool quiet, params string[] allowed)
        {
            var invalid = files
                .Where(f => !allowed.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            if (invalid.Count > 0)
            {
                string allowedList = string.Join(", ", allowed);
                string invalidList = string.Join("\n  ", invalid.Select(Path.GetFileName));
                string msg = Loc("cli_err_invalid_format", command, allowedList, invalidList);
                Console.WriteLine(Loc("cli_err_prefix") + msg);
                if (!quiet) ShowWarning(msg, Loc("cli_err_invalid_format_title"));
                Environment.Exit(1);
            }
        }

        /// <summary>Expands directory arguments into the files they contain (recursively for
        /// supported extensions).</summary>
        internal static List<string> ExpandDirectoryArguments(string command, IEnumerable<string> inputs)
        {
            string[] allowed = ConvertCommandRegistry.GetAllowedExtensions(command);

            var expanded = new List<string>();
            foreach (var input in inputs)
            {
                if (Directory.Exists(input) && allowed.Length > 0)
                {
                    expanded.AddRange(Directory.EnumerateFiles(input)
                        .Where(file => allowed.Contains(Path.GetExtension(file).ToLowerInvariant())));
                }
                else
                {
                    expanded.Add(input);
                }
            }
            return expanded;
        }

        /// <summary>Attaches to the parent console when launched from a terminal.</summary>
        internal static void AttachParentConsoleForCli(string[] args)
        {
            if (args.Length == 0) return;

            try
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            }
            catch { }
        }

        /// <summary>Writes an inline progress line to the console.</summary>
        static void WriteConsoleProgress(int current, int total, string message)
        {
            total = Math.Max(1, total);
            current = Math.Clamp(current, 0, total);

            int percent = (int)Math.Round(current * 100.0 / total);
            const int width = 28;
            int filled = Math.Clamp((int)Math.Round(width * percent / 100.0), 0, width);
            string bar = new string('#', filled) + new string('-', width - filled);
            string line = $"[Progress] [{bar}] {percent,3}% {message}";

            try
            {
                int consoleWidth = Console.WindowWidth;
                if (consoleWidth > 0 && line.Length >= consoleWidth)
                {
                    line = line[..Math.Max(0, consoleWidth - 1)];
                }
            }
            catch { }

            int padLength = Math.Max(_lastProgressLineLength, line.Length);
            Console.Write("\r" + line.PadRight(padLength));
            Console.Out.Flush();
            _lastProgressLineLength = line.Length;
        }

        static void FinishConsoleProgressLine()
        {
            if (_lastProgressLineLength > 0)
            {
                Console.WriteLine();
                _lastProgressLineLength = 0;
            }
        }

        internal static string? ExtractOptionValue(List<string> args, params string[] optionNames)
        {
            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                foreach (var optionName in optionNames)
                {
                    if (arg.Equals(optionName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Count)
                        {
                            Console.WriteLine(Loc("cli_err_prefix") + Loc("cli_err_option_requires_dir", optionName));
                            args.RemoveAt(i);
                            return null;
                        }

                        string value = args[i + 1];
                        args.RemoveAt(i + 1);
                        args.RemoveAt(i);
                        return value;
                    }

                    string prefix = optionName + "=";
                    if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string value = arg.Substring(prefix.Length);
                        args.RemoveAt(i);
                        return value;
                    }
                }
            }

            return null;
        }

        static void RequireMinFiles(List<string> files, string command, int min, bool quiet)
        {
            if (files.Count < min)
            {
                string msg = Loc("cli_err_min_files", command, min, files.Count);
                Console.WriteLine(Loc("cli_err_prefix") + msg);
                if (!quiet) ShowWarning(msg, Loc("cli_err_min_files_title"));
                Environment.Exit(1);
            }
        }
    }
}

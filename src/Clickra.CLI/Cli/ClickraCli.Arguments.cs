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
        private static int _lastReportedPercent = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(int dwProcessId);

        const int ATTACH_PARENT_PROCESS = -1;

        internal static string Loc(string key, params object[] args) =>
            Localization.T(key, args);

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
            if (!OperatingSystem.IsWindows() || args.Length == 0) return;

            try
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            }
            catch { }
        }

        /// <summary>Writes an inline progress line to the console with ANSI colors and Unicode bar when supported.</summary>
        static void WriteConsoleProgress(int current, int total, string message)
        {
            total = Math.Max(1, total);
            current = Math.Clamp(current, 0, total);
            int percent = (int)Math.Round(current * 100.0 / total);

            if (Console.IsOutputRedirected)
            {
                WriteRedirectedProgress(percent, message);
                return;
            }

            int consoleWidth = GetConsoleWidth();
            (string line, int plainLength) = FormatInteractiveProgress(percent, message, consoleWidth);

            int padLength = Math.Max(_lastProgressLineLength, plainLength);
            Console.Write("\r" + line + new string(' ', Math.Max(0, padLength - plainLength)));
            Console.Out.Flush();
            _lastProgressLineLength = plainLength;
        }

        static int GetConsoleWidth()
        {
            try
            {
                return Console.WindowWidth;
            }
            catch
            {
                return 0;
            }
        }

        static void WriteRedirectedProgress(int percent, string message)
        {
            if (percent != _lastReportedPercent && (percent % 25 == 0 || percent == 100))
            {
                Console.WriteLine($"[Progress] {percent,3}% {message}");
                _lastReportedPercent = percent;
            }
        }

        static (string line, int plainLength) FormatInteractiveProgress(int percent, string message, int consoleWidth)
        {
            bool useAnsi = Environment.GetEnvironmentVariable("NO_COLOR") is null;
            bool useUnicode = Console.OutputEncoding.CodePage == 65001 || !OperatingSystem.IsWindows();

            int maxLen = consoleWidth > 0 ? Math.Max(10, consoleWidth - 1) : 0;
            int barWidth = consoleWidth > 0 ? Math.Clamp(consoleWidth - 25, 0, 28) : 28;

            string bar = barWidth >= 6 ? BuildBar(percent, barWidth, useUnicode, useAnsi) : "";
            string plainBar = barWidth >= 6 ? BuildPlainBar(percent, barWidth, useUnicode) : "";

            string tag = useAnsi ? "\x1b[36m[Progress]\x1b[0m" : "[Progress]";
            string percentText = useAnsi ? $"\x1b[1m{percent,3}%\x1b[0m" : $"{percent,3}%";

            string barPrefix = string.IsNullOrEmpty(bar) ? "" : $"[{bar}] ";
            string plainBarPrefix = string.IsNullOrEmpty(plainBar) ? "" : $"[{plainBar}] ";

            string prefix = $"{tag} {barPrefix}{percentText}";
            string plainPrefix = $"[Progress] {plainBarPrefix}{percent,3}%";

            string displayMessage = message;
            if (maxLen > 0)
            {
                int availableForMsg = maxLen - plainPrefix.Length - 1;
                if (availableForMsg <= 0)
                {
                    displayMessage = "";
                }
                else if (displayMessage.Length > availableForMsg)
                {
                    displayMessage = displayMessage[..availableForMsg];
                }
            }

            string line = string.IsNullOrEmpty(displayMessage) ? prefix : $"{prefix} {displayMessage}";
            string plain = string.IsNullOrEmpty(displayMessage) ? plainPrefix : $"{plainPrefix} {displayMessage}";

            return (line, plain.Length);
        }

        static string BuildPlainBar(int percent, int width, bool useUnicode)
        {
            char filledChar = useUnicode ? '█' : '#';
            char emptyChar = useUnicode ? '░' : '-';
            int filled = Math.Clamp((int)Math.Round(width * percent / 100.0), 0, width);
            return new string(filledChar, filled) + new string(emptyChar, width - filled);
        }

        static string BuildBar(int percent, int width, bool useUnicode, bool useAnsi)
        {
            char filledChar = useUnicode ? '█' : '#';
            char emptyChar = useUnicode ? '░' : '-';
            int filled = Math.Clamp((int)Math.Round(width * percent / 100.0), 0, width);
            if (!useAnsi)
            {
                return new string(filledChar, filled) + new string(emptyChar, width - filled);
            }
            return $"\x1b[32m{new string(filledChar, filled)}\x1b[0m\x1b[90m{new string(emptyChar, width - filled)}\x1b[0m";
        }

        static void FinishConsoleProgressLine()
        {
            if (_lastProgressLineLength > 0)
            {
                Console.WriteLine();
                _lastProgressLineLength = 0;
            }
            _lastReportedPercent = -1;
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

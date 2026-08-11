using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Clickra
{
    partial class ClickraCli
    {
        private static int _lastProgressLineLength;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(int dwProcessId);

        const int ATTACH_PARENT_PROCESS = -1;

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
                string msg = $"指令\u300c{command}\u300d\u53ea\u63a5\u53d7\u4ee5\u4e0b\u683c\u5f0f\uff1a{allowedList}\n\n\u4ee5\u4e0b\u6a94\u6848\u683c\u5f0f\u4e0d\u7b26\uff0c\u5df2\u4e2d\u6b62\u57f7\u884c\uff1a\n  {invalidList}";
                Console.WriteLine("[錯誤] " + msg);
                if (!quiet) ShowWarning(msg, "Clickra — 格式錯誤");
                Environment.Exit(1);
            }
        }

        /// <summary>Expands directory arguments into the files they contain (recursively for
        /// supported extensions).</summary>
        internal static List<string> ExpandDirectoryArguments(string command, IEnumerable<string> inputs)
        {
            var allowed = command switch
            {
                "ppt2pdf" => new[] { ".pptx", ".ppt" },
                "word2pdf" => new[] { ".docx", ".doc" },
                "excel2pdf" => new[] { ".xlsx", ".xls" },
                "merge-pdf" or "translate-pdf" or "decrypt-pdf" or "compress-pdf" or "split-pdf" => new[] { ".pdf" },
                "img2pdf" or "img-merge" or "img-stitch" => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" },
                _ => Array.Empty<string>()
            };

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
                            Console.WriteLine($"[錯誤] 參數「{optionName}」需要指定資料夾。");
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
                string msg = $"指令「{command}」至少需要 {min} 個檔案，但您只傳入了 {files.Count} 個。\n\n請多選幾個檔案後，再透過「傳送到」執行。";
                Console.WriteLine("[錯誤] " + msg);
                if (!quiet) ShowWarning(msg, "Clickra — 檔案數量不足");
                Environment.Exit(1);
            }
        }
    }
}

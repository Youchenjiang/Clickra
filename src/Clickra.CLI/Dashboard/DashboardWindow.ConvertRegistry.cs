using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Clickra.Core;
using Clickra.Core.Processors;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI;

public static partial class DashboardWindow
{
    // ---- Convert command registry (single source of truth) ----
    // The dashboard "轉檔" page is fully data-driven from ConvertCommandDefs:
    // button list, group layout, localization text, file-open filters,
    // file validation rules and history tag colors are all derived here.
    // To add a new feature, add ONE entry below and nothing else.

    private const string FilterPdfFiles = "PDF Files (*.pdf)\0*.pdf\0All Files (*.*)\0*.*\0\0";
    private const string FilterWordFiles = "Word Files (*.doc; *.docx)\0*.doc;*.docx\0All Files (*.*)\0*.*\0\0";
    private const string FilterExcelFiles = "Excel Files (*.xlsx; *.xls)\0*.xlsx;*.xls\0All Files (*.*)\0*.*\0\0";
    private const string FilterPowerPointFiles = "PowerPoint Files (*.ppt; *.pptx)\0*.ppt;*.pptx\0All Files (*.*)\0*.*\0\0";
    private const string FilterImageFiles = "Image Files (*.jpg; *.jpeg; *.png; *.bmp; *.gif; *.tiff; *.webp)\0*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp\0All Files (*.*)\0*.*\0\0";

    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" };

    private sealed class ConvertCommandDef
    {
        public string Command = "";
        public string TextKey = "";
        public string Filter = "";
        public string[] Extensions = Array.Empty<string>();
        public int MinFiles = 1;
        public bool RequiresOffice = false;
        public int Group = 0; // 0: Office 轉 PDF, 1: PDF 工具, 2: 圖片工具
        public Color TagColor = Color.FromArgb(100, 100, 100);
    }

    private static readonly ConvertCommandDef[] ConvertCommandDefs = BuildCommandDefs();

    private static ConvertCommandDef[] BuildCommandDefs() => new[]
    {
        // Office 轉 PDF (Group 0)
        new ConvertCommandDef { Command = "word2pdf",      TextKey = "cmd_word_to_pdf",    Filter = FilterWordFiles,       Extensions = new[] { ".doc", ".docx" },          MinFiles = 1, RequiresOffice = true, Group = 0, TagColor = Color.FromArgb(0, 120, 212) },
        new ConvertCommandDef { Command = "excel2pdf",     TextKey = "cmd_excel_to_pdf",   Filter = FilterExcelFiles,      Extensions = new[] { ".xlsx", ".xls" },          MinFiles = 1, RequiresOffice = true, Group = 0, TagColor = Color.FromArgb(16, 124, 65) },
        new ConvertCommandDef { Command = "ppt2pdf",       TextKey = "cmd_ppt_to_pdf",     Filter = FilterPowerPointFiles, Extensions = new[] { ".ppt", ".pptx" },          MinFiles = 1, RequiresOffice = true, Group = 0, TagColor = Color.FromArgb(180, 50, 30) },

        // PDF 工具 (Group 1)
        new ConvertCommandDef { Command = "merge-pdf",     TextKey = "cmd_merge_pdf",      Filter = FilterPdfFiles,        Extensions = new[] { ".pdf" },                  MinFiles = 2, Group = 1, TagColor = Color.FromArgb(16, 124, 65) },
        new ConvertCommandDef { Command = "compress-pdf",  TextKey = "cmd_compress_pdf",   Filter = FilterPdfFiles,        Extensions = new[] { ".pdf" },                  MinFiles = 1, Group = 1, TagColor = Color.FromArgb(0, 120, 120) },
        new ConvertCommandDef { Command = "translate-pdf", TextKey = "cmd_translate_pdf",  Filter = FilterPdfFiles,        Extensions = new[] { ".pdf" },                  MinFiles = 1, Group = 1, TagColor = Color.FromArgb(138, 43, 226) },
        new ConvertCommandDef { Command = "decrypt-pdf",   TextKey = "cmd_decrypt_pdf",    Filter = FilterPdfFiles,        Extensions = new[] { ".pdf" },                  MinFiles = 1, Group = 1, TagColor = Color.FromArgb(0, 150, 136) },
        new ConvertCommandDef { Command = "split-pdf",     TextKey = "cmd_split_pdf",      Filter = FilterPdfFiles,        Extensions = new[] { ".pdf" },                  MinFiles = 1, Group = 1, TagColor = Color.FromArgb(0, 188, 212) },

        // 圖片工具 (Group 2)
        new ConvertCommandDef { Command = "img2pdf",       TextKey = "cmd_img_to_pdf",     Filter = FilterImageFiles,      Extensions = ImageExtensions,                   MinFiles = 1, Group = 2, TagColor = Color.FromArgb(100, 60, 180) },
        new ConvertCommandDef { Command = "img-merge",     TextKey = "cmd_merge_img",      Filter = FilterImageFiles,      Extensions = ImageExtensions,                   MinFiles = 2, Group = 2, TagColor = Color.FromArgb(0, 130, 135) },
        new ConvertCommandDef { Command = "img-stitch",    TextKey = "cmd_stitch_img",     Filter = FilterImageFiles,      Extensions = ImageExtensions,                   MinFiles = 2, Group = 2, TagColor = Color.FromArgb(216, 59, 1) },
    };

    // Derived state — keep these names so existing layout / hit-test / paint code keeps working.
    private static readonly ConvertCommand[] ConvertCommands = BuildCommands(ConvertCommandDefs);
    private static readonly int[] ConvertCommandGroupSizes = BuildGroupSizes(ConvertCommandDefs);
    private static readonly Dictionary<string, ConvertCommand> ConvertCommandByKey = BuildCommandLookup(ConvertCommands);

    private static ConvertCommand[] BuildCommands(ConvertCommandDef[] defs)
        => defs.OrderBy(d => d.Group).Select(d => new ConvertCommand(d)).ToArray();

    private static int[] BuildGroupSizes(ConvertCommandDef[] defs)
    {
        int maxGroup = defs.Max(d => d.Group);
        var sizes = new int[maxGroup + 1];
        foreach (var d in defs)
            sizes[d.Group]++;
        return sizes;
    }

    private static Dictionary<string, ConvertCommand> BuildCommandLookup(ConvertCommand[] commands)
    {
        var map = new Dictionary<string, ConvertCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in commands)
            map[c.Command] = c;
        return map;
    }

    /// <summary>A dashboard convert feature: registry metadata plus its execution behavior.</summary>
    private sealed class ConvertCommand
    {
        private readonly ConvertCommandDef _def;

        public ConvertCommand(ConvertCommandDef def) => _def = def;

        public string Command => _def.Command;
        public string TextKey => _def.TextKey;
        public string[] Extensions => _def.Extensions;
        public int MinFiles => _def.MinFiles;
        public bool RequiresOffice => _def.RequiresOffice;
        public int Group => _def.Group;
        public Color TagColor => _def.TagColor;

        /// <summary>The localized button label.</summary>
        public string DisplayName => GetText(_def.TextKey);

        public string GetOpenFilter()
            => _def.Filter.Length > 0 ? _def.Filter : FilterImageFiles;

        /// <summary>Validates that the selected files are usable for this command, returning
        /// the translated error message when not.</summary>
        public bool ValidateFiles(List<string> files, out string errorMsg)
        {
            errorMsg = "";
            if (files.Count == 0) return true;

            var invalid = files.Where(f => !Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
            if (invalid.Count > 0)
            {
                errorMsg = GetText("convert_err_invalid_ext");
                return false;
            }

            if (files.Count < MinFiles)
            {
                errorMsg = string.Format(GetText("convert_err_min_files"), MinFiles);
                return false;
            }

            return true;
        }

        /// <summary>Whether a usable Office engine (Microsoft or LibreOffice) is available for this command.</summary>
        public bool HasAvailableEngine()
        {
            string engine = ClickraStorage.GetSetting("OfficeEngine");
            bool libreOfficeReady = !string.IsNullOrWhiteSpace(LibreOfficeHelper.GetResolvedExecutablePath());

            if (engine.Equals("libreoffice", StringComparison.OrdinalIgnoreCase))
                return libreOfficeReady;

            string app = Command switch
            {
                "ppt2pdf" => "PowerPoint",
                "word2pdf" => "Word",
                "excel2pdf" => "Excel",
                _ => ""
            };
            bool microsoftReady = !string.IsNullOrWhiteSpace(app) && IsOfficeInstalled(app);

            return engine.Equals("microsoft", StringComparison.OrdinalIgnoreCase)
                ? microsoftReady
                : microsoftReady || libreOfficeReady;
        }

        /// <summary>Activates the given command on the convert tab, clearing incompatible
        /// selections.</summary>
        public static void Select(ConvertCommand command)
        {
            _convertCommandIndex = Array.IndexOf(ConvertCommands, command);
            if (_selectedFiles.Count > 0 && !command.ValidateFiles(_selectedFiles, out _))
            {
                _selectedFiles.Clear();
            }
        }

        /// <summary>Runs the given command for the currently selected files and switches
        /// to the history tab.</summary>
        public static void Run(ConvertCommand command, IntPtr hwnd)
        {
            if (_selectedFiles.Count == 0) return;

            if (!command.ValidateFiles(_selectedFiles, out string error))
            {
                MessageBox(hwnd, error, "Clickra", 0x30);
                return;
            }

            if (command.RequiresOffice && !command.HasAvailableEngine())
            {
                string language = ClickraStorage.GetSetting("Language");
                string engine = ClickraStorage.GetSetting("OfficeEngine");
                string errorKey = "";
                if (engine.Equals("libreoffice", StringComparison.OrdinalIgnoreCase))
                {
                    errorKey = "error_libreoffice_not_ready";
                }
                else if (engine.Equals("microsoft", StringComparison.OrdinalIgnoreCase))
                {
                    errorKey = "error_microsoftoffice_not_ready";
                }
                else
                {
                    errorKey = "setting_engine_none_available";
                }
                MessageBox(hwnd, Localization.T(errorKey, language), "Clickra", 0x30);
                return;
            }

            var filesCopy = new List<string>(_selectedFiles);
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    ProgressWindow.Show(command.Command, filesCopy);
                }
                catch (Exception ex)
                {
                    MessageBox(IntPtr.Zero, $"Execution failed: {ex.Message}", "Clickra", 0x10);
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();

            _selectedFiles.Clear();

            _activeTab = 2; // Switch to History
            RefreshHistoryData();
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Clickra.UI
{
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

        private static readonly ConvertCommandDef[] ConvertCommandDefs =
        {
            // Office 轉 PDF (Group 0)
            new() { Command = "word2pdf",      TextKey = "cmd_word_to_pdf",    Filter = FilterWordFiles,       Extensions = new[] { ".doc", ".docx" },          MinFiles = 1, RequiresOffice = true, Group = 0, TagColor = Color.FromArgb(0, 120, 212) },
            new() { Command = "excel2pdf",     TextKey = "cmd_excel_to_pdf",   Filter = FilterExcelFiles,      Extensions = new[] { ".xlsx", ".xls" },          MinFiles = 1, RequiresOffice = true, Group = 0, TagColor = Color.FromArgb(16, 124, 65) },
            new() { Command = "ppt2pdf",       TextKey = "cmd_ppt_to_pdf",     Filter = FilterPowerPointFiles, Extensions = new[] { ".ppt", ".pptx" },          MinFiles = 1, RequiresOffice = true, Group = 0, TagColor = Color.FromArgb(180, 50, 30) },

            // PDF 工具 (Group 1)
            new() { Command = "merge-pdf",     TextKey = "cmd_merge_pdf",      Filter = FilterPdfFiles,        Extensions = new[] { ".pdf" },                  MinFiles = 2, Group = 1, TagColor = Color.FromArgb(16, 124, 65) },
            new() { Command = "compress-pdf",  TextKey = "cmd_compress_pdf",   Filter = FilterPdfFiles,        Extensions = new[] { ".pdf" },                  MinFiles = 1, Group = 1, TagColor = Color.FromArgb(0, 120, 120) },
            new() { Command = "translate-pdf", TextKey = "cmd_translate_pdf",  Filter = FilterPdfFiles,        Extensions = new[] { ".pdf" },                  MinFiles = 1, Group = 1, TagColor = Color.FromArgb(138, 43, 226) },
            new() { Command = "decrypt-pdf",   TextKey = "cmd_decrypt_pdf",    Filter = FilterPdfFiles,        Extensions = new[] { ".pdf" },                  MinFiles = 1, Group = 1, TagColor = Color.FromArgb(0, 150, 136) },
            new() { Command = "split-pdf",     TextKey = "cmd_split_pdf",      Filter = FilterPdfFiles,        Extensions = new[] { ".pdf" },                  MinFiles = 1, Group = 1, TagColor = Color.FromArgb(0, 188, 212) },

            // 圖片工具 (Group 2)
            new() { Command = "img2pdf",       TextKey = "cmd_img_to_pdf",     Filter = FilterImageFiles,      Extensions = ImageExtensions,                   MinFiles = 1, Group = 2, TagColor = Color.FromArgb(100, 60, 180) },
            new() { Command = "img-merge",     TextKey = "cmd_merge_img",      Filter = FilterImageFiles,      Extensions = ImageExtensions,                   MinFiles = 2, Group = 2, TagColor = Color.FromArgb(0, 130, 135) },
            new() { Command = "img-stitch",    TextKey = "cmd_stitch_img",     Filter = FilterImageFiles,      Extensions = ImageExtensions,                   MinFiles = 2, Group = 2, TagColor = Color.FromArgb(216, 59, 1) },
        };

        // Derived state — keep these names so existing layout / hit-test / paint code keeps working.
        private static readonly string[] ConvertCommands = BuildCommandList(ConvertCommandDefs);
        private static readonly int[] ConvertCommandGroupSizes = BuildGroupSizes(ConvertCommandDefs);
        private static readonly Dictionary<string, ConvertCommandDef> ConvertCommandByKey = BuildCommandLookup(ConvertCommandDefs);

        private static string[] BuildCommandList(ConvertCommandDef[] defs)
            => defs.OrderBy(d => d.Group).Select(d => d.Command).ToArray();

        private static int[] BuildGroupSizes(ConvertCommandDef[] defs)
        {
            int maxGroup = defs.Max(d => d.Group);
            var sizes = new int[maxGroup + 1];
            foreach (var d in defs)
                sizes[d.Group]++;
            return sizes;
        }

        private static Dictionary<string, ConvertCommandDef> BuildCommandLookup(ConvertCommandDef[] defs)
        {
            var map = new Dictionary<string, ConvertCommandDef>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in defs)
                map[d.Command] = d;
            return map;
        }
    }
}

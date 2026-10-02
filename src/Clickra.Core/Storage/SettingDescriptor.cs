using System;
using System.Collections.Generic;
using System.Linq;

namespace Clickra.Core;
    /// <summary>
    /// 設定控制項之編輯器種類。UI 生成引擎根據此欄位推導對應的控制項。
    /// </summary>
    public enum SettingEditorKind
    {
        /// <summary>布林切換開關（true / false）。</summary>
        Toggle,

        /// <summary>多選一下拉或按鈕組（從宣告的選項中挑選一項）。</summary>
        Choice,

        /// <summary>整數離散滑桿（依據 NumericSettingRange 劃分等距刻度與標籤）。</summary>
        Slider,

        /// <summary>數值輸入框 / 步進計數器（可設定上下限與步進值）。</summary>
        Number,
    }

    /// <summary>Choice 編輯器中的一個選項定義。</summary>
    public sealed record SettingOption(string Value, string LabelKey, string? FallbackText = null);

    /// <summary>設定群組/分類之描述定義。</summary>
    public sealed record SettingGroupDescriptor(
        string Key,
        string TitleKey,
        string? DescriptionKey = null,
        int Order = 0
    );

    /// <summary>
    /// 一個設定項在設定頁中的完整介面規格描述，由 Core 定義作為單一真實來源。
    /// 新增設定時只要在此登錄，兩端介面（Fluent 與 CLI Dashboard）即可由它自動推導與生成對應控制項。
    /// </summary>
    public sealed record SettingDescriptor(
        string Key,
        string GroupKey,
        SettingEditorKind EditorKind,
        string TitleKey,
        string? DescriptionKey = null,
        IReadOnlyList<SettingOption>? Options = null,
        NumericSettingRange? NumericRange = null,
        IReadOnlyList<string>? SliderLabels = null,
        int Order = 0
    )
    {
        /// <summary>取得有效的數值範圍宣告；若未自訂則由 ClickraSettings.NumericRanges 自動推導。</summary>
        public NumericSettingRange? GetEffectiveNumericRange()
        {
            if (NumericRange.HasValue) return NumericRange.Value;
            if (ClickraSettings.TryGetNumericRange(Key, out var r)) return r;
            return null;
        }

        /// <summary>取得離散滑桿之總刻度數。</summary>
        public int GetSliderStops()
        {
            var range = GetEffectiveNumericRange();
            return range.HasValue ? Math.Max(2, range.Value.Max - range.Value.Min + 1) : 2;
        }

        /// <summary>取得此設定的預設字串值。</summary>
        public string GetDefaultValue() => ClickraSettings.GetDefault(Key);
    }

    /// <summary>
    /// 設定頁登錄表：集中管理所有使用者介面可見之設定描述與群組分類。
    /// Fluent UI 與 CLI Dashboard 均以它為單一來源自動生成控制項。
    /// </summary>
    public static class SettingPageRegistry
    {
        private const string TraditionalChineseOptionText = "繁體中文 (zh-TW)";
        private const string SimplifiedChineseOptionText = "简体中文 (zh-CN)";

        // ─── 群組識別常數 ───────────────────────────────────────────────────────
        public const string GroupGeneral = "general";
        public const string GroupOffice = "office";
        public const string GroupBehavior = "behavior";
        public const string GroupPdf = "pdf";
        public const string GroupPdfCompression = "pdf_compression";
        public const string GroupImageCompression = "image_compression";
        public const string GroupTaskQueue = "task_queue";

        /// <summary>設定群組清單，已依推薦順序排序。</summary>
        public static readonly IReadOnlyList<SettingGroupDescriptor> Groups = new SettingGroupDescriptor[]
        {
            new(GroupGeneral, "fluent_output_dir", "fluent_output_dir_desc", 10),
            new(GroupOffice, "fluent_office_engine", "fluent_office_engine_desc", 20),
            new(GroupBehavior, "fluent_behavior", null, 30),
            new(GroupPdf, "fluent_pdf_target", null, 40),
            new(GroupPdfCompression, "fluent_pdf_compression", "setting_pdf_compress_desc", 50),
            new(GroupImageCompression, "fluent_image_compression", "fluent_images_desc", 60),
            new(GroupTaskQueue, "setting_parked_ttl_title", "setting_parked_ttl_desc", 70),
        };

        /// <summary>
        /// 全量設定介面描述項。新增設定時於此處宣告，Fluent 與 CLI 即刻自動渲染對應控制項。
        /// </summary>
        public static readonly IReadOnlyList<SettingDescriptor> AllDescriptors = new SettingDescriptor[]
        {
            // Behavior: 靜默模式與通知開關
            new(
                ClickraSettings.QuietMode,
                GroupBehavior,
                SettingEditorKind.Toggle,
                "setting_silent_title",
                "setting_silent_desc",
                Order: 10
            ),
            new(
                ClickraSettings.Notification,
                GroupBehavior,
                SettingEditorKind.Toggle,
                "setting_notify_title",
                "setting_notify_desc",
                Order: 20
            ),

            // PDF Compression: PDF 壓縮等級、字型剝離、內容極小化
            new(
                ClickraSettings.PdfCompressImageLevel,
                GroupPdfCompression,
                SettingEditorKind.Slider,
                "setting_pdf_compress_title",
                "setting_pdf_compress_desc",
                SliderLabels: new[] { "setting_pdf_compress_level_small", "setting_pdf_compress_level_std", "setting_pdf_compress_level_high" },
                Order: 10
            ),
            new(
                ClickraSettings.PdfCompressStripFonts,
                GroupPdfCompression,
                SettingEditorKind.Toggle,
                "setting_pdf_compress_strip_fonts",
                null,
                Order: 20
            ),
            new(
                ClickraSettings.PdfCompressMinifyContent,
                GroupPdfCompression,
                SettingEditorKind.Toggle,
                "setting_pdf_compress_minify_content",
                null,
                Order: 30
            ),

            // Image Compression: 圖片壓縮品質等級、長邊像素限制
            new(
                ClickraSettings.ImageCompressLevel,
                GroupImageCompression,
                SettingEditorKind.Slider,
                "setting_image_compress_title",
                "setting_image_compress_desc",
                SliderLabels: new[] { "setting_image_level_min", "setting_image_level_small", "setting_image_level_std", "setting_image_level_high" },
                Order: 10
            ),
            new(
                ClickraSettings.ImageCompressMaxDimension,
                GroupImageCompression,
                SettingEditorKind.Number,
                "setting_image_max_dimension_title",
                "setting_image_max_dimension_desc",
                Order: 20
            ),

            // Task Queue: 暫存任務保留天數
            new(
                ClickraSettings.ParkedTaskRetention,
                GroupTaskQueue,
                SettingEditorKind.Number,
                "setting_parked_ttl_title",
                "setting_parked_ttl_desc",
                Order: 10
            ),

            // General: 輸出路徑模式與介面語言
            new(
                ClickraSettings.OutputDir,
                GroupGeneral,
                SettingEditorKind.Choice,
                "setting_output_title",
                "setting_output_desc",
                Options: new SettingOption[]
                {
                    new(ClickraSettings.DefaultOutputDirSource, "setting_output_same_as_source"),
                    new(ClickraSettings.OutputDirDesktop, "setting_output_desktop"),
                    new(ClickraSettings.OutputDirDownloads, "setting_output_downloads"),
                    new("custom", "setting_output_custom"),
                },
                Order: 10
            ),
            new(
                ClickraSettings.Language,
                GroupGeneral,
                SettingEditorKind.Choice,
                "setting_lang_title",
                "setting_lang_desc",
                Options: new SettingOption[]
                {
                    new("zh-TW", TraditionalChineseOptionText, TraditionalChineseOptionText),
                    new("zh-CN", SimplifiedChineseOptionText, SimplifiedChineseOptionText),
                    new("en-US", "English (en-US)", "English (en-US)"),
                    new("ja-JP", "日本語 (ja-JP)", "日本語 (ja-JP)"),
                    new("ko-KR", "한국어 (ko-KR)", "한국어 (ko-KR)"),
                },
                Order: 20
            ),

            // Office: 轉檔引擎
            new(
                ClickraSettings.OfficeEngine,
                GroupOffice,
                SettingEditorKind.Choice,
                "setting_engine_title",
                "setting_engine_desc",
                Options: new SettingOption[]
                {
                    new(ClickraSettings.DefaultOfficeEngineAuto, "setting_engine_auto"),
                    new(ClickraSettings.OfficeEngineMicrosoft, "setting_engine_microsoft"),
                    new(ClickraSettings.OfficeEngineLibreOffice, "setting_engine_libreoffice"),
                },
                Order: 10
            ),

            // PDF: 翻譯目標語言
            new(
                ClickraSettings.TranslateTargetLang,
                GroupPdf,
                SettingEditorKind.Choice,
                "setting_pdf_title",
                "setting_pdf_desc",
                Options: new SettingOption[]
                {
                    new("zh-TW", TraditionalChineseOptionText, TraditionalChineseOptionText),
                    new("en", "English (en)", "English (en)"),
                    new("zh-CN", SimplifiedChineseOptionText, SimplifiedChineseOptionText),
                    new("ja", "日本語 (ja)", "日本語 (ja)"),
                    new("ko", "한국어 (ko)", "한국어 (ko)"),
                },
                Order: 10
            ),
        };

        private static readonly Dictionary<string, SettingDescriptor> ByKey =
            AllDescriptors.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, SettingGroupDescriptor> GroupByKey =
            Groups.ToDictionary(g => g.Key, StringComparer.OrdinalIgnoreCase);

        /// <summary>嘗試依照鍵名取得設定介面描述項。</summary>
        public static bool TryGetDescriptor(string key, out SettingDescriptor? descriptor) =>
            ByKey.TryGetValue(key, out descriptor);

        /// <summary>依照鍵名取得設定介面描述項；若未註冊則回傳 null。</summary>
        public static SettingDescriptor? GetDescriptor(string key) =>
            ByKey.TryGetValue(key, out var d) ? d : null;

        /// <summary>嘗試依照群組鍵取得群組描述項。</summary>
        public static bool TryGetGroup(string key, out SettingGroupDescriptor? group) =>
            GroupByKey.TryGetValue(key, out group);

        /// <summary>取得指定群組下的所有設定描述項（已按 Order 排序）。</summary>
        public static IReadOnlyList<SettingDescriptor> GetDescriptorsByGroup(string groupKey) =>
            AllDescriptors.Where(d => string.Equals(d.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase))
                          .OrderBy(d => d.Order)
                          .ToList();

        /// <summary>是否在設定頁描述表中已註冊此鍵。</summary>
        public static bool IsDescriptorRegistered(string key) =>
            !string.IsNullOrWhiteSpace(key) && ByKey.ContainsKey(key);
    }

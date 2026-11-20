using System;
using System.Collections.Generic;
using System.Linq;
using Clickra.Core.Modules.Sys.Settings.Model;

namespace Clickra.Core.Storage;

/// <summary>一個設定的完整定義：鍵、預設值與用途說明。</summary>
public sealed record ClickraSetting(string Key, string Default, string Description);

/// <summary>
/// 所有使用者設定的單一登錄表。設定的「鍵」與「預設值」只在這裡定義：
/// <see cref="ClickraStorage.GetSetting"/> 用它補預設，讀取端一律引用這裡的常數，
/// 不得自行寫死鍵或預設值（由 SettingsRegistryTests 守門）。
///
/// 新增設定時：在下面加一個常數、在 All 加一列，然後才能使用。
/// </summary>
public static class ClickraSettings
{
    // ─── 介面與輸出 ────────────────────────────────────────────────────────
    public const string Language = "Language";
    public const string OutputDir = "OutputDir";
    public const string QuietMode = "QuietMode";
    public const string Notification = "Notification";

    // ─── Office 引擎 ───────────────────────────────────────────────────────
    public const string OfficeEngine = "OfficeEngine";
    public const string LibreOfficePath = "LibreOfficePath";
    public const string LibreOfficeRemovalPendingRestart = "LibreOfficeRemovalPendingRestart";
    public const string LibreOfficeInstalledByClickra = "LibreOfficeInstalledByClickra";
    public const string LibreOfficeManagedProductCode = "LibreOfficeManagedProductCode";
    public const string LibreOfficeManagedSofficePath = "LibreOfficeManagedSofficePath";

    // ─── PDF ───────────────────────────────────────────────────────────────
    public const string TranslateTargetLang = "TranslateTargetLang";
    public const string TranslateEngine = "TranslateEngine";
    public const string TranslateAiEndpoint = "TranslateAiEndpoint";
    public const string TranslateAiApiKey = "TranslateAiApiKey";
    public const string TranslateAiModel = "TranslateAiModel";
    public const string PdfCompressImageLevel = "PdfCompressImageLevel";
    public const string PdfCompressStripFonts = "PdfCompressStripFonts";
    public const string PdfCompressMinifyContent = "PdfCompressMinifyContent";

    // ─── 圖片壓縮 ──────────────────────────────────────────────────────────
    public const string ImageCompressLevel = "ImageCompressLevel";
    public const string ImageCompressMaxDimension = "ImageCompressMaxDimension";

    // ─── 任務佇列 ──────────────────────────────────────────────────────────
    public const string ParkedTaskRetention = "ParkedTaskRetention";

    // ─── 功能預設值（4.0 桌面端設定頁 › 功能預設值）──────────────────────
    public const string PdfToImageFormat = "PdfToImageFormat";
    public const string MarkdownTheme = "MarkdownTheme";
    public const string MarkdownPaper = "MarkdownPaper";
    public const string MarkdownTextSize = "MarkdownTextSize";
    public const string MarkdownTemplatePath = "MarkdownTemplatePath";
    public const string MarkdownCodeTheme = "MarkdownCodeTheme";
    public const string PdfToImageDpi = "PdfToImageDpi";
    public const string PdfToImageStitch = "PdfToImageStitch";
    public const string PrintPrinter = "PrintPrinter";
    public const string PrintCopies = "PrintCopies";
    public const string PrintDuplex = "PrintDuplex";
    public const string PrintPaper = "PrintPaper";
    public const string PrintOrientation = "PrintOrientation";
    public const string PrintFitToPage = "PrintFitToPage";
    public const string PrintDarkenText = "PrintDarkenText";

    // ─── 4.0 桌面端外觀與視窗 ─────────────────────────────────────────────
    public const string DesktopTheme = "DesktopTheme";
    public const string DesktopWindowBounds = "DesktopWindowBounds";

    // ─── 檔案總管右鍵選單 ──────────────────────────────────────────────────
    // 右鍵選單擴充（ClickraShell.dll）不能參照 Core，鍵值定義在相依性為零的 Shared 檔案，這裡引用同一個常數。
    public const string ContextMenuEnabled = Clickra.Shared.ContextMenuVisibility.EnabledKey;
    public const string ContextMenuHidden = Clickra.Shared.ContextMenuVisibility.HiddenKey;

    // ─── 預設值常數（讀取端需要以程式比較時引用這些，不要重寫字串）──────────
    public const string DefaultEmpty = "";
    public const string ValueTrue = "true";
    public const string ValueFalse = "false";
    public const string DefaultOutputDirSource = "source";
    public const string DefaultOfficeEngineAuto = "auto";
    public const string DefaultTranslateTargetLang = "zh-TW";
    public const string DefaultTranslateEngine = "google";
    public const string DefaultTranslateAiEndpoint = "https://api.openai.com/v1";
    public const string DefaultTranslateAiApiKey = "";
    public const string DefaultTranslateAiModel = "gpt-4o-mini";
    public const string DefaultPdfCompressLevel = "1";
    public const string DefaultImageCompressLevel = "1";
    public const string DefaultImageCompressMaxDimension = "0";
    public const string DefaultParkedTaskRetention = "7";
    public const string DefaultPdfToImageFormat = "png";
    public const string DefaultMarkdownTheme = "default";
    public const string DefaultMarkdownPaper = "a4";
    public const string DefaultMarkdownTextSize = "standard";
    public const string MarkdownThemeWordTemplate = "template";
    public const string DefaultMarkdownCodeTheme = "dark";
    public const string DefaultPdfToImageDpi = "150";
    public const string DefaultPrintCopies = "1";
    public const string DefaultPrintDuplex = "Simplex";
    public const string DefaultPrintOrientation = "Auto";
    public const string DesktopThemeLight = "light";
    public const string DesktopThemeDark = "dark";
    public const string DesktopThemeSystem = "system";
    public const int MaxParkedTaskRetentionDays = 365;
    public const int MinParkedRetentionDays = 0;
    public const int MaxParkedRetentionDays = MaxParkedTaskRetentionDays;
    public const int ParkedRetentionStepDays = 7;
    public const int MinPdfCompressLevel = 0;
    public const int MaxPdfCompressLevel = 2;
    public const int MinImageCompressLevel = 0;
    public const int MaxImageCompressLevel = 3;
    public const int MinImageCompressMaxDimension = 0;
    public const int MaxImageCompressMaxDimension = int.MaxValue;
    public const int MinPdfToImageDpi = 72;
    public const int MaxPdfToImageDpi = 600;
    public const int MinPrintCopies = 1;
    public const int MaxPrintCopies = 99;

    // ─── 設定值的列舉字彙 ─────────────────────────────────────────────────
    public const string OutputDirDesktop = "desktop";
    public const string OutputDirDownloads = "downloads";
    public const string OfficeEngineMicrosoft = "microsoft";
    public const string OfficeEngineLibreOffice = "libreoffice";
    public const string TranslateEngineGoogle = "google";
    public const string TranslateEngineCustomAi = "custom-ai";

    /// <summary>登錄表本體：每個設定恰好一列，鍵不重複。</summary>
    public static readonly IReadOnlyList<ClickraSetting> All = new[]
    {
        new ClickraSetting(Language, DefaultEmpty, "介面語言代碼（空 = 跟隨系統）"),
        new ClickraSetting(OutputDir, DefaultOutputDirSource, $"輸出資料夾：{DefaultOutputDirSource} / {OutputDirDesktop} / {OutputDirDownloads} / 自訂路徑"),
        new ClickraSetting(QuietMode, ValueFalse, "靜默模式：不顯示進度視窗"),
        new ClickraSetting(Notification, ValueTrue, "轉換完成時顯示通知"),
        new ClickraSetting(OfficeEngine, DefaultOfficeEngineAuto, $"Office 轉檔引擎：{DefaultOfficeEngineAuto} / {OfficeEngineMicrosoft} / {OfficeEngineLibreOffice}"),
        new ClickraSetting(LibreOfficePath, DefaultEmpty, "LibreOffice 執行檔路徑（空 = 自動偵測）"),
        new ClickraSetting(LibreOfficeRemovalPendingRestart, ValueFalse, "LibreOffice 已排程移除，等待 Windows 重新啟動"),
        new ClickraSetting(LibreOfficeInstalledByClickra, ValueFalse, "目前的系統 LibreOffice 是否已明確交由 Clickra 管理"),
        new ClickraSetting(LibreOfficeManagedProductCode, DefaultEmpty, "Clickra 目前獲授權管理的 LibreOffice MSI ProductCode"),
        new ClickraSetting(LibreOfficeManagedSofficePath, DefaultEmpty, "Clickra 目前獲授權管理的 LibreOffice soffice.exe 路徑"),
        new ClickraSetting(TranslateTargetLang, DefaultTranslateTargetLang, "PDF 翻譯目標語言"),
        new ClickraSetting(TranslateEngine, DefaultTranslateEngine, $"PDF 翻譯引擎：{TranslateEngineGoogle} / {TranslateEngineCustomAi}"),
        new ClickraSetting(TranslateAiEndpoint, DefaultTranslateAiEndpoint, "自訂 AI 翻譯 API 端點"),
        new ClickraSetting(TranslateAiApiKey, DefaultTranslateAiApiKey, "自訂 AI 翻譯 API 金鑰"),
        new ClickraSetting(TranslateAiModel, DefaultTranslateAiModel, "自訂 AI 翻譯模型名稱"),
        new ClickraSetting(PdfCompressImageLevel, DefaultPdfCompressLevel, "PDF 圖片壓縮等級 0-2（1 = 平衡）"),
        new ClickraSetting(PdfCompressStripFonts, ValueFalse, "壓縮 PDF 時移除嵌入字型"),
        new ClickraSetting(PdfCompressMinifyContent, ValueTrue, "壓縮 PDF 時最佳化結構"),
        new ClickraSetting(ImageCompressLevel, DefaultImageCompressLevel, "圖片壓縮品質等級 0-3"),
        new ClickraSetting(ImageCompressMaxDimension, DefaultImageCompressMaxDimension, "圖片壓縮最大長邊（0 = 保持原尺寸）"),
        new ClickraSetting(ParkedTaskRetention, DefaultParkedTaskRetention, "已暫存任務保留天數（0 = 無限期）"),
        new ClickraSetting(PdfToImageFormat, DefaultPdfToImageFormat, "PDF 轉成圖片的預設格式：png / jpg / webp"),
        new ClickraSetting(MarkdownTheme, DefaultMarkdownTheme, "Markdown 轉換的預設文件範本：default / minimal / academic"),
        new ClickraSetting(MarkdownPaper, DefaultMarkdownPaper, "Markdown 轉成 PDF 的預設紙張：a4 / letter"),
        new ClickraSetting(MarkdownTextSize, DefaultMarkdownTextSize, "Markdown 轉換的預設文字大小：small / standard / large"),
        new ClickraSetting(MarkdownTemplatePath, DefaultEmpty, $"Markdown 預設 Word 範本的路徑（MarkdownTheme 為 {MarkdownThemeWordTemplate} 時使用）"),
        new ClickraSetting(MarkdownCodeTheme, DefaultMarkdownCodeTheme, "Markdown 轉換的預設程式碼區塊配色：dark / light"),
        new ClickraSetting(PdfToImageDpi, DefaultPdfToImageDpi, "PDF 轉成圖片的預設解析度（DPI）"),
        new ClickraSetting(PdfToImageStitch, ValueFalse, "PDF 轉成圖片時預設把所有頁面接成一張長圖"),
        new ClickraSetting(PrintPrinter, DefaultEmpty, "列印的預設印表機名稱（空 = 系統預設印表機）"),
        new ClickraSetting(PrintCopies, DefaultPrintCopies, "列印的預設份數"),
        new ClickraSetting(PrintDuplex, DefaultPrintDuplex, "列印的預設雙面：Simplex / DuplexLongEdge / DuplexShortEdge"),
        new ClickraSetting(PrintPaper, DefaultEmpty, "列印的預設紙張名稱（空 = 跟隨印表機）"),
        new ClickraSetting(PrintOrientation, DefaultPrintOrientation, "列印的預設方向：Auto / Portrait / Landscape"),
        new ClickraSetting(PrintFitToPage, ValueTrue, "列印時預設縮放至紙張大小"),
        new ClickraSetting(PrintDarkenText, ValueFalse, "列印時預設加深淺色文字"),
        new ClickraSetting(DesktopTheme, DesktopThemeLight, $"4.0 桌面端外觀：{DesktopThemeLight} / {DesktopThemeDark} / {DesktopThemeSystem}"),
        new ClickraSetting(DesktopWindowBounds, DefaultEmpty, "4.0 桌面端上次的視窗位置與大小（x,y,寬,高,是否最大化；空 = 預設）"),
        new ClickraSetting(ContextMenuEnabled, ValueTrue, "在檔案總管右鍵選單顯示 Clickra"),
        new ClickraSetting(ContextMenuHidden, DefaultEmpty, "右鍵選單中隱藏的功能（指令名稱，以逗號分隔；空 = 全部顯示）"),
    };

    private static readonly Dictionary<string, ClickraSetting> ByKey =
        All.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>該鍵是否已在登錄表中註冊。</summary>
    public static bool IsRegistered(string key) =>
        !string.IsNullOrWhiteSpace(key) && ByKey.ContainsKey(key);

    /// <summary>已註冊鍵的預設值；未註冊的鍵回傳空字串。</summary>
    public static string GetDefault(string key) =>
        !string.IsNullOrWhiteSpace(key) && ByKey.TryGetValue(key, out ClickraSetting? setting)
            ? setting.Default
            : DefaultEmpty;

    /// <summary>以整數解讀已註冊鍵的預設值；無法解析時回傳 0。</summary>
    public static int GetDefaultInt(string key) =>
        int.TryParse(GetDefault(key), out int value) ? value : 0;

    /// <summary>以布林解讀已註冊鍵的預設值（只有 "true" 為真）。</summary>
    public static bool GetDefaultBool(string key) =>
        GetDefault(key).Equals(ValueTrue, StringComparison.OrdinalIgnoreCase);

    /// <summary>所有整數/數值設定的合法範圍宣告（單一真實來源）。</summary>
    public static readonly IReadOnlyDictionary<string, NumericSettingRange> NumericRanges =
        new Dictionary<string, NumericSettingRange>(StringComparer.OrdinalIgnoreCase)
        {
            [ParkedTaskRetention] = new(MinParkedRetentionDays, MaxParkedRetentionDays, 7),
            [PdfCompressImageLevel] = new(MinPdfCompressLevel, MaxPdfCompressLevel, 1),
            [ImageCompressLevel] = new(MinImageCompressLevel, MaxImageCompressLevel, 1),
            [ImageCompressMaxDimension] = new(MinImageCompressMaxDimension, MaxImageCompressMaxDimension, 0),
            [PdfToImageDpi] = new(MinPdfToImageDpi, MaxPdfToImageDpi, 150),
            [PrintCopies] = new(MinPrintCopies, MaxPrintCopies, 1),
        };

    /// <summary>取得指定數值設定的合法範圍宣告；若未註冊則回傳 false。</summary>
    public static bool TryGetNumericRange(string key, out NumericSettingRange range) =>
        NumericRanges.TryGetValue(key, out range);

    /// <summary>取得指定數值設定的合法範圍宣告；若未註冊則擲出 KeyNotFoundException。</summary>
    public static NumericSettingRange GetNumericRange(string key) =>
        NumericRanges.TryGetValue(key, out var range)
            ? range
            : throw new KeyNotFoundException($"'{key}' is not registered as a numeric setting with a defined range.");

    /// <summary>將數值限制在指定設定的合法範圍內；若非數值設定則直接回傳原值。</summary>
    public static int ClampNumericSetting(string key, int value) =>
        NumericRanges.TryGetValue(key, out var range) ? range.Clamp(value) : value;

    /// <summary>
    /// Normalizes the persisted PDF compression slider value. Level 3 is a legacy duplicate
    /// of the current High stop; every other out-of-range value falls back to the registered default.
    /// </summary>
    public static int NormalizePdfCompressLevel(int value)
    {
        if (value == 3)
        {
            return ClickraSettings.MaxPdfCompressLevel;
        }

        return ClickraSettings.IsNumericSettingInRange(ClickraSettings.PdfCompressImageLevel, value)
            ? value
            : ClickraSettings.GetDefaultInt(ClickraSettings.PdfCompressImageLevel);
    }

    /// <summary>指定數值是否在設定的合法範圍內。</summary>
    public static bool IsNumericSettingInRange(string key, int value) =>
        NumericRanges.TryGetValue(key, out var range) && range.IsInRange(value);

    /// <summary>
    /// 已退役的舊設定鍵。LoadSettings 時若遇到這些鍵，會自動從記憶體中丟棄並重寫設定檔，
    /// 避免廢棄設定永久殘留在使用者的 settings.conf。
    /// </summary>
    public static readonly IReadOnlyCollection<string> RetiredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "PdfCompressTargetDpi",
        "PdfCompressJpegQuality",
        "PdfCompressDpi",
    };

    /// <summary>該鍵是否已被標記為退役廢棄。</summary>
    public static bool IsRetired(string key) =>
        !string.IsNullOrWhiteSpace(key) && RetiredKeys.Contains(key);
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Clickra.Core;

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

    // ─── PDF ───────────────────────────────────────────────────────────────
    public const string TranslateTargetLang = "TranslateTargetLang";
    public const string PdfCompressImageLevel = "PdfCompressImageLevel";
    public const string PdfCompressStripFonts = "PdfCompressStripFonts";
    public const string PdfCompressMinifyContent = "PdfCompressMinifyContent";

    // ─── 圖片壓縮 ──────────────────────────────────────────────────────────
    public const string ImageCompressLevel = "ImageCompressLevel";
    public const string ImageCompressMaxDimension = "ImageCompressMaxDimension";

    // ─── 任務佇列 ──────────────────────────────────────────────────────────
    public const string ParkedTaskRetention = "ParkedTaskRetention";

    // ─── 預設值常數（讀取端需要以程式比較時引用這些，不要重寫字串）──────────
    public const string DefaultEmpty = "";
    public const string ValueTrue = "true";
    public const string ValueFalse = "false";
    public const string DefaultOutputDirSource = "source";
    public const string DefaultOfficeEngineAuto = "auto";
    public const string DefaultTranslateTargetLang = "zh-TW";
    public const string DefaultPdfCompressLevel = "1";
    public const string DefaultImageCompressLevel = "1";
    public const string DefaultImageCompressMaxDimension = "0";
    public const string DefaultParkedTaskRetention = "7";

    // ─── 設定值的列舉字彙 ─────────────────────────────────────────────────
    public const string OutputDirDesktop = "desktop";
    public const string OutputDirDownloads = "downloads";
    public const string OfficeEngineMicrosoft = "microsoft";
    public const string OfficeEngineLibreOffice = "libreoffice";

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
        new ClickraSetting(LibreOfficeInstalledByClickra, ValueFalse, "這台機器上的 LibreOffice 是否由 Clickra 安裝"),
        new ClickraSetting(TranslateTargetLang, DefaultTranslateTargetLang, "PDF 翻譯目標語言"),
        new ClickraSetting(PdfCompressImageLevel, DefaultPdfCompressLevel, "PDF 圖片壓縮等級 0-2（1 = 平衡）"),
        new ClickraSetting(PdfCompressStripFonts, ValueFalse, "壓縮 PDF 時移除嵌入字型"),
        new ClickraSetting(PdfCompressMinifyContent, ValueTrue, "壓縮 PDF 時最佳化結構"),
        new ClickraSetting(ImageCompressLevel, DefaultImageCompressLevel, "圖片壓縮品質等級 0-3"),
        new ClickraSetting(ImageCompressMaxDimension, DefaultImageCompressMaxDimension, "圖片壓縮最大長邊（0 = 保持原尺寸）"),
        new ClickraSetting(ParkedTaskRetention, DefaultParkedTaskRetention, "已暫存任務保留天數（0 = 無限期）"),
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

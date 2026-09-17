using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Clickra.Core;

namespace Clickra.Core.Tests;

/// <summary>
/// 在地化鍵的「歸屬」守門，兩個方向各一條：
///
/// 1. 已宣告的鍵都必須有人消費——否則它是一條永遠不會出現在畫面上的死翻譯，五種語言
///    的翻譯成本已經付了，卻沒有任何使用者看得到。
/// 2. 被查詢的鍵都必須已宣告——否則 <c>T()</c> 會原樣回傳鍵名，使用者看到的是
///    "task_parked_title" 這種字串。
///
/// 孤兒鍵基線只能單向縮小。新增孤兒鍵會讓測試失敗；把某個鍵接上 UI（或連同翻譯刪除）
/// 之後也必須同步把基線條目刪掉，否則「基線曾經是對的」會掩蓋「現在已經不準了」。這
/// 正是過去孤兒鍵無聲累積到四十多條的原因。
/// </summary>
static partial class TestSuite
{
    private static readonly TimeSpan LocalizationRegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>孤兒鍵基線目前條數。基線只能單向縮小。</summary>
    public static int UnconsumedKeyBaselineCount => UnconsumedKeyBaseline.Length;

    /// <summary>
    /// 孤兒鍵基線水位硬上限。任何 PR 若試圖擴大基線，將直接被此上限及 Git 基準比對擋下。
    /// 只能隨著鍵被消費或刪除而單向調低，絕不可調高。
    /// </summary>
    public const int BaselineCeiling = 33;

    /// <summary>
    /// 目前仍沒有消費者的鍵。每一條都必須有一條明確的出路：接上某個介面，或連同五種
    /// 語言的翻譯一起刪除。分組只是為了讓稽核有跡可循，不是豁免理由。
    /// </summary>
    private static readonly string[] UnconsumedKeyBaseline =
    {
        // 引擎／PDF 設定：這些曾被設定頁與錯誤對話框使用，現在同一個位置改由 Core 的
        // 預設表或別的鍵呈現，字串本身已經沒有讀取端。
        // setting_pdf_compress_group_image／_other 已接上設定頁分組標題。
        "engine_pdf", "engine_ppt", "engine_word", "engine_excel", "engine_libreoffice",
        "setting_libreoffice_optional", "error_processing_failed",

        // Fluent 檔案類型／拖放白名單：描述一套以檔案類型過濾拖放內容的介面。
        "fluent_remove_file", "fluent_file_type", "fluent_file_type_pdf", "fluent_file_type_word",
        "fluent_file_type_ppt", "fluent_file_type_excel",
        "fluent_file_type_image", "fluent_drop_title_for_type",
        "fluent_drop_types_only", "fluent_files_skipped_type", "fluent_files_removed_type",
        "fluent_github",

        // Fluent 進度頁：TaskProgressPage 目前只用 fluent_progress_* 其中的一部分。
        "fluent_progress_running_title", "fluent_progress_output",
        "fluent_progress_waiting", "fluent_progress_done_footer", "fluent_progress_failed_footer",
        "fluent_progress_file_not_found",

        // Fluent 任務佇列／帳本：排隊與暫存任務的看板文案。
        "fluent_task_queue_title", "fluent_task_queue_running",
        "fluent_task_ledger_title", "fluent_task_ledger_empty", "fluent_task_view",

        // 視覺化分割器：pdf_split_prompt 從未被接上，pdf_split_pages_per_segment 在 N
        // 步進器改用 pdf_split_pages_n 之後失效。兩者的模型側已由
        // TestSuite.VisualSplitter.cs 單獨守住。
        // 圖片壓縮／編解碼器：圖片壓縮設定頁與 Codec 商店的文案。這批是進行中的圖片
        // 轉檔／壓縮功能的字串，接上介面之後就該從基線移除。
        "codec_missing_store_prompt", "codec_heif_extension_name",
        "codec_missing_install_action"
    };

    /// <summary>
    /// 在地化鍵的位置：CLI 進度視窗的一行提示（36..484 邏輯像素、12px 字）。換算成半形
    /// 字元寬度約為 448 / 6。
    /// </summary>
    private const int ProgressTipLineHalfWidthUnits = 74;

    /// <summary>
    /// 不得出現在可翻譯文案裡的介面字符：破折號與箭頭是「某個按鈕」的圖示，而圖示會
    /// 隨繪製它的控制項改變。原本的 progress_tray_hint 正是把視窗標題列的「—」寫進
    /// 句子裡，才會在接上 CLI 時發現句子描述的是另一個介面。
    /// </summary>
    private static readonly char[] UiGlyphsInCopy = { '—', '–', '―', '↘', '↙', '→', '←', '↺', '✕', '✖' };

    /// <summary>
    /// 會把第一個字串引數當成在地化鍵的存取子：Fluent 的 <c>L</c>、CLI 的 <c>Loc</c> 與
    /// dashboard 的 <c>GetText</c>、Core 的 <c>T</c> 及 <c>Localization.T</c>。四者都在
    /// src/ 有自己的定義，由測試下面自行檢查，改名不會讓守門靜默失效。
    /// </summary>
    private static readonly Regex LocalizationLookupPattern = new(
        @"(?:\bL|\bLoc|\bGetText|\bT)\(\s*""([A-Za-z0-9_]+)""",
        RegexOptions.Compiled,
        LocalizationRegexTimeout);

    public static void RegisterLocalizationOrphanTests(TestRunner runner)
    {
        runner.Run("Localization guard: every declared key has a consumer (shrinking orphan baseline)", TestEveryDeclaredKeyHasConsumer);
        runner.Run("Localization guard: orphan baseline monotonically shrinking invariants and helpers", TestOrphanBaselineMonotonicInvariants);
        runner.Run("Localization guard: every localization lookup names a declared key", TestEveryLookupNamesDeclaredKey);
        runner.Run("Localization guard: the CLI progress tip is glyph-free and fits one tip line", TestProgressTipFitsOneLine);
    }

    private static void TestEveryDeclaredKeyHasConsumer()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

        Assert.True(UnconsumedKeyBaseline.Length == UnconsumedKeyBaseline.Distinct(StringComparer.Ordinal).Count(),
            "The unconsumed-key baseline must not list the same key twice.");

        Assert.True(UnconsumedKeyBaseline.Length <= BaselineCeiling,
            $"The orphan baseline may only shrink, never grow. Current count ({UnconsumedKeyBaseline.Length}) " +
            $"exceeds the hard ceiling of {BaselineCeiling}. New unconsumed keys must not be added to the baseline.");

        VerifyBaselineMonotonicallyShrinksAgainstGit(root);
        WriteBaselineReport(UnconsumedKeyBaseline.Length);

        var keys = Localization.GetAllKeys();
        Assert.True(keys.Count > 0, "Expected Localization to declare at least one key.");

        string localizationPath = Path.Combine(root, "src", "Clickra.Core", "Localization", "Localization.cs");
        Assert.True(File.Exists(localizationPath), $"Expected the localization source to exist: {localizationPath}");

        var searchable = new StringBuilder(StripDeclarations(StripComments(File.ReadAllText(localizationPath))));
        string srcDir = Path.Combine(root, "src");
        foreach (string file in EnumerateSources(srcDir))
        {
            if (string.Equals(file, localizationPath, StringComparison.OrdinalIgnoreCase)) continue;
            searchable.Append('\n').Append(StripComments(File.ReadAllText(file)));
        }

        string searchableText = searchable.ToString();
        var consumed = keys.Where(k => searchableText.Contains('"' + k + '"', StringComparison.Ordinal)).ToList();
        var orphans = keys.Where(k => !consumed.Contains(k, StringComparer.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(consumed.Count > 0, "Expected at least one localization key to be consumed by src/.");

        string expected = string.Join(", ", UnconsumedKeyBaseline.OrderBy(k => k, StringComparer.Ordinal));
        string actual = string.Join(", ", orphans);
        Assert.True(string.Equals(expected, actual, StringComparison.Ordinal),
            "The unconsumed-key baseline is stale. Every key still without a consumer must be wired " +
            "up, or deleted together with its 5 translations, and then dropped from " +
            "UnconsumedKeyBaseline - the list may only shrink." +
            $"{Environment.NewLine}  baseline: {expected}{Environment.NewLine}  actual:   {actual}");
    }

    private static void TestOrphanBaselineMonotonicInvariants()
    {
        Assert.True(BaselineCeiling == UnconsumedKeyBaseline.Length,
            $"BaselineCeiling ({BaselineCeiling}) must exactly match UnconsumedKeyBaseline.Length ({UnconsumedKeyBaseline.Length}). " +
            "When removing consumed or deleted keys from the baseline, BaselineCeiling must be lowered synchronously.");

        string sampleCode = "private static readonly string[] UnconsumedKeyBaseline = { \"key_one\", \"key_two\" };";
        var extracted = ExtractBaselineKeys(sampleCode);
        Assert.True(extracted.Contains("key_one") && extracted.Contains("key_two") && extracted.Count == 2,
            "ExtractBaselineKeys must correctly parse keys from source code snippet.");

        var mockBase = new HashSet<string> { "key_one" };
        var mockCurrent = new HashSet<string> { "key_one", "key_injected" };
        var mockAdded = mockCurrent.Where(k => !mockBase.Contains(k)).ToList();
        Assert.True(mockAdded.Count == 1 && mockAdded[0] == "key_injected",
            "Monotonic shrinking detector must flag any key not present in the base branch baseline.");
    }

    private static void TestEveryLookupNamesDeclaredKey()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");

        var declared = new HashSet<string>(Localization.GetAllKeys(), StringComparer.Ordinal);
        Assert.True(declared.Count > 0, "Expected Localization to declare at least one key.");

        string srcDir = Path.Combine(root, "src");
        string sourceText = string.Concat(EnumerateSources(srcDir)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select(f => StripComments(File.ReadAllText(f))));

        foreach (string accessor in new[] { "L", "Loc", "GetText", "T" })
        {
            Assert.True(Regex.IsMatch(sourceText, $@"static string {accessor}\(string", RegexOptions.None, LocalizationRegexTimeout),
                $"The '{accessor}' localization accessor must still be defined in src/.");
        }

        var violations = new List<string>();
        foreach (string file in EnumerateSources(srcDir).Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            string text = StripComments(File.ReadAllText(file));
            foreach (Match match in LocalizationLookupPattern.Matches(text))
            {
                string key = match.Groups[1].Value;
                if (declared.Contains(key)) continue;
                int line = text.Substring(0, match.Index).Count(c => c == '\n') + 1;
                violations.Add($"{Path.GetRelativePath(root, file).Replace('\\', '/')}:{line}: {key}");
            }
        }

        Assert.True(violations.Count == 0,
            "Every key handed to a localization lookup must exist in the dictionaries; T() echoes an " +
            "undeclared key to the user verbatim. Wire it to a declared key or declare the missing " +
            $"translation in all 5 languages ({violations.Count} site(s)):{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static void TestProgressTipFitsOneLine()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException("Could not locate the repository root from the test output directory.");
        string paintCode = File.ReadAllText(Path.Combine(root, "src", "Clickra.CLI", "Progress", "ProgressWindow.Paint.cs"));
        Assert.True(paintCode.Contains("Loc(\"progress_tray_hint\")", StringComparison.Ordinal),
            "The in-progress state must render progress_tray_hint, otherwise the sentence is unreachable copy.");

        foreach (string lang in Localization.SupportedLanguages)
        {
            string tip = Localization.T("progress_tray_hint", lang);

            Assert.False(string.IsNullOrWhiteSpace(tip),
                $"progress_tray_hint must have a translation for '{lang}'.");
            Assert.False(string.Equals(tip, "progress_tray_hint", StringComparison.Ordinal),
                $"progress_tray_hint has no dictionary entry for '{lang}'.");

            int glyphIndex = tip.IndexOfAny(UiGlyphsInCopy);
            string glyph = glyphIndex >= 0 ? $"'{tip[glyphIndex]}'" : "(none)";
            Assert.True(glyphIndex < 0,
                $"progress_tray_hint for '{lang}' embeds the UI glyph {glyph} ('{tip}'). Name the action, " +
                "not the button, so the sentence stays true for whichever control draws it.");

            int units = tip.Sum(c => IsFullWidth(c) ? 2 : 1);
            Assert.True(units <= ProgressTipLineHalfWidthUnits,
                $"progress_tray_hint for '{lang}' needs {units} halfwidth units but the CLI tip line " +
                $"fits {ProgressTipLineHalfWidthUnits} ('{tip}').");
        }
    }
    /// <summary>Every .cs and .xaml file under the given directory, excluding build output.</summary>
    private static IEnumerable<string> EnumerateSources(string sourceDir)
    {
        Assert.True(Directory.Exists(sourceDir), $"Expected the source directory to exist: {sourceDir}");
        return Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Removes every declaration site from the localization source, so only real lookups
    /// remain: the two literal table forms, and keys declared through a const.</summary>
    private static string StripDeclarations(string localizationSource)
    {
        string stripped = Regex.Replace(localizationSource, @"\[""[A-Za-z0-9_]+""\]\s*=", " = ", RegexOptions.None, LocalizationRegexTimeout);
        // The tuple tables hold (Key, Tw, Cn, En, Ja, Ko); the first value is sometimes a const.
        stripped = Regex.Replace(stripped, @"^(\s*)\(""[A-Za-z0-9_]+"",", "$1(\"", RegexOptions.Multiline, LocalizationRegexTimeout);
        return Regex.Replace(stripped, @"private const string [A-Za-z0-9_]+ = ""[A-Za-z0-9_]+"";", string.Empty, RegexOptions.None, LocalizationRegexTimeout);
    }

    /// <summary>Whether the character occupies a full-width cell: CJK, kana, Hangul and the
    /// full-width forms block.</summary>
    private static bool IsFullWidth(char c) =>
        (c >= '\u1100' && c <= '\u115F') ||
        (c >= '\u2E80' && c <= '\uA4CF') ||
        (c >= '\uAC00' && c <= '\uD7A3') ||
        (c >= '\uF900' && c <= '\uFAFF') ||
        (c >= '\uFE30' && c <= '\uFE6F') ||
        (c >= '\uFF00' && c <= '\uFF60');

    /// <summary>Removes block and line comments so a key mentioned in prose is not mistaken
    /// for a consumer.</summary>
    private static string StripComments(string text)
    {
        string stripped = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline, LocalizationRegexTimeout);
        stripped = Regex.Replace(stripped, @"<!--.*?-->", string.Empty, RegexOptions.Singleline, LocalizationRegexTimeout);
        return Regex.Replace(stripped, @"//[^\n]*", string.Empty, RegexOptions.None, LocalizationRegexTimeout);
    }

    /// <summary>
    /// 驗證當前基線相較於 Git 基準分支（origin/main 或 main）只能縮小，絕不可增加任何 Main 沒有的鍵。
    /// </summary>
    private static void VerifyBaselineMonotonicallyShrinksAgainstGit(string repoRoot)
    {
        string? baseContent = TryGetGitFileContent(repoRoot, "origin/main", "tests/Clickra.Core.Tests/TestSuite.LocalizationOrphans.cs")
                           ?? TryGetGitFileContent(repoRoot, "main", "tests/Clickra.Core.Tests/TestSuite.LocalizationOrphans.cs");

        if (string.IsNullOrEmpty(baseContent)) return;

        var baseKeys = ExtractBaselineKeys(baseContent);
        if (baseKeys.Count == 0) return;

        var currentKeys = new HashSet<string>(UnconsumedKeyBaseline, StringComparer.Ordinal);
        var addedKeys = currentKeys.Where(k => !baseKeys.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(addedKeys.Count == 0,
            $"PR blocked: New unconsumed key(s) detected in baseline that were not present on base branch ({string.Join(", ", addedKeys)}). " +
            "New localization keys must have active consumers in src/, and the orphan baseline may only shrink.");

        Assert.True(UnconsumedKeyBaseline.Length <= baseKeys.Count,
            $"The orphan baseline count ({UnconsumedKeyBaseline.Length}) cannot exceed base branch count ({baseKeys.Count}).");
    }

    /// <summary>從指定 Git 版本取得檔案內容；若非 Git 環境或該分支不存在則優雅回傳 null。</summary>
    private static string? TryGetGitFileContent(string repoRoot, string revision, string relativePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"show {revision}:{relativePath.Replace('\\', '/')}",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return proc.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>從原始碼中解析 UnconsumedKeyBaseline 陣列內宣告的鍵清單。</summary>
    private static HashSet<string> ExtractBaselineKeys(string fileContent)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var match = Regex.Match(fileContent, @"UnconsumedKeyBaseline\s*=\s*\{(?<content>.*?)\};", RegexOptions.Singleline, LocalizationRegexTimeout);
        if (!match.Success) return keys;

        string arrayBody = match.Groups["content"].Value;
        foreach (Match m in Regex.Matches(arrayBody, @"""([A-Za-z0-9_]+)""", RegexOptions.None, LocalizationRegexTimeout))
        {
            keys.Add(m.Groups[1].Value);
        }
        return keys;
    }

    /// <summary>將基線條數報告輸出至控制台，並在 CI 環境寫入 GITHUB_STEP_SUMMARY。</summary>
    private static void WriteBaselineReport(int currentCount)
    {
        string reportLine = $"[Localization] Orphan baseline: {currentCount} keys remaining (ceiling: {BaselineCeiling}, monotonic shrinking)";
        Console.WriteLine(reportLine);

        string? summaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrEmpty(summaryFile))
        {
            try
            {
                string markdown = Environment.NewLine +
                    "### 🌐 Localization Orphan Baseline Report" + Environment.NewLine +
                    $"- **Current Unconsumed Keys**: `{currentCount}`" + Environment.NewLine +
                    $"- **Baseline Hard Ceiling**: `{BaselineCeiling}`" + Environment.NewLine +
                    "- **Shrinking Policy**: `Enforced` (PRs adding new unconsumed keys are automatically blocked)" + Environment.NewLine;
                File.AppendAllText(summaryFile, markdown);
            }
            catch
            {
                // Best-effort in CI environments
            }
        }
    }
}

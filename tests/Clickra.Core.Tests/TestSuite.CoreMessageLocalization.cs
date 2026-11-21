using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Clickra.Core;

namespace Clickra.Core.Tests;

/// <summary>
/// Guards for the progress and error messages that Core hands to every window
/// (docs/specs/v4_localization_plan.md section 8, step 2, and docs/specs/ui_writing_guide.md):
/// processors look their text up in the locale files instead of writing Chinese inline, and each
/// message names its parameters the same way in all five languages.
/// </summary>
static partial class TestSuite
{
    /// <summary>Folders whose code reports progress or failures to the user.</summary>
    private static readonly string[] CoreMessageSourceDirs =
    {
        "Modules/Pdf/Compression/Processing",
        "Modules/Pdf/Decrypt/Processing",
        "Modules/Pdf/Merge/Processing",
        "Modules/Pdf/Split/Processing",
        "Modules/Pdf/ToImage/Processing",
        "Modules/Pdf/Print/Processing",
        "Modules/Img/Compression/Processing",
        "Modules/Img/FormatConvert/Processing",
        "Modules/Img/Stitch/Processing",
        "Modules/Img/ToPdf/Processing",
        "Modules/Img/Merge/Processing",
        "Modules/Doc/MarkdownPdf/Processing",
        "Modules/Doc/MarkdownWord/Processing",
        "Modules/Doc/Shared/Infrastructure",
    };

    private static readonly TimeSpan CoreMessageRegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Locale key families that Core uses for progress and error messages.</summary>
    private static readonly Regex CoreMessageKey = new(
        @"^(?:core_|md_progress_|pdf_err_|pdf_error_|pdf_progress_|status_office_|status_libreoffice_|error_heic_|error_image_|error_img_|error_office_|error_libreoffice_|error_microsoftoffice_|img_compress_progress_)|^(?:pdf_not_encrypted|setting_engine_none_available)$",
        RegexOptions.Compiled, CoreMessageRegexTimeout);

    private static readonly Regex PositionalParameter = new(@"\{\d+\}", RegexOptions.Compiled, CoreMessageRegexTimeout);

    public static void RegisterCoreMessageLocalizationTests(TestRunner runner)
    {
        runner.RunGuard("Core messages: processors hold no hard-coded Chinese, Japanese or Korean", GuardCoreProcessorsHaveNoHardCodedText);
        runner.RunGuard("Core messages: texts use the same named parameters in every language", GuardCoreMessageParametersMatch);
        runner.RunGuard("Core messages: zh-TW texts use the agreed wording", GuardCoreMessagesUseAgreedWording);
    }

    private static IEnumerable<string> CoreMessageKeys() =>
        Localization.GetAllKeys().Where(k => CoreMessageKey.IsMatch(k)).OrderBy(k => k, StringComparer.Ordinal);

    private static void GuardCoreProcessorsHaveNoHardCodedText()
    {
        string? root = FindRepoRoot();
        if (root is null) throw new TestSkippedException(RepositoryRootMissingMessage);

        var hits = new List<string>();
        foreach (string dir in CoreMessageSourceDirs)
        {
            string full = Path.Combine(root, "src", "Clickra.Core", dir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full)) continue;
            foreach (string file in Directory.EnumerateFiles(full, "*.cs", SearchOption.TopDirectoryOnly))
            {
                using var reader = new StringReader(WithoutComments(File.ReadAllText(file)));
                string? currentLine;
                while ((currentLine = reader.ReadLine()) != null)
                {
                    if (DesktopCjk.IsMatch(currentLine))
                    {
                        string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                        hits.Add($"{rel}: {currentLine.Trim()}");
                    }
                }
            }
        }

        Assert.True(hits.Count == 0,
            "Progress and error messages belong in the locale files, looked up with the task's language " +
            $"(ExecutionLocalizationOptions.Text or Localization.Format). {hits.Count} line(s):{Environment.NewLine}" +
            string.Join(Environment.NewLine, hits.Take(30)));
    }

    private static void GuardCoreMessageParametersMatch()
    {
        var keys = CoreMessageKeys().ToList();
        Assert.True(keys.Count > 60, $"Expected Core's message keys in the locale files, found {keys.Count}.");

        var missing = Localization.FindMissingTranslations(keys);
        Assert.True(missing.Count == 0,
            "Every Core message needs a non-empty text in all five languages:" + Environment.NewLine +
            Localization.FormatMissingReport(missing));

        var problems = new List<string>();
        foreach (string key in keys)
        {
            var reference = Parameters(Localization.T(key, "zh-TW"));
            foreach (string lang in Localization.SupportedLanguages)
            {
                string text = Localization.T(key, lang);
                if (PositionalParameter.IsMatch(text))
                    problems.Add($"{key} [{lang}] uses a numbered parameter; name it instead: \"{text}\"");
                var actual = Parameters(text);
                if (!actual.SetEquals(reference))
                    problems.Add($"{key} [{lang}]: {{{string.Join("}, {", actual)}}} vs zh-TW {{{string.Join("}, {", reference)}}}");
            }
        }
        Assert.True(problems.Count == 0,
            "Core messages name their parameters ({name}, {page}, ...) the same way in every language " +
            "(ui_writing_guide.md section 4):" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    private static void GuardCoreMessagesUseAgreedWording()
    {
        var problems = new List<string>();
        foreach (string key in CoreMessageKeys())
        {
            string tw = Localization.T(key, "zh-TW");
            foreach (string word in RetiredTraditionalChineseWords.Where(w => tw.Contains(w, StringComparison.Ordinal)))
                problems.Add($"{key} [zh-TW] uses \"{word}\", which ui_writing_guide.md section 2 replaced: \"{tw}\"");
        }
        Assert.True(problems.Count == 0,
            "Core messages follow the same wording as the window (ui_writing_guide.md section 2):" +
            Environment.NewLine + string.Join(Environment.NewLine, problems));
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core.Modules.PdfTranslation.Translation;

internal class MyMemoryTranslator : ITranslationEngine
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    public string Name => "mymemory";

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        int retries = 2;
        int delayMs = 500;
        while (true)
        {
            try
            {
                return await TranslateInternalAsync(text, targetLanguage, cancellationToken);
            }
            // Rate limits and daily quotas surface to the batch runner so it handles
            // the wait, countdown, and retry in one place without stalling.
            catch (Exception httpException) // skipcq: CS-R1008, CS-R1084
                when (
                    retries > 0 &&
                    !cancellationToken.IsCancellationRequested &&
                    !TranslationProviderLimits.IsLimit(httpException))
            {
                retries--;
                await Task.Delay(delayMs, cancellationToken);
                delayMs = Math.Min(delayMs * 2, 3000);
            }
        }
    }

    private const string Endpoint = "https://api.mymemory.translated.net/get";

    private static async Task<string> TranslateInternalAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        if (text.All(c => !char.IsLetter(c))) return text;

        string normalizedTarget = NormalizeLanguageCode(targetLanguage);
        string? sourceLang = DetectSourceLanguage(text, normalizedTarget);
        if (sourceLang == null)
        {
            throw new NotSupportedException($"Unable to infer source language for translation to '{targetLanguage}'.");
        }

        if (string.Equals(sourceLang, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            return text;

        string langPair = $"{sourceLang}|{normalizedTarget}";
        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("q", text),
            new KeyValuePair<string, string>("langpair", langPair)
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = formContent
        };
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 Clickra/1.0");
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new TranslationRateLimitException(
                "MyMemory rate limited the request.",
                TranslationProviderLimits.ReadRetryAfter(response, TranslationProviderLimits.DefaultRetryAfter));
        }
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseResponse(json, text);
    }

    /// <summary>
    /// Builds the request without a shared "de" contact address. MyMemory may
    /// count an e-mail's quota across every install that sends it, so each user
    /// keeps the anonymous quota of their own connection instead.
    /// </summary>
    internal static Uri BuildRequestUri(string text, string targetLanguage)
    {
        string normalizedTarget = NormalizeLanguageCode(targetLanguage);
        string? sourceLang = DetectSourceLanguage(text, normalizedTarget);
        string langPair = Uri.EscapeDataString($"{sourceLang ?? "en"}|{normalizedTarget}");
        string query = Uri.EscapeDataString(text);
        return new Uri($"{Endpoint}?q={query}&langpair={langPair}");
    }

    /// <summary>
    /// Infers the source language from character sets to avoid sending an
    /// invalid language pair (such as en|en when translating CJK to English)
    /// or misclassifying Japanese/Korean/Chinese text as English.
    /// </summary>
    internal static string? DetectSourceLanguage(string text, string normalizedTarget)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (text.Any(IsJapaneseKana)) return "ja";
        if (text.Any(IsKoreanHangul)) return "ko";
        if (text.Any(IsCjkIdeograph)) return ResolveCjkVariant(normalizedTarget);
        if (text.Any(IsCyrillic)) return "ru";
        if (text.Any(IsGreek)) return "el";
        if (text.Any(IsArabic)) return "ar";
        if (text.Any(char.IsAsciiLetter)) return "en";

        return null;
    }

    private static bool IsJapaneseKana(char c) => c is >= '\u3040' and <= '\u30ff';
    private static bool IsKoreanHangul(char c) => c is >= '\uac00' and <= '\ud7af';
    private static bool IsCjkIdeograph(char c) => c is >= '\u4e00' and <= '\u9fff' or >= '\u3400' and <= '\u4dbf';
    private static bool IsCyrillic(char c) => c is >= '\u0400' and <= '\u04ff';
    private static bool IsGreek(char c) => c is >= '\u0370' and <= '\u03ff';
    private static bool IsArabic(char c) => c is >= '\u0600' and <= '\u06ff';

    private static string ResolveCjkVariant(string normalizedTarget) =>
        normalizedTarget switch
        {
            var t when t.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) => "zh-CN",
            var t when t.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) => "zh-TW",
            _ => "zh"
        };

    /// <summary>
    /// Reads a MyMemory reply. When the free quota is used up MyMemory still
    /// answers with HTTP 200 and puts its warning where the translation belongs,
    /// so the warning must be rejected here rather than rendered into the PDF.
    /// </summary>
    internal static string ParseResponse(string json, string sourceText)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? translated = null;
        bool hasTranslatedText = false;
        if (root.TryGetProperty("responseData", out var responseData) &&
            responseData.ValueKind == JsonValueKind.Object &&
            responseData.TryGetProperty("translatedText", out var translatedText))
        {
            hasTranslatedText = true;
            translated = translatedText.GetString();
        }

        bool quotaFinished = root.TryGetProperty("quotaFinished", out var quotaElement) &&
            quotaElement.ValueKind == JsonValueKind.True;
        if (quotaFinished || (translated != null &&
            TranslationResultQualityGuard.IsProviderQuotaNotice(translated) &&
            !TranslationResultQualityGuard.IsProviderQuotaNotice(sourceText)))
            throw new TranslationQuotaExceededException("MyMemory's free daily quota is used up.");

        int? status = root.TryGetProperty("responseStatus", out var statusElement) ? ReadStatus(statusElement) : null;
        if (status == 429)
            throw new TranslationRateLimitException("MyMemory rate limited the request.", TranslationProviderLimits.DefaultRetryAfter);
        if (status is int code && code != 200)
            throw new InvalidOperationException($"MyMemory returned status {code}.");

        if (hasTranslatedText)
            return translated ?? sourceText;

        throw new InvalidOperationException("Invalid MyMemory response structure.");
    }

    private static int? ReadStatus(JsonElement status) => status.ValueKind switch
    {
        JsonValueKind.Number when status.TryGetInt32(out int number) => number,
        JsonValueKind.String when int.TryParse(status.GetString(), out int parsed) => parsed,
        _ => null
    };

    public async Task<List<string>> TranslateBatchAsync(List<string> texts, string targetLanguage, CancellationToken cancellationToken)
    {
        var results = new List<string>();
        if (texts == null || texts.Count == 0) return results;

        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await TranslateAsync(text, targetLanguage, cancellationToken));
            await Task.Delay(150, cancellationToken);
        }

        return results;
    }

    private static string NormalizeLanguageCode(string code) => LanguageCodeHelper.Normalize(code);
}

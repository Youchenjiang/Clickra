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
            catch (TranslationRateLimitException rateLimitException) when (!cancellationToken.IsCancellationRequested)
            {
                if (retries <= 0)
                    throw new TranslationRateLimitException(
                        "MyMemory rate limit remained active after bounded retries.",
                        rateLimitException.RetryAfter,
                        rateLimitException);

                retries--;
                TimeSpan retryDelay = TranslationProviderLimits.Clamp(rateLimitException.RetryAfter);
                await Task.Delay(retryDelay, cancellationToken);
                delayMs = Math.Min(Math.Max(delayMs * 2, (int)retryDelay.TotalMilliseconds), 30000);
            }
            // An exhausted daily quota is final for today: retrying only burns time.
            catch (Exception httpException) when (
                !cancellationToken.IsCancellationRequested &&
                httpException is not TranslationQuotaExceededException) // skipcq: CS-R1008
            {
                if (retries <= 0)
                {
                    throw new InvalidOperationException(
                        "MyMemory HTTP translation failed after bounded retries.",
                        httpException);
                }
                retries--;
            }

            await Task.Delay(delayMs, cancellationToken);
            delayMs = Math.Min(delayMs * 2, 3000);
        }
    }

    private static async Task<string> TranslateInternalAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(text, targetLanguage));
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
        string langPair = Uri.EscapeDataString($"en|{NormalizeLanguageCode(targetLanguage)}");
        string query = Uri.EscapeDataString(text);
        return new Uri($"https://api.mymemory.translated.net/get?q={query}&langpair={langPair}");
    }

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

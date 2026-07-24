using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core;

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
                    throw new InvalidOperationException("MyMemory rate limit remained active after bounded retries.", rateLimitException);

                retries--;
                int retryDelayMs = (int)Math.Clamp(rateLimitException.RetryAfter.TotalMilliseconds, 1000, 30000);
                await Task.Delay(retryDelayMs, cancellationToken);
                delayMs = Math.Min(Math.Max(delayMs * 2, retryDelayMs), 30000);
            }
            catch (Exception httpException) when (!cancellationToken.IsCancellationRequested) // skipcq: CS-R1008
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
        string langPair = Uri.EscapeDataString($"en|{NormalizeLanguageCode(targetLanguage)}");
        string query = Uri.EscapeDataString(text);
        string url = $"https://api.mymemory.translated.net/get?q={query}&langpair={langPair}&de=clickra@yandex.com";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 Clickra/1.0");
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if ((int)response.StatusCode == 429)
        {
            TimeSpan retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);
            throw new TranslationRateLimitException(retryAfter);
        }
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("responseData", out var responseData) &&
            responseData.TryGetProperty("translatedText", out var translatedText))
        {
            return translatedText.GetString() ?? text;
        }

        throw new InvalidOperationException("Invalid MyMemory response structure.");
    }

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

public sealed class TranslationRateLimitException : Exception
{
    public TranslationRateLimitException()
        : base("MyMemory rate limited the request.")
    {
    }

    public TranslationRateLimitException(string message)
        : base(message)
    {
    }

    public TranslationRateLimitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public TranslationRateLimitException(TimeSpan retryAfter)
        : base($"MyMemory rate limited the request; retry after {retryAfter.TotalSeconds:0}s.")
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan RetryAfter { get; }
}

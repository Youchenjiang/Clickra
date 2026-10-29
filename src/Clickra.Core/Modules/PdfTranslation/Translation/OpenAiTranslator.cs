using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core.Modules.PdfTranslation.Translation;

/// <summary>
/// OpenAI-compatible translation engine supporting OpenAI, DeepSeek, Ollama, Groq, and other OpenAI-compatible endpoints.
/// </summary>
public class OpenAiTranslator : ITranslationEngine
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private readonly HttpClient _httpClient;

    public string Name => "custom-ai";
    public string Endpoint { get; }
    public string? ApiKey { get; }
    public string Model { get; }

    public OpenAiTranslator(string? endpoint = null, string? apiKey = null, string? model = null, HttpClient? httpClient = null)
    {
        Endpoint = NormalizeEndpoint(endpoint);
        ApiKey = apiKey;
        Model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model.Trim();
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text) || text.All(c => !char.IsLetter(c)))
        {
            return text;
        }

        string systemPrompt = $"You are a professional translator. Translate the text into {targetLanguage}. Preserve all numbers, markdown, code, math formulas, and placeholders like {{{{0}}}}, {{{{MATH_0}}}}, etc. Output ONLY the translated text without any explanation, markdown code fence, or conversational response.";
        string payloadJson = BuildChatPayloadJson(Model, systemPrompt, text);

        using var request = CreateChatRequest(payloadJson);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new TranslationRateLimitException(
                "OpenAI-compatible translation service rate limited the request.",
                TranslationProviderLimits.DefaultRetryAfter);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException($"OpenAI authentication failed (status {(int)response.StatusCode}). Check your API Key.");
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI request failed (status {(int)response.StatusCode}): {body}");
        }

        return ParseResponse(body, text);
    }

    public async Task<List<string>> TranslateBatchAsync(List<string> texts, string targetLanguage, CancellationToken cancellationToken)
    {
        var results = new List<string>();
        if (texts == null || texts.Count == 0) return results;

        try
        {
            var batchResults = await TranslateJsonArrayBatchAsync(texts, targetLanguage, cancellationToken);
            if (batchResults != null && batchResults.Count == texts.Count)
            {
                return batchResults;
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && !TranslationProviderLimits.IsLimit(ex))
        {
            // Fall back to item-by-item translation when JSON batching fails.
        }

        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await TranslateAsync(text, targetLanguage, cancellationToken));
        }

        return results;
    }

    private async Task<List<string>?> TranslateJsonArrayBatchAsync(List<string> texts, string targetLanguage, CancellationToken cancellationToken)
    {
        string inputJson = BuildJsonArrayString(texts);
        string systemPrompt = $"You are a professional translator. Translate each string in the JSON array to {targetLanguage}. Maintain the exact order and length. Return ONLY a valid JSON array of strings without markdown code blocks, explanations, or wrappers.";
        string payloadJson = BuildChatPayloadJson(Model, systemPrompt, inputJson);

        using var request = CreateChatRequest(payloadJson);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new TranslationRateLimitException("OpenAI rate limited the request.", TranslationProviderLimits.DefaultRetryAfter);
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        string rawContent = ParseResponse(body, inputJson);
        return ParseJsonArray(rawContent, texts.Count);
    }

    private HttpRequestMessage CreateChatRequest(string payloadJson)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey.Trim());
        }

        return request;
    }

    internal static string NormalizeEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return "https://api.openai.com/v1/chat/completions";
        }

        string trimmed = endpoint.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"{trimmed}/chat/completions";
    }

    internal static string ParseResponse(string json, string sourceText)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
            {
                string? text = content.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return CleanResponse(text);
                }
            }
        }

        if (root.TryGetProperty("error", out var errorElement))
        {
            throw new InvalidOperationException($"OpenAI returned error: {errorElement}");
        }

        throw new InvalidOperationException("Invalid OpenAI response structure.");
    }

    internal static List<string>? ParseJsonArray(string rawContent, int expectedCount)
    {
        string cleaned = CleanResponse(rawContent);
        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            if (doc.RootElement.ValueKind == JsonValueKind.Array &&
                doc.RootElement.GetArrayLength() == expectedCount)
            {
                var list = new List<string>(expectedCount);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    list.Add(el.GetString() ?? string.Empty);
                }
                return list;
            }
        }
        catch (JsonException)
        {
            // Model returned non-JSON text.
        }

        return null;
    }

    private static string BuildChatPayloadJson(string model, string systemPrompt, string userContent)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WriteNumber("temperature", 0.2);
            writer.WriteStartArray("messages");

            writer.WriteStartObject();
            writer.WriteString("role", "system");
            writer.WriteString("content", systemPrompt);
            writer.WriteEndObject();

            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteString("content", userContent);
            writer.WriteEndObject();

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildJsonArrayString(IEnumerable<string> items)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var item in items)
            {
                writer.WriteStringValue(item);
            }
            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string CleanResponse(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal) && text.EndsWith("```", StringComparison.Ordinal))
        {
            int firstNewline = text.IndexOf('\n');
            int lastBackticks = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastBackticks > firstNewline)
            {
                text = text.Substring(firstNewline + 1, lastBackticks - firstNewline - 1).Trim();
            }
        }
        return text;
    }
}

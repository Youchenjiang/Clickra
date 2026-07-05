using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core;

internal class GoogleFreeTranslator : BaseTranslator
{
    public override string Name => "google-free";

    public override async Task<string> TranslateInternalAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        string lang = Uri.EscapeDataString(NormalizeLanguageCode(targetLanguage));
        string url = $"https://translate.google.com/translate_a/t?client=at&sl=auto&tl={lang}";

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("q", text)
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        request.Headers.UserAgent.ParseAdd("AndroidTranslate/5.3.0.RC02.130758309-53000263 5.1 phone TRANSLATE_MOBILE_APPLICATION");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            var first = root[0];
            if (first.ValueKind == JsonValueKind.Array && first.GetArrayLength() > 0)
            {
                return first[0].GetString() ?? text;
            }
        }
        throw new InvalidOperationException("Unexpected response format from Google Mobile Translate.");
    }

    public override async Task<List<string>> TranslateBatchAsync(List<string> texts, string targetLanguage, CancellationToken cancellationToken)
    {
        if (texts == null || texts.Count == 0) return new List<string>();

        int retries = 2;
        int delayMs = 1000;
        while (true)
        {
            try
            {
                string lang = Uri.EscapeDataString(NormalizeLanguageCode(targetLanguage));
                string url = $"https://translate.google.com/translate_a/t?client=at&sl=auto&tl={lang}";

                var list = new List<KeyValuePair<string, string>>();
                foreach (var text in texts)
                {
                    list.Add(new KeyValuePair<string, string>("q", text));
                }

                var content = new FormUrlEncodedContent(list);
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = content;
                request.Headers.UserAgent.ParseAdd("AndroidTranslate/5.3.0.RC02.130758309-53000263 5.1 phone TRANSLATE_MOBILE_APPLICATION");

                using var response = await HttpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    var results = new List<string>();
                    foreach (var element in root.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0)
                        {
                            var translated = element[0].GetString();
                            results.Add(translated ?? "");
                        }
                        else
                        {
                            results.Add("");
                        }
                    }
                    return results;
                }
                throw new InvalidOperationException("Unexpected response format from Google Mobile Translate.");
            }
            catch (Exception) when (retries > 0 && !cancellationToken.IsCancellationRequested)
            {
                retries--;
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
        }
    }
}

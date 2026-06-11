using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core
{
    public interface ITranslationEngine
    {
        string Name { get; }
        Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken);
        Task<List<string>> TranslateBatchAsync(List<string> texts, string targetLanguage, CancellationToken cancellationToken);
    }

    public static class TranslationEngineFactory
    {
        public static ITranslationEngine Create()
        {
            return new GoogleFreeTranslator();
        }
    }

    public abstract class BaseTranslator : ITranslationEngine
    {
        public abstract string Name { get; }
        protected static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly SemaphoreSlim ConcurrencySemaphore = new SemaphoreSlim(1, 1);
        private static readonly Random Rnd = new Random();

        public abstract Task<string> TranslateInternalAsync(string text, string targetLanguage, CancellationToken cancellationToken);
        public abstract Task<List<string>> TranslateBatchAsync(List<string> texts, string targetLanguage, CancellationToken cancellationToken);

        public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            int retries = 5;
            int delayMs = 1500;
            while (true)
            {
                await ConcurrencySemaphore.WaitAsync(cancellationToken);
                try
                {
                    // Add a small random delay inside the lock to regulate concurrency rate
                    int sleepMs = Rnd.Next(150, 400);
                    await Task.Delay(sleepMs, cancellationToken);

                    string result = await TranslateInternalAsync(text, targetLanguage, cancellationToken);
                    return string.IsNullOrWhiteSpace(result) ? text : result;
                }
                catch (Exception) when (retries > 0 && !cancellationToken.IsCancellationRequested)
                {
                    retries--;
                }
                finally
                {
                    ConcurrencySemaphore.Release();
                }

                // Delay outside the lock to prevent deadlocking other concurrent requests
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
        }

        protected string NormalizeLanguageCode(string code)
        {
            code = code.ToLowerInvariant();
            return code switch
            {
                "zh-tw" => "zh-TW",
                "zh-cn" => "zh-CN",
                _ => code
            };
        }
    }

    public class GoogleFreeTranslator : BaseTranslator
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
            throw new Exception("Unexpected response format from Google Mobile Translate.");
        }

        public override async Task<List<string>> TranslateBatchAsync(List<string> texts, string targetLanguage, CancellationToken cancellationToken)
        {
            if (texts == null || texts.Count == 0) return new List<string>();

            int retries = 5;
            int delayMs = 1500;
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
                    throw new Exception("Unexpected response format from Google Mobile Translate.");
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
}

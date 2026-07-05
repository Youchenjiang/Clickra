using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
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
            string? engine = Environment.GetEnvironmentVariable("CLICKRA_TRANSLATION_ENGINE");
            if (string.Equals(engine, "identity", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(engine, "passthrough", StringComparison.OrdinalIgnoreCase))
            {
                return new IdentityTranslator();
            }
            if (string.Equals(engine, "google", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(engine, "google-free", StringComparison.OrdinalIgnoreCase))
            {
                return new GoogleFreeTranslator();
            }
            if (string.Equals(engine, "mymemory", StringComparison.OrdinalIgnoreCase))
            {
                return new MyMemoryTranslator();
            }

            return new FallbackTranslator(new GoogleFreeTranslator(), new MyMemoryTranslator());
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

    public class MyMemoryTranslator : ITranslationEngine
    {
        private static int _nodeTransportRequired;
        private static readonly HttpClient HttpClient = new HttpClient(new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }
        })
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        public string Name => "mymemory";

        public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (Volatile.Read(ref _nodeTransportRequired) == 1)
                return await TranslateWithNodeAsync(text, targetLanguage, cancellationToken);

            int retries = 2;
            int delayMs = 1000;
            while (true)
            {
                try
                {
                    return await TranslateInternalAsync(text, targetLanguage, cancellationToken);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref _nodeTransportRequired, 1);
                    try
                    {
                        return await TranslateWithNodeAsync(text, targetLanguage, cancellationToken);
                    }
                    catch when (retries > 0 && !cancellationToken.IsCancellationRequested)
                    {
                        retries--;
                    }
                }

                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
        }

        private async Task<string> TranslateInternalAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            string langPair = Uri.EscapeDataString($"en|{NormalizeLanguageCode(targetLanguage)}");
            string query = Uri.EscapeDataString(text);
            string url = $"https://api.mymemory.translated.net/get?q={query}&langpair={langPair}&de=clickra@yandex.com";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 Clickra/1.0");
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("responseData", out var responseData) &&
                responseData.TryGetProperty("translatedText", out var translatedText))
            {
                return translatedText.GetString() ?? text;
            }

            throw new Exception("Invalid MyMemory response structure.");
        }

        private static async Task<string> TranslateWithNodeAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            string script = @"
const https = require('https');
const text = process.argv[1] || '';
const target = process.argv[2] || 'zh-TW';
const url = 'https://api.mymemory.translated.net/get?q=' + encodeURIComponent(text) +
  '&langpair=' + encodeURIComponent('en|' + target) +
  '&de=clickra@yandex.com';
const req = https.get(url, res => {
  let data = '';
  res.on('data', chunk => data += chunk);
  res.on('end', () => {
    try {
      const parsed = JSON.parse(data);
      const translated = parsed && parsed.responseData && parsed.responseData.translatedText;
      if (!translated) process.exit(2);
      process.stdout.write(translated);
    } catch {
      process.exit(3);
    }
  });
});
req.on('error', () => process.exit(4));
req.setTimeout(20000, () => { req.destroy(); process.exit(5); });
";
            using var process = new Process();
            process.StartInfo.FileName = "node";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.ArgumentList.Add("-e");
            process.StartInfo.ArgumentList.Add(script);
            process.StartInfo.ArgumentList.Add(text);
            process.StartInfo.ArgumentList.Add(NormalizeLanguageCode(targetLanguage));

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                throw new Exception("Node.js MyMemory fallback is unavailable.", ex);
            }

            using var registration = cancellationToken.Register(() =>
            {
                try { process.Kill(true); } catch { }
            });

            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            string error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                throw new Exception($"Node.js MyMemory fallback failed: {process.ExitCode} {error}");

            return output;
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

        private static string NormalizeLanguageCode(string code)
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

    public class FallbackTranslator : ITranslationEngine
    {
        private readonly ITranslationEngine _primary;
        private readonly ITranslationEngine _fallback;

        public FallbackTranslator(ITranslationEngine primary, ITranslationEngine fallback)
        {
            _primary = primary;
            _fallback = fallback;
        }

        public string Name => $"{_primary.Name}+{_fallback.Name}";

        public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            try
            {
                return await _primary.TranslateAsync(text, targetLanguage, cancellationToken);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                return await _fallback.TranslateAsync(text, targetLanguage, cancellationToken);
            }
        }

        public async Task<List<string>> TranslateBatchAsync(List<string> texts, string targetLanguage, CancellationToken cancellationToken)
        {
            var results = new List<string>();
            if (texts == null || texts.Count == 0) return results;

            foreach (var chunk in BuildChunks(texts, maxItems: 24, maxChars: 6000))
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<string> translated;
                try
                {
                    translated = await _primary.TranslateBatchAsync(chunk, targetLanguage, cancellationToken);
                    if (translated.Count != chunk.Count)
                        throw new Exception($"{_primary.Name} returned {translated.Count}/{chunk.Count} results.");
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    Console.Error.WriteLine(
                        $"[Translate] {_primary.Name} batch failed ({ex.Message}); falling back to {_fallback.Name}.");
                    translated = await _fallback.TranslateBatchAsync(chunk, targetLanguage, cancellationToken);
                    if (translated.Count != chunk.Count)
                        throw new Exception($"{_fallback.Name} returned {translated.Count}/{chunk.Count} results.");
                }

                results.AddRange(translated);
            }

            if (results.Count != texts.Count)
                throw new Exception($"{Name} returned {results.Count}/{texts.Count} total results.");

            return results;
        }

        private static IEnumerable<List<string>> BuildChunks(List<string> texts, int maxItems, int maxChars)
        {
            var chunk = new List<string>();
            int charCount = 0;

            foreach (var text in texts)
            {
                string value = text ?? "";
                int textLength = value.Length;
                if (chunk.Count > 0 &&
                    (chunk.Count >= maxItems || charCount + textLength > maxChars))
                {
                    yield return chunk;
                    chunk = new List<string>();
                    charCount = 0;
                }

                chunk.Add(value);
                charCount += textLength;
            }

            if (chunk.Count > 0)
                yield return chunk;
        }
    }

    public class IdentityTranslator : ITranslationEngine
    {
        public string Name => "identity";

        public Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            return Task.FromResult(text);
        }

        public Task<List<string>> TranslateBatchAsync(List<string> texts, string targetLanguage, CancellationToken cancellationToken)
        {
            return Task.FromResult(texts?.ToList() ?? new List<string>());
        }
    }
}

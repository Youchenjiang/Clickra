using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Clickra.Core
{
    internal class IdentityTranslator : ITranslationEngine
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

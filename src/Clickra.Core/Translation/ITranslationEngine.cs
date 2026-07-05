using System.Collections.Generic;
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
}

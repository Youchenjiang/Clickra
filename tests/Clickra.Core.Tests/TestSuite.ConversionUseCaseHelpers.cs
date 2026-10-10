using Clickra.Core.Application;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    private static (ConversionPlan Plan, ConversionResult Result) ExecuteUntrackedUseCase(
        IConversionUseCase useCase,
        string command,
        IReadOnlyList<string> inputs,
        string outputDir,
        IReadOnlyDictionary<string, object>? options = null)
    {
        ConversionPlan plan = useCase.Plan(new ConversionRequest(
            command,
            inputs,
            options,
            OutputOverride: outputDir,
            TrackTaskLifecycle: false));
        DelegateConversionInteraction interaction = CreateNoOpConversionInteraction();
        ConversionResult result = useCase.ExecuteAsync(plan, interaction, progress: null)
            .GetAwaiter()
            .GetResult();
        return (plan, result);
    }
}

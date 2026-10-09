namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow owner for one conversion command.</summary>
public interface IConversionUseCase
{
    string Command { get; }

    ConversionValidationResult Validate(ConversionRequest request);

    ConversionPlan Plan(ConversionRequest request);

    Task<ConversionResult> ExecuteAsync(
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken = default);
}

/// <summary>Validation result that presentation layers can render without reconstructing policy.</summary>
public readonly record struct ConversionValidationResult(bool IsValid, string? Error)
{
    public static ConversionValidationResult Success() => new(true, null);

    public static ConversionValidationResult Failure(string error) => new(false, error);
}

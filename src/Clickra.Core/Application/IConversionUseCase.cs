namespace Clickra.Core.Application;

/// <summary>High-level input requirements self-declared by conversion use cases for blind UI rendering.</summary>
public enum RequiredInputType
{
    None,
    Password,
    PageRange,
    CustomOptions
}

/// <summary>Authoritative application workflow owner for one conversion command.</summary>
public interface IConversionUseCase
{
    string Command { get; }

    IReadOnlySet<ConversionCapability> Capabilities => new HashSet<ConversionCapability>();

    /// <summary>Self-declared input requirement consumed by presentation surfaces without hardcoding command IDs.</summary>
    RequiredInputType InputRequirement =>
        Capabilities.Contains(ConversionCapability.Password) ? RequiredInputType.Password :
        Capabilities.Contains(ConversionCapability.SplitPages) ? RequiredInputType.PageRange :
        (Capabilities.Contains(ConversionCapability.MarkdownOptions) ||
         Capabilities.Contains(ConversionCapability.PrintOptions) ||
         Capabilities.Contains(ConversionCapability.BatchRename)) ? RequiredInputType.CustomOptions :
        RequiredInputType.None;

    /// <summary>Self-declared lifecycle behavior: whether running this use case records a history task.</summary>
    bool GeneratesHistoryTask => true;

    /// <summary>Self-declared input acceptance: whether the use case accepts any file extension.</summary>
    bool AcceptsAllFileExtensions => false;

    ConversionValidationResult Validate(ConversionRequest request);

    ConversionPlan Plan(ConversionRequest request);

    Task<ConversionResult> ExecuteAsync(
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        IConversionExecutionObserver? observer = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Validation result that presentation layers can render without reconstructing policy.</summary>
public readonly record struct ConversionValidationResult(bool IsValid, string? Error)
{
    public static ConversionValidationResult Success() => new(true, null);

    public static ConversionValidationResult Failure(string error) => new(false, error);
}

/// <summary>Lifecycle events exposed to presentation adapters without giving them persistence ownership.</summary>
public interface IConversionExecutionObserver
{
    void OnTaskStarted(string taskId);

    void OnFileStarting(int fileIndex);
}

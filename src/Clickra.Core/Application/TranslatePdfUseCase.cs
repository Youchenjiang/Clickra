using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>Authoritative application workflow for translating PDF files.</summary>
public sealed class TranslatePdfUseCase : PerFileConversionUseCaseBase
{
    public const string CommandName = "translate-pdf";
    public const string TargetLanguageOptionKey = "targetLang";

    public override string Command => CommandName;

    protected override string UseCaseName => nameof(TranslatePdfUseCase);

    protected override string InputRequirementError => "At least one PDF file is required.";

    protected override string UnsupportedInputError(string path) =>
        $"Unsupported translation input '{path}'.";

    protected override string OutputCountError =>
        "PDF translation requires one output per input.";

    protected override string GetOutputFileName(string input) =>
        Path.GetFileNameWithoutExtension(input) + "_translated.pdf";

    protected override ConversionValidationResult ValidateOptions(ConversionRequest request)
    {
        if (request.Options is not null
            && request.Options.TryGetValue(TargetLanguageOptionKey, out object? targetLanguage)
            && (targetLanguage is not string text || string.IsNullOrWhiteSpace(text)))
        {
            return ConversionValidationResult.Failure(
                "Translation target language must be a non-empty string.");
        }

        return ConversionValidationResult.Success();
    }

    protected override IReadOnlyDictionary<string, object> NormalizeOptions(ConversionRequest request)
    {
        var options = request.Options is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(request.Options, StringComparer.Ordinal);
        if (!options.ContainsKey(TargetLanguageOptionKey))
        {
            options[TargetLanguageOptionKey] =
                ClickraStorage.GetSetting(ClickraSettings.TranslateTargetLang);
        }
        return options;
    }

    protected override object CreateExecutionState(ConversionPlan plan)
    {
        if (!plan.NormalizedOptions.TryGetValue(TargetLanguageOptionKey, out object? value)
            || value is not string targetLanguage
            || string.IsNullOrWhiteSpace(targetLanguage))
        {
            throw new InvalidOperationException("PDF translation requires a target language.");
        }
        return targetLanguage;
    }

    protected override Task ExecuteFileAsync(
        int index,
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        object? executionState,
        CancellationToken cancellationToken)
    {
        string targetLanguage = (string)executionState!;
        return Task.Run(
            () => FileProcessor.TranslatePdf(
                plan.Inputs[index],
                plan.Outputs[index],
                targetLanguage,
                (current, _, message) => ReportFileProgress(plan, index, progress, current, message),
                cancellationToken),
            cancellationToken);
    }

    protected override ConversionFailureKind ClassifyFailure(Exception exception) =>
        ConversionFailureClassifier.Classify(exception);
}

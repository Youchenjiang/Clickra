using Clickra.Core.Modules.Sys.Settings.Application;
using Clickra.Core.Application;
using Clickra.Core.Modules.Pdf.Decrypt.Processing;
using Clickra.Core.Processors;
using PdfSharp.Pdf.IO;

namespace Clickra.Core.Modules.Pdf.Decrypt.Application;

/// <summary>Authoritative application workflow for removing PDF password protection.</summary>
public sealed class DecryptPdfUseCase : PerFileConversionUseCaseBase
{
    public const string CommandName = "decrypt-pdf";
    /// <summary>Password typed on the decrypt card; tried before asking.</summary>
    public const string PasswordOptionKey = "password";

    public override string Command => CommandName;

    protected override string UseCaseName => nameof(DecryptPdfUseCase);
    protected override string InputRequirementError => "At least one PDF file is required.";
    protected override string OutputCountError => "Decrypt conversion requires one output per input.";
    protected override string UnsupportedInputError(string path) => $"Unsupported decrypt input '{path}'.";
    protected override string GetOutputFileName(string input) =>
        Path.GetFileNameWithoutExtension(input) + "_decrypted.pdf";

    public override IReadOnlySet<ConversionCapability> Capabilities =>
        new HashSet<ConversionCapability> { ConversionCapability.Password };

    public override RequiredInputType InputRequirement => RequiredInputType.Password;

    protected override IReadOnlySet<ConversionCapability> GetCapabilities(ConversionRequest request) =>
        Capabilities;

    protected override IReadOnlyDictionary<string, object> NormalizeOptions(ConversionRequest request)
    {
        var options = request.Options is null
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(request.Options, StringComparer.Ordinal);
        if (!options.ContainsKey(ExecutionLocalizationOptions.LanguageKey) || options[ExecutionLocalizationOptions.LanguageKey] is null)
        {
            options[ExecutionLocalizationOptions.LanguageKey] = SettingsController.Language;
        }
        return options;
    }

    protected override async Task ExecuteFileAsync(
        int index,
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        object? executionState,
        CancellationToken cancellationToken)
    {
        if (plan.NormalizedOptions.TryGetValue(PasswordOptionKey, out object? supplied) &&
            supplied is string text && text.Length > 0)
        {
            PasswordCache.Add(text);
        }

        using var unlock = await PdfUnlockStep.UnlockAsync(
            index,
            plan.Inputs[index],
            interaction,
            PasswordCache,
            cancellationToken).ConfigureAwait(false);

        if (unlock.Status == PdfUnlockStatus.NotEncrypted)
        {
            throw new ConversionFileSkippedException(ConversionSkipReason.NotEncrypted);
        }

        if (unlock.IsSkipped)
        {
            throw new ConversionFileSkippedException(ConversionSkipReason.NeedsPassword);
        }

        string lang = ExecutionLocalizationOptions.ReadLanguage(plan.NormalizedOptions);
        ReportFileProgress(plan, index, progress, 50, ExecutionLocalizationOptions.Text(lang, "core_decrypt_progress_removing"));

        cancellationToken.ThrowIfCancellationRequested();

        string targetOutput = plan.Outputs[index];
        string? targetDir = Path.GetDirectoryName(targetOutput);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        System.IO.File.Copy(unlock.EffectivePath, targetOutput, overwrite: true);

        ReportFileProgress(plan, index, progress, 100, ExecutionLocalizationOptions.Text(lang, "core_decrypt_done"));
    }
}

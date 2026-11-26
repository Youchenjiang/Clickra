using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clickra.Core.Application;
using Clickra.Core.Modules.Pdf.Decrypt.Application;
using Clickra.Core.Modules.Pdf.Merge.Processing;
using Clickra.Core.Processors;

namespace Clickra.Core.Modules.Pdf.Merge.Application;

/// <summary>Authoritative application workflow for merging PDF files into one document.</summary>
public sealed class MergePdfUseCase : SingleOutputConversionUseCaseBase
{
    public const string CommandName = "merge-pdf";
    public const string OutputFileName = "Merged_PDF.pdf";

    public override string Command => CommandName;
    public override IReadOnlySet<ConversionCapability> Capabilities =>
        new HashSet<ConversionCapability> { ConversionCapability.Password };

    protected override string UseCaseName => nameof(MergePdfUseCase);
    protected override string InputRequirementError => "At least two PDF files are required.";
    protected override string PlannedOutputFileName => OutputFileName;
    protected override string OutputCountError => "PDF merge requires exactly one output.";
    protected override string UnsupportedInputError(string path) => $"Unsupported merge input '{path}'.";

    protected override async Task ExecuteSingleAsync(
        ConversionPlan plan,
        IConversionInteraction interaction,
        IProgress<ConversionProgress>? progress,
        List<SkippedFileInfo> skippedFiles,
        CancellationToken cancellationToken)
    {
        var passwordCache = new ConversionPasswordCache();
        var unlockedFiles = new List<PdfUnlockResult>();
        try
        {
            for (int i = 0; i < plan.Inputs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var unlock = await PdfUnlockStep.UnlockAsync(
                    i,
                    plan.Inputs[i],
                    interaction,
                    passwordCache,
                    cancellationToken).ConfigureAwait(false);

                if (unlock.IsSkipped)
                {
                    unlock.Dispose();
                    skippedFiles.Add(new SkippedFileInfo(plan.Inputs[i], ConversionSkipReason.NeedsPassword));
                }
                else
                {
                    unlockedFiles.Add(unlock);
                }
            }

            if (skippedFiles.Count == plan.Inputs.Count)
            {
                string lang = ReadLanguage(plan.NormalizedOptions);
                throw new InvalidOperationException(Localization.T("core_pdf_all_files_need_password", lang));
            }

            if (unlockedFiles.Count < 2)
            {
                string lang = ReadLanguage(plan.NormalizedOptions);
                throw new InvalidOperationException(Localization.T("core_merge_only_one_file", lang));
            }

            var processor = new PdfMergeProcessor();
            await Task.Run(
                () => processor.Process(
                    unlockedFiles.Select(u => u.EffectivePath).ToList(),
                    plan.Outputs[0],
                    new Dictionary<string, object>(plan.NormalizedOptions, StringComparer.Ordinal),
                    (current, total, message) => progress?.Report(
                        new ConversionProgress(current, total, message)),
                    cancellationToken),
                cancellationToken);
        }
        finally
        {
            foreach (var u in unlockedFiles)
            {
                u.Dispose();
            }
        }
    }

    private static string ReadLanguage(IReadOnlyDictionary<string, object>? options) =>
        options is not null && options.TryGetValue(ConversionRequest.LanguageOptionKey, out object? value)
            ? Convert.ToString(value) ?? ""
            : "";
}

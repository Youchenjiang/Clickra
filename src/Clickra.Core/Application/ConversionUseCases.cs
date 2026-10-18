using Clickra.Core.Modules.Image.Application;
using Clickra.Core.Modules.Markdown.Application;
using Clickra.Core.Modules.Office.Application;
using Clickra.Core.Modules.PdfCompression.Application;
using Clickra.Core.Modules.PdfDecrypt.Application;
using Clickra.Core.Modules.PdfMerge.Application;
using Clickra.Core.Modules.PdfSplit.Application;
using Clickra.Core.Modules.PdfTranslation.Application;

namespace Clickra.Core.Application;

/// <summary>Product-wide catalog of application conversion use cases.</summary>
public static class ConversionUseCases
{
    private static readonly ConversionUseCaseRegistry Registry = new(
        new IConversionUseCase[]
        {
            new CompressPdfUseCase(),
            new DecryptPdfUseCase(),
            new ExcelToPdfUseCase(),
            new ImgCompressUseCase(),
            new ImageFormatConvertUseCase(ImageFormatConvertUseCase.PngCommand),
            new ImageFormatConvertUseCase(ImageFormatConvertUseCase.JpgCommand),
            new ImageFormatConvertUseCase(ImageFormatConvertUseCase.WebpCommand),
            new ImageFormatConvertUseCase(ImageFormatConvertUseCase.GifCommand),
            new ImageFormatConvertUseCase(ImageFormatConvertUseCase.HeicCommand),
            new ImgMergeUseCase(),
            new Img2PdfUseCase(),
            new ImgStitchUseCase(),
            new MergePdfUseCase(),
            new MarkdownToPdfUseCase(),
            new MarkdownToWordUseCase(),
            new PptToPdfUseCase(),
            new SplitPdfUseCase(),
            new TranslatePdfUseCase(),
            new WordToPdfUseCase()
        });

    public static IReadOnlyCollection<string> Commands => Registry.Commands;

    public static bool TryGet(string command, out IConversionUseCase? useCase) =>
        Registry.TryGet(command, out useCase);

    public static IConversionUseCase GetRequired(string command) =>
        Registry.GetRequired(command);
}

/// <summary>
/// Unified application execution service that plans and runs conversion workflows.
/// Consolidates execution orchestration across headless CLI, Native adapters, and presentation controllers.
/// </summary>
public static class ConversionExecutionService
{
    public static ConversionPlan Plan(
        string command,
        IReadOnlyList<string> files,
        IReadOnlyDictionary<string, object>? options = null,
        string? outputOverride = null,
        bool TrackTaskLifecycle = true,
        string? existingTaskId = null,
        bool bestEffortTaskPersistence = false,
        int resumeStartIndex = 0)
    {
        IConversionUseCase useCase = ConversionUseCases.GetRequired(command);
        ConversionPlan plan = useCase.Plan(new ConversionRequest(
            command,
            files,
            options,
            OutputOverride: outputOverride,
            TrackTaskLifecycle: TrackTaskLifecycle,
            ExistingTaskId: existingTaskId,
            BestEffortTaskPersistence: bestEffortTaskPersistence));

        if (resumeStartIndex > 0)
            plan = plan with { ResumeStartIndex = resumeStartIndex };

        return plan;
    }

    public static async Task<ConversionResult> ExecuteAsync(
        ConversionPlan plan,
        IConversionInteraction? interaction = null,
        IProgress<ConversionProgress>? progress = null,
        IConversionExecutionObserver? observer = null,
        CancellationToken cancellationToken = default)
    {
        IConversionUseCase useCase = ConversionUseCases.GetRequired(plan.Command);
        interaction ??= new DelegateConversionInteraction(
            (_, _, _, _) => Task.FromResult<string?>(null),
            (_, _, _) => Task.FromResult<string?>(null),
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<string, object>?>(null));

        return await useCase.ExecuteAsync(
            plan,
            interaction,
            progress,
            observer,
            cancellationToken).ConfigureAwait(false);
    }

    public static ConversionResult Execute(
        ConversionPlan plan,
        IConversionInteraction? interaction = null,
        IProgress<ConversionProgress>? progress = null,
        IConversionExecutionObserver? observer = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(plan, interaction, progress, observer, cancellationToken)
            .GetAwaiter()
            .GetResult();
}

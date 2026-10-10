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

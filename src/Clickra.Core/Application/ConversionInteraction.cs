namespace Clickra.Core.Application;

/// <summary>Platform capabilities a conversion use case can request without depending on a UI technology.</summary>
public interface IConversionInteraction
{
    Task<string?> RequestPasswordAsync(
        int fileIndex,
        string inputPath,
        CancellationToken cancellationToken);

    Task<string?> RequestSplitPagesAsync(
        int fileIndex,
        string inputPath,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, object>?> RequestMarkdownOptionsAsync(
        string command,
        IReadOnlyList<string> inputFiles,
        CancellationToken cancellationToken);
}

/// <summary>UI-independent progress payload emitted by application use cases.</summary>
public readonly record struct ConversionProgress(int Current, int Total, string Message);

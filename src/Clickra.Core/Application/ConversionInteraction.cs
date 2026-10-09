namespace Clickra.Core.Application;

/// <summary>Platform capabilities a conversion use case can request without depending on a UI technology.</summary>
public interface IConversionInteraction
{
    Task<string?> RequestPasswordAsync(
        int fileIndex,
        string inputPath,
        bool isRetry,
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

/// <summary>Signals that an interactive conversion should be parked and resumed later.</summary>
public sealed class ConversionParkedException : Exception
{
    public ConversionParkedException(string reason, int nextFileIndex)
        : base(reason) =>
        NextFileIndex = nextFileIndex;

    public int NextFileIndex { get; }
}

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

/// <summary>Adapts platform delegates to the application interaction contract.</summary>
public sealed class DelegateConversionInteraction : IConversionInteraction
{
    private readonly Func<int, string, bool, CancellationToken, Task<string?>> _password;
    private readonly Func<int, string, CancellationToken, Task<string?>> _splitPages;
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, object>?>> _markdownOptions;

    public DelegateConversionInteraction(
        Func<int, string, bool, CancellationToken, Task<string?>> password,
        Func<int, string, CancellationToken, Task<string?>> splitPages,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, object>?>> markdownOptions)
    {
        _password = password ?? throw new ArgumentNullException(nameof(password));
        _splitPages = splitPages ?? throw new ArgumentNullException(nameof(splitPages));
        _markdownOptions = markdownOptions ?? throw new ArgumentNullException(nameof(markdownOptions));
    }

    public Task<string?> RequestPasswordAsync(
        int fileIndex,
        string inputPath,
        bool isRetry,
        CancellationToken cancellationToken) =>
        _password(fileIndex, inputPath, isRetry, cancellationToken);

    public Task<string?> RequestSplitPagesAsync(
        int fileIndex,
        string inputPath,
        CancellationToken cancellationToken) =>
        _splitPages(fileIndex, inputPath, cancellationToken);

    public Task<IReadOnlyDictionary<string, object>?> RequestMarkdownOptionsAsync(
        string command,
        IReadOnlyList<string> inputFiles,
        CancellationToken cancellationToken) =>
        _markdownOptions(command, inputFiles, cancellationToken);
}

/// <summary>Signals that an interactive conversion should be parked and resumed later.</summary>
public sealed class ConversionParkedException : Exception
{
    public ConversionParkedException(string reason, int nextFileIndex)
        : base(reason) =>
        NextFileIndex = nextFileIndex;

    public int NextFileIndex { get; }
}

namespace Clickra.Core.Application;

public enum PasswordPromptStatus
{
    Password,
    Skip,
    Cancel
}

public sealed record PasswordPromptResult(PasswordPromptStatus Status, string? Password = null)
{
    public static PasswordPromptResult FromPassword(string password) =>
        new(PasswordPromptStatus.Password, password ?? throw new ArgumentNullException(nameof(password)));

    public static PasswordPromptResult Skip { get; } = new(PasswordPromptStatus.Skip);

    public static PasswordPromptResult Cancel { get; } = new(PasswordPromptStatus.Cancel);

    public bool IsPassword => Status == PasswordPromptStatus.Password;
    public bool IsSkip => Status == PasswordPromptStatus.Skip;
    public bool IsCancel => Status == PasswordPromptStatus.Cancel;
}

public enum ConversionSkipReason
{
    None,
    NeedsPassword,
    NotEncrypted
}

public readonly record struct SkippedFileInfo(string Path, ConversionSkipReason Reason);

public sealed class ConversionFileSkippedException : Exception
{
    public ConversionFileSkippedException(ConversionSkipReason reason)
        : base($"File was skipped: {reason}") =>
        Reason = reason;

    public ConversionSkipReason Reason { get; }
}

public sealed class ConversionPasswordCache
{
    private readonly List<string> _passwords = new();
    private readonly object _lock = new();

    public IReadOnlyList<string> GetSnapshot()
    {
        lock (_lock) return _passwords.ToArray();
    }

    public void Add(string password)
    {
        if (string.IsNullOrEmpty(password)) return;
        lock (_lock)
        {
            if (!_passwords.Contains(password))
            {
                _passwords.Add(password);
            }
        }
    }
}

/// <summary>Platform capabilities a conversion use case can request without depending on a UI technology.</summary>
public interface IConversionInteraction
{
    Task<string?> RequestPasswordAsync(
        int fileIndex,
        string inputPath,
        bool isRetry,
        CancellationToken cancellationToken);

    Task<PasswordPromptResult> RequestPasswordResultAsync(
        int fileIndex,
        string inputPath,
        bool isRetry,
        CancellationToken cancellationToken) =>
        RequestPasswordAsync(fileIndex, inputPath, isRetry, cancellationToken)
            .ContinueWith(t => t.Result is { } pwd ? PasswordPromptResult.FromPassword(pwd) : PasswordPromptResult.Cancel, cancellationToken);

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
    private readonly Func<int, string, bool, CancellationToken, Task<string?>>? _password;
    private readonly Func<int, string, bool, CancellationToken, Task<PasswordPromptResult>>? _passwordResult;
    private readonly Func<int, string, CancellationToken, Task<string?>> _splitPages;
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, object>?>> _markdownOptions;

    public DelegateConversionInteraction(
        Func<int, string, bool, CancellationToken, Task<string?>>? password,
        Func<int, string, CancellationToken, Task<string?>> splitPages,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, object>?>> markdownOptions)
        : this(password, null, splitPages, markdownOptions)
    {
    }

    public DelegateConversionInteraction(
        Func<int, string, bool, CancellationToken, Task<string?>>? password,
        Func<int, string, bool, CancellationToken, Task<PasswordPromptResult>>? passwordResult,
        Func<int, string, CancellationToken, Task<string?>> splitPages,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, object>?>> markdownOptions)
    {
        _password = password;
        _passwordResult = passwordResult;
        _splitPages = splitPages ?? throw new ArgumentNullException(nameof(splitPages));
        _markdownOptions = markdownOptions ?? throw new ArgumentNullException(nameof(markdownOptions));
    }

    public async Task<string?> RequestPasswordAsync(
        int fileIndex,
        string inputPath,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        if (_password != null)
            return await _password(fileIndex, inputPath, isRetry, cancellationToken).ConfigureAwait(false);
        if (_passwordResult != null)
        {
            var res = await _passwordResult(fileIndex, inputPath, isRetry, cancellationToken).ConfigureAwait(false);
            return res.IsPassword ? res.Password : null;
        }
        return null;
    }

    public async Task<PasswordPromptResult> RequestPasswordResultAsync(
        int fileIndex,
        string inputPath,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        if (_passwordResult != null)
            return await _passwordResult(fileIndex, inputPath, isRetry, cancellationToken).ConfigureAwait(false);
        if (_password != null)
        {
            string? pwd = await _password(fileIndex, inputPath, isRetry, cancellationToken).ConfigureAwait(false);
            return pwd is not null ? PasswordPromptResult.FromPassword(pwd) : PasswordPromptResult.Cancel;
        }
        return PasswordPromptResult.Skip;
    }

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

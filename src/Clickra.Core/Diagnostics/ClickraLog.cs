using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Clickra.Core.Application;
using Clickra.Core.Modules.Sys.Settings.Application;
using Clickra.Core.Storage;

namespace Clickra.Core.Diagnostics;

public sealed record RedactionContext(
    IReadOnlyList<string> KnownPaths,
    IReadOnlyList<string> Secrets);

/// <summary>
/// Centralized, non-blocking, redacted diagnostic logger shared by Desktop, CLI, and Core.
/// </summary>
public static partial class ClickraLog
{
    internal const string MutexName = "Local\\ClickraLogMutex_v1";
    internal const long MaxFileBytes = 5L * 1024 * 1024;
    internal const int RetentionDays = 14;
    private const int MutexWaitMs = 500;
    private const int ProcessExitFlushMs = 1000;
    private const int MaxExternalOutputChars = 2048;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly ConcurrentQueue<LogWriteItem> WriteQueue = new();
    private static readonly AutoResetEvent QueueSignal = new(false);
    private static readonly AsyncLocal<TaskScopeState?> CurrentTaskScope = new();

    private static volatile string _source = "core";
    private static int _workerStarted;

    static ClickraLog()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush(TimeSpan.FromMilliseconds(ProcessExitFlushMs));
    }

    public static string Source
    {
        get => _source;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _source = value.Trim();
            }
        }
    }

    public static string LogsDirectory => StorageRuntime.LogsDirectory;

    public static void Info(string area, string message) =>
        Enqueue("INFO", area, message, ex: null, directoryOverride: null);

    public static void Warn(string area, string message, Exception? ex = null) =>
        Enqueue("WARN", area, message, ex, directoryOverride: null);

    public static void Error(string area, string message, Exception? ex = null) =>
        Enqueue("ERROR", area, message, ex, directoryOverride: null);

    public static IDisposable BeginTask(
        string command,
        string taskId,
        IEnumerable<string>? knownPaths = null,
        ConversionPasswordCache? passwordCache = null)
    {
        TaskScopeState? previous = CurrentTaskScope.Value;
        var state = new TaskScopeState(command, taskId, knownPaths, passwordCache);
        CurrentTaskScope.Value = state;
        return new TaskScopeLease(previous);
    }

    public static void SetTaskFile(int fileIndex, int totalFiles, string? filePath)
    {
        TaskScopeState? scope = CurrentTaskScope.Value;
        if (scope is null) return;
        scope.SetFile(fileIndex, totalFiles, filePath);
    }

    public static string FormatOptionsSummary(IReadOnlyDictionary<string, object>? options)
    {
        if (options is null || options.Count == 0) return string.Empty;
        var parts = new List<string>(options.Count);
        foreach (KeyValuePair<string, object> pair in options)
        {
            string valueText = IsSensitiveOptionKey(pair.Key)
                ? "***"
                : Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            parts.Add($"{pair.Key}={valueText}");
        }
        return string.Join(", ", parts);
    }

    public static string Redact(string text, RedactionContext? ctx)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        string result = text;

        if (ctx is not null)
        {
            foreach (string secret in ctx.Secrets.Where(s => !string.IsNullOrEmpty(s)).OrderByDescending(s => s.Length))
            {
                result = result.Replace(secret, "***", StringComparison.Ordinal);
            }

            foreach (string path in ctx.KnownPaths.Where(p => !string.IsNullOrWhiteSpace(p)).OrderByDescending(p => p.Length))
            {
                string fileName = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(fileName))
                {
                    result = result.Replace(path, fileName, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        result = UrlWithQueryRegex().Replace(result, m =>
        {
            if (Uri.TryCreate(m.Value, UriKind.Absolute, out Uri? uri))
            {
                return $"{uri.Scheme}://{uri.Authority}";
            }
            return "<url>";
        });

        result = UncPathRegex().Replace(result, m => $"<path>\\{Path.GetFileName(m.Value)}");
        result = DrivePathRegex().Replace(result, m => $"<path>\\{Path.GetFileName(m.Value)}");
        return result;
    }

    public static string? GetPreferredLogFilePath() => GetPreferredLogFilePath(LogsDirectory);

    internal static string? GetPreferredLogFilePath(string logsDirectory)
    {
        try
        {
            if (!Directory.Exists(logsDirectory)) return null;
            string todayFile = Path.Combine(logsDirectory, $"clickra-{DateTime.Now:yyyy-MM-dd}.log");
            if (File.Exists(todayFile)) return todayFile;

            return Directory.GetFiles(logsDirectory, "clickra-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public static void Flush(TimeSpan? timeout = null)
    {
        if (Volatile.Read(ref _workerStarted) == 0 && WriteQueue.IsEmpty) return;
        TimeSpan wait = timeout ?? TimeSpan.FromSeconds(2);
        using var done = new ManualResetEventSlim(false);
        WriteQueue.Enqueue(LogWriteItem.FlushMarker(done));
        QueueSignal.Set();
        done.Wait(wait);
    }

    public static void CleanExpiredLogs(TimeSpan? maxAge = null) =>
        CleanExpiredLogs(LogsDirectory, maxAge ?? TimeSpan.FromDays(RetentionDays), DateTime.UtcNow);

    internal static void CleanExpiredLogs(string logsDirectory, TimeSpan maxAge, DateTime utcNow)
    {
        try
        {
            if (!Directory.Exists(logsDirectory)) return;
            DateTime cutoff = utcNow - maxAge;
            foreach (string file in Directory.GetFiles(logsDirectory, "clickra-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Best-effort log pruning; never fail startup.
                }
            }
        }
        catch
        {
            // Best-effort log pruning.
        }
    }

    internal static void EnqueueForTests(
        string level,
        string area,
        string message,
        Exception? ex,
        string logsDirectory,
        DateTimeOffset? timestamp = null) =>
        Enqueue(level, area, message, ex, logsDirectory, timestamp);

    internal static string ResolveLogFilePathForWrite(string logsDirectory, DateTime localDate, long incomingByteCount, long maxFileBytes)
    {
        string baseName = $"clickra-{localDate:yyyy-MM-dd}";
        string primary = Path.Combine(logsDirectory, $"{baseName}.log");
        if (!NeedsRotation(primary, incomingByteCount, maxFileBytes))
        {
            return primary;
        }

        for (int index = 1; index < 1000; index++)
        {
            string candidate = Path.Combine(logsDirectory, $"{baseName}.{index}.log");
            if (!NeedsRotation(candidate, incomingByteCount, maxFileBytes))
            {
                return candidate;
            }
        }

        return primary;
    }

    private static bool NeedsRotation(string filePath, long incomingByteCount, long maxFileBytes)
    {
        if (!File.Exists(filePath)) return false;
        long currentLength = new FileInfo(filePath).Length;
        return currentLength + incomingByteCount > maxFileBytes;
    }

    private static void Enqueue(
        string level,
        string area,
        string message,
        Exception? ex,
        string? directoryOverride,
        DateTimeOffset? timestampOverride = null)
    {
        try
        {
            EnsureWorkerStarted();
            TaskScopeState? scope = CurrentTaskScope.Value;
            RedactionContext redaction = BuildRedactionContext(scope);
            DateTimeOffset now = timestampOverride ?? DateTimeOffset.Now;
            string formatted = FormatEntry(now, level, area, scope, message, ex, redaction);
            WriteQueue.Enqueue(LogWriteItem.Entry(directoryOverride ?? LogsDirectory, now.LocalDateTime.Date, formatted));
            QueueSignal.Set();
        }
        catch
        {
            // Logging must never throw or disrupt callers.
        }
    }

    private static void EnsureWorkerStarted()
    {
        if (Interlocked.CompareExchange(ref _workerStarted, 1, 0) != 0) return;
        var thread = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = "ClickraLogWriter"
        };
        thread.Start();
    }

    private static void ProcessQueue()
    {
        while (true)
        {
            while (WriteQueue.TryDequeue(out LogWriteItem item))
            {
                if (item.FlushSignal is not null)
                {
                    item.FlushSignal.Set();
                    continue;
                }

                WriteEntrySafe(item.LogsDirectory!, item.LocalDate, item.FormattedText!);
            }

            QueueSignal.WaitOne(1000);
        }
    }

    private static void WriteEntrySafe(string logsDirectory, DateTime localDate, string formattedText)
    {
        try
        {
            Directory.CreateDirectory(logsDirectory);
            byte[] payload = Utf8NoBom.GetBytes(formattedText);
            using var mutex = new Mutex(false, MutexName);
            bool acquired = false;
            try
            {
                try
                {
                    acquired = mutex.WaitOne(MutexWaitMs);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }

                if (!acquired) return;

                string targetPath = ResolveLogFilePathForWrite(logsDirectory, localDate, payload.Length, MaxFileBytes);
                using var stream = new FileStream(targetPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(payload, 0, payload.Length);
            }
            finally
            {
                if (acquired)
                {
                    try { mutex.ReleaseMutex(); }
                    catch { /* Best-effort release */ }
                }
            }
        }
        catch
        {
            // Swallow all write failures; never retry or write to Console.Error.
        }
    }

    private static RedactionContext BuildRedactionContext(TaskScopeState? scope)
    {
        var secrets = new List<string>();
        try
        {
            string apiKey = SettingsController.TranslateAiApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey)) secrets.Add(apiKey);
        }
        catch
        {
            // Settings lookup is best effort during redaction.
        }

        if (scope is null)
        {
            return new RedactionContext([], secrets);
        }

        if (scope.PasswordCache is not null)
        {
            secrets.AddRange(scope.PasswordCache.GetSnapshot());
        }

        return new RedactionContext(scope.GetPathsSnapshot(), secrets);
    }

    private static string FormatEntry(
        DateTimeOffset timestamp,
        string level,
        string area,
        TaskScopeState? scope,
        string message,
        Exception? ex,
        RedactionContext redaction)
    {
        var sb = new StringBuilder();
        string headerScope = scope is not null
            ? scope.FormatHeaderScope()
            : area;

        sb.Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
          .Append(' ')
          .Append(level)
          .Append(" [")
          .Append(_source)
          .Append(' ')
          .Append(Environment.ProcessId)
          .Append("] ")
          .Append(headerScope)
          .Append("\r\n");

        if (!string.IsNullOrWhiteSpace(message))
        {
            AppendIndentedLines(sb, Redact(message, redaction));
        }

        if (ex is not null)
        {
            AppendExceptionDetails(sb, ex, redaction, isInner: false);
        }

        return sb.ToString();
    }

    private static void AppendExceptionDetails(StringBuilder sb, Exception ex, RedactionContext redaction, bool isInner)
    {
        string prefix = isInner ? "--- inner: " : string.Empty;
        string msg = Redact(ex.Message, redaction);
        sb.Append("  ")
          .Append(prefix)
          .Append(ex.GetType().FullName)
          .Append(" (0x")
          .Append(ex.HResult.ToString("X8", CultureInfo.InvariantCulture))
          .Append("): ")
          .Append(msg)
          .Append("\r\n");

        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
        {
            AppendIndentedLines(sb, Redact(ex.StackTrace, redaction));
        }

        AppendExternalProcessOutput(sb, ex, redaction);

        if (ex is AggregateException agg)
        {
            foreach (Exception inner in agg.InnerExceptions)
            {
                AppendExceptionDetails(sb, inner, redaction, isInner: true);
            }
        }
        else if (ex.InnerException is not null)
        {
            AppendExceptionDetails(sb, ex.InnerException, redaction, isInner: true);
        }
    }

    private static void AppendExternalProcessOutput(StringBuilder sb, Exception ex, RedactionContext redaction)
    {
        if (ex.Data.Contains("ExitCode"))
        {
            sb.Append("  exit code: ").Append(ex.Data["ExitCode"]).Append("\r\n");
        }

        string? output = ex.Data.Contains("StdErr")
            ? ex.Data["StdErr"] as string
            : ex.Data.Contains("Output") ? ex.Data["Output"] as string : null;

        if (string.IsNullOrWhiteSpace(output)) return;
        string redacted = Redact(output, redaction);
        if (redacted.Length > MaxExternalOutputChars)
        {
            redacted = redacted[..MaxExternalOutputChars] + "...(truncated)";
        }
        sb.Append("  output (max 2 KB): ").Append(redacted.Replace("\r\n", " ").Replace('\n', ' ')).Append("\r\n");
    }

    private static void AppendIndentedLines(StringBuilder sb, string text)
    {
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            sb.Append("  ").Append(line).Append("\r\n");
        }
    }

    private static bool IsSensitiveOptionKey(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("key", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("token", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"https?://[^\s""'<>]+\?[^\s""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlWithQueryRegex();

    [GeneratedRegex(@"\\\\[^\s""'<>|/]+\\[^\s""'<>|]+")]
    private static partial Regex UncPathRegex();

    [GeneratedRegex(@"\b[A-Za-z]:\\[^\s""'<>|]+")]
    private static partial Regex DrivePathRegex();

    private sealed class TaskScopeState
    {
        private readonly string _command;
        private readonly string _shortTaskId;
        private readonly List<string> _knownPaths;
        private readonly object _lock = new();
        private int _fileIndex = -1;
        private int _totalFiles;
        private string? _currentFileName;

        public TaskScopeState(
            string command,
            string taskId,
            IEnumerable<string>? knownPaths,
            ConversionPasswordCache? passwordCache)
        {
            _command = command;
            _shortTaskId = taskId.Length > 4 ? taskId[..4] : taskId;
            _knownPaths = knownPaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [];
            PasswordCache = passwordCache;
        }

        public ConversionPasswordCache? PasswordCache { get; }

        public void SetFile(int fileIndex, int totalFiles, string? filePath)
        {
            lock (_lock)
            {
                _fileIndex = fileIndex;
                _totalFiles = totalFiles;
                _currentFileName = string.IsNullOrWhiteSpace(filePath) ? null : Path.GetFileName(filePath);
                if (!string.IsNullOrWhiteSpace(filePath) && !_knownPaths.Contains(filePath))
                {
                    _knownPaths.Add(filePath);
                }
            }
        }

        public IReadOnlyList<string> GetPathsSnapshot()
        {
            lock (_lock) return _knownPaths.ToArray();
        }

        public string FormatHeaderScope()
        {
            lock (_lock)
            {
                string baseScope = $"{_command} task={_shortTaskId}";
                if (_fileIndex >= 0 && _totalFiles > 0)
                {
                    string filePart = !string.IsNullOrEmpty(_currentFileName)
                        ? $" file={_fileIndex + 1}/{_totalFiles} \"{_currentFileName}\""
                        : $" file={_fileIndex + 1}/{_totalFiles}";
                    return baseScope + filePart;
                }
                return baseScope;
            }
        }
    }

    private sealed class TaskScopeLease : IDisposable
    {
        private readonly TaskScopeState? _previous;
        private int _disposed;

        public TaskScopeLease(TaskScopeState? previous) => _previous = previous;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            CurrentTaskScope.Value = _previous;
        }
    }

    private readonly record struct LogWriteItem(
        string? LogsDirectory,
        DateTime LocalDate,
        string? FormattedText,
        ManualResetEventSlim? FlushSignal)
    {
        public static LogWriteItem Entry(string logsDirectory, DateTime localDate, string formattedText) =>
            new(logsDirectory, localDate, formattedText, null);

        public static LogWriteItem FlushMarker(ManualResetEventSlim signal) =>
            new(null, default, null, signal);
    }
}

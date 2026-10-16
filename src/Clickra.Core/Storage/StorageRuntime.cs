using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Clickra.Core.Storage;

/// <summary>Shared filesystem location and inter-process synchronization primitives for storage owners.</summary>
internal static class StorageRuntime
{
    internal const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    internal const string TaskFilePrefix = "task-";
    internal const string HistoryFileName = "history.log";

    internal static readonly object FileLock = new();

    static StorageRuntime()
    {
        string? overrideDir = Environment.GetEnvironmentVariable("CLICKRA_DATA_DIR");
        DataDirectory = !string.IsNullOrWhiteSpace(overrideDir)
            ? Path.GetFullPath(overrideDir)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clickra");

        SettingsFilePath = Path.Combine(DataDirectory, "settings.conf");
        HistoryFilePath = Path.Combine(DataDirectory, HistoryFileName);
        TasksDirectory = Path.Combine(DataDirectory, "tasks");

        try
        {
            Directory.CreateDirectory(DataDirectory);
        }
        catch
        {
            // Existing storage APIs are best effort during initialization; operations report their own failures.
        }
    }

    internal static string DataDirectory { get; }
    internal static string SettingsFilePath { get; }
    internal static string HistoryFilePath { get; }
    internal static string TasksDirectory { get; }

    internal static void RunWithMutex(Action action)
    {
        using var mutex = new Mutex(false, "Local\\ClickraStorageMutex_v1");
        bool acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(5000, false);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
                throw new TimeoutException("Storage mutex not acquired within 5 s; aborting to prevent concurrent file corruption.");
            action();
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    internal static T RunWithMutex<T>(Func<T> func)
    {
        using var mutex = new Mutex(false, "Local\\ClickraStorageMutex_v1");
        bool acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(5000, false);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
                throw new TimeoutException("Storage mutex not acquired within 5 s; aborting to prevent concurrent file corruption.");
            return func();
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    internal static Dictionary<string, string> ParseKeyValueLines(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            int index = line.IndexOf('=');
            if (index > 0)
                values[line[..index]] = line[(index + 1)..];
        }
        return values;
    }
}

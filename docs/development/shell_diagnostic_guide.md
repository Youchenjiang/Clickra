# Shell Extension Diagnostic Logging Guide

When debugging Shell Extension loading issues (like missing menu items), use a file-based logger to capture COM initialization and `QueryInterface` calls.

## Implementation Pattern

### 1. Logger Class
Implement a lightweight, fail-safe logger in `ShellExtension.cs`:

```csharp
internal static class Logger
{
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "ClickraShell.log");

    public static void Log(string message)
    {
        try
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.AppendAllText(LogPath, $"[{timestamp}] {message}{Environment.NewLine}");
        }
        catch { /* Fail silently */ }
    }
}
```

### 2. Key Instrumentation Points

*   **DllGetClassObject**: Log CLSID and requested IID to verify COM registration.
*   **QIInternal**: Log requested IIDs, especially failed ones, to identify missing interface support.
*   **GetTitle/Invoke**: Log entry to verify user interaction.

### 3. Analyzing Logs
Look for `QI FAILED` messages. Use a tool like `Guid Explorer` or search the web for the GUID to identify the interface name. Common interfaces:
*   `a08ce4d0-fa25-44ab-b57c-c7b3c3ef1cf0`: Standard `IExplorerCommand`
*   `c5740441-fa60-492d-944c-354313f8c7b6`: Standard `IEnumExplorerCommand`
*   Variations (e.g., ending in `c7b1c323e0b9`) often indicate system-specific or compatibility interface requests.

## Cleanup
**IMPORTANT**: Remove the Logger and all `Logger.Log` calls before shipping to production to avoid unnecessary disk I/O and potential privacy issues.

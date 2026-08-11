# Shell Extension Diagnostic Logging Guide

Use this guide when Explorer does not load `ClickraShell.dll`, the submenu is
missing, or a command is displayed but cannot be invoked. The goal is to prove
which stage failed instead of changing COM code blindly.

## 1. Fail-safe diagnostic logger

For local debugging only, use a logger that cannot take down the shell server:

```csharp
internal static class Logger
{
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "ClickraShell.log");

    public static void Log(string message)
    {
        try
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.AppendAllText(LogPath, $"[{timestamp}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never break Explorer's COM callback.
        }
    }
}
```

Instrument the smallest useful set of callbacks:

| Event | What it proves |
|---|---|
| `DllGetClassObject` | Explorer found and loaded the DLL. |
| `QueryInterface` | Which interface IDs Explorer requested. |
| `GetTitle` / `GetFlags` | The command object is valid. |
| `EnumSubCommands` / enumerator `Next` | The submenu can be discovered. |
| selection setter | The selected files reached the command object. |
| `Invoke` | Explorer dispatched the selected command. |

Log the requested IID and HRESULT, not file contents or credentials. Remove
the logger and all logging calls, or guard them behind a local-only switch,
before shipping because Explorer callbacks are latency-sensitive and logs may
contain user paths.

## 2. Investigation sequence

Run the checks in order:

1. Confirm that the package is installed and its publisher matches the
   manifest certificate.
2. Launch the packaged `Clickra.Fluent` dashboard directly. If it exits before
   showing a window, diagnose the app/runtime before touching shell code.
3. Activate the packaged app with one known command independently of Explorer.
4. Restart Explorer after reinstalling or changing the shell DLL.
5. Check whether `DllGetClassObject` appears in the log.
6. If it does, compare every requested IID with the values in
   [`src/ClickraShell/Guids.cs`](../../src/ClickraShell/Guids.cs).
7. If submenu callbacks appear but no item is shown, inspect the enumerator's
   `Next` result, item count, and `GetState`/selection-count logic.
8. If `Invoke` appears but no progress window opens, trace packaged activation
   and Fluent startup independently of Explorer.

Useful PowerShell commands:

```powershell
$log = Join-Path $env:TEMP "ClickraShell.log"
Get-Content $log -Tail 80
Select-String -Path $log -Pattern "DllGetClassObject|QI|EnumSubCommands|Invoke"
Remove-Item $log -ErrorAction SilentlyContinue
```

## 3. HRESULT quick reference

| HRESULT | Meaning | Typical Clickra cause |
|---|---|---|
| `0x00000000` (`S_OK`) | Success | Output pointer and ownership were set correctly. |
| `0x80004002` (`E_NOINTERFACE`) | Interface unavailable | Missing or mismatched IID handling. |
| `0x80004001` (`E_NOTIMPL`) | Intentionally unsupported | Valid for optional callbacks when output is cleared. |
| `0x80004005` (`E_FAIL`) | Unspecified failure | Inspect object lifetime, vtable, and HRESULT propagation. |
| `0x80070005` (`E_ACCESSDENIED`) | Access denied | Package, certificate, or filesystem permission issue. |
| `0x8007007E` (`ERROR_MOD_NOT_FOUND`) | Module not found | Missing DLL, dependency, or stale package install. |
| `0x80040154` (`CLASS_NOTREG`) | Class not registered | Identity/manifest/registration mismatch. |

Never return `S_OK` while leaving an output pointer unset. Clear output
pointers before branching and return `E_NOTIMPL` or `E_NOINTERFACE` when the
operation is not supported.

## 4. App starts but no window appears

If Explorer briefly shows a busy cursor and then nothing happens, first decide
whether the shell invocation failed or the Fluent process crashed before XAML
created a window.

1. Confirm whether shell logging reached `Invoke`.
2. Launch `Clickra.Fluent.exe` directly from the installed package.
3. Inspect Windows Event Viewer under **Application Error** and
   **.NET Runtime**.
4. Treat `MissingMethodException` (`0x80131513`) or `TypeLoadException`
   (`0x80131522`) involving `Microsoft.UI.Xaml` as a likely Windows App SDK
   projection/runtime mismatch.
5. Compare:
   - `Microsoft.WindowsAppSDK` in
     `src/Clickra.Fluent/Clickra.Fluent.csproj`;
   - `Microsoft.WindowsAppRuntime.<major>` and `MinVersion` in the layout
     manifest;
   - `Microsoft.WinUI.dll` in the layout with the resolved NuGet package.
6. Rebuild only through `scripts/build_msix.ps1`. It cleans stale Release
   output, aligns the copied runtime dependency for Windows App SDK 2.x, and
   preserves the SDK-generated `resources.pri`.

An unexpectedly small package or `Microsoft.WinUI.dll` is evidence of missing
or stale payload, not an optimization success.

### 2026-07 packaging incident

The observed symptom was a working context-menu command followed by no progress
window. Explorer reached `Invoke`, but `Clickra.Fluent` crashed before creating
the first XAML window. First-chance diagnostics reported missing WinUI methods,
and the packaged `Microsoft.WinUI.dll` did not match the resolved SDK payload.

The package had mixed stale Windows App SDK artifacts, and a later `makepri new`
step replaced Fluent's XAML resource index. The durable fix was to clean Fluent
Release output before publish, keep the SDK-generated `resources.pri`, align the
layout runtime dependency from the Fluent project version, and restart Explorer
after reinstalling the same package version.

## 5. Package and Explorer checks

The supported packaging path is [`scripts/build_msix.ps1`](../../scripts/build_msix.ps1),
which produces the NativeAOT shell DLL and the full MSIX layout. Do not use a
legacy registry-only installer to diagnose a Sparse Package failure; it tests a
different integration path.

When a package appears installed but Explorer never calls the DLL, check:

- the CLSID in both manifests matches `Guids.Clsid`;
- the package publisher matches the signing certificate;
- `ClickraShell.dll` and its side-by-side manifest are present in the layout;
- `Clickra.Fluent.exe`, `Microsoft.WinUI.dll`, and `resources.pri` are present;
- the layout Windows App Runtime dependency matches the Fluent project SDK;
- the package was rebuilt after changing the DLL;
- Explorer was restarted after reinstalling the package.

For `SignatureKind: None`, install the development certificate in the correct
current-user development store or rebuild with the project packaging script.
Avoid blindly adding certificates to the machine Root store.

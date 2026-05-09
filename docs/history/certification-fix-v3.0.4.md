# Fix Record: Windows Store Certification 10.1.2.10

## 1. Issue Description
The Clickra app failed certification because the context menu item did not appear on the tester's device (Surface Laptop 5, Windows 11 Build 22631). This was a silent failure with no visible error messages to the user.

## 2. Root Cause Analysis
Through file-based diagnostic logging, we identified two primary causes:

### A. CLSID Mismatch
The CLSID registered in the `AppxManifest.xml` (Sparse Package and MSIX) did not consistently match the `Clsid` defined in `ShellExtension.cs`. This caused the Shell to fail when searching for the COM server.

### B. Compatibility IID Requests
Windows 11 shells (especially on specific hardware or builds) may request `IExplorerCommand` or `IEnumExplorerCommand` using non-standard or alternative IIDs. 
*   **Requested**: `a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9` (Standard: `...c7b3c3ef1cf0`)
*   **Requested**: `a88826f8-186f-4987-aade-ea0cef8fbfe8` (Standard: `...c5740441...`)
The extension was rejecting these requests with `E_NOINTERFACE`, causing the menu to remain hidden or the sub-menu to fail to load.

## 3. Resolution Journey
1.  **Diagnostic Implementation**: Implemented a thread-safe `Logger` in `ShellExtension.cs` to write logs to `%TEMP%\ClickraShell.log`.
2.  **Reproduction**: Captured logs showing `CreateInstance` being called but `QueryInterface` failing for specific IIDs.
3.  **Code Correction**:
    *   Synchronized all CLSIDs to `B17A34D2-55E0-4D6F-8D1F-7A6E9C2B30A1`.
    *   Updated `QIInternal` to support the alternative IIDs discovered in the logs.
    *   Marked `GetModuleDir` as `unsafe` to fix NativeAOT compilation errors related to function pointers.
4.  **Verification**: Verified menu visibility and sub-menu functionality on local development machine.
5.  **Final Cleanup**: Removed logging and bumped version to `3.0.4.0` for resubmission.

## 4. Key Takeaway
Never assume a standard IID is the only one the shell will request. Always log `QueryInterface` requests when diagnosing "invisible" COM extensions.

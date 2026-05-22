# Local Build Notes

## Windows SDK Tools
This project requires `makepri.exe` and `makeappx.exe`. On this machine, they are located at:
`C:\Windows Kits\10\bin\10.0.26100.0\x64`

The `scripts/build_msix.ps1` script has been updated to automatically detect this path.

## Packaging Requirements
- Version revision number (4th digit) MUST be 0 for Microsoft Store.
- App must handle zero-argument launch without crashing (handled in `Clickra.CLI/Program.cs`).

## v3.0.8 Technical Context (For AI Handoff)
- **Architecture**: The entire project (`ClickraShell` and `Clickra.CLI`) is now **100% Native AOT**. Standard WinForms/WPF cannot be used.
- **UI Rendering**: The Dashboard (`DashboardForm.cs` / `DashboardWindow.cs`) and Progress Window (`ProgressWindow.cs`) use raw Win32 APIs (`CreateWindowExW`) and GDI+.
  - *Rule 1*: Always use `W` (Unicode) suffixed APIs and `Marshal.StringToHGlobalUni` to prevent MSIX title bar truncation (the "C" bug).
  - *Rule 2*: Do not attempt Mica/Acrylic rendering. We use a solid `#202020` dark background because GDI+ cannot blend with DWM Mica properly.
  - *Rule 3*: ProgressWindow uses `WM_TIMER` (16ms) to drive smooth cubic easing animations and shimmer glow overlays. It dynamically fetches system accent color via `DwmGetColorizationColor`.
- **Office Detection in MSIX**: Standard `Type.GetTypeFromProgID` fails inside the MSIX container. You must use direct registry checks (`HKLM\SOFTWARE\Classes\PowerPoint.Application`).
- **Cross-process Sync & Locking**:
  - We use file-based IPC (`active.tmp`) instead of in-memory lists to track active jobs across the shell extension process (`explorer.exe`) and CLI process.
  - Named Mutexes (`Global\Clickra_Language_Mutex`) and file locks are utilized to normalize language and prevent settings race conditions in MSIX.
- **Code Layout Restrictions (`ClickraStorage.cs`)**:
  - Avoid moving private helper methods (`WriteActiveFileInternal`/`ReadActiveFileInternal`) above public active record methods. Keep the original method ordering to prevent noisy, massive diff blocks in git history.

## How to Build (Manual Compilation)
Since we use asset embedding, the build is a two-stage process:

1.  **Stage 1: Build the Shell Extension (DLL)**:
    ```powershell
    dotnet publish src\ClickraShell\ClickraShell.csproj -c Release -r win-x64 -p:PublishAot=true --output .
    ```
2.  **Stage 2: Build the Main App (CLI)**:
    This embeds the DLL and assets from `src/resources` into the final executable using NativeAOT:
    ```powershell
    dotnet publish src\Clickra.CLI\Clickra.csproj -c Release -r win-x64 --output .
    ```

## Automated Packaging & Versioning Scripts
The project provides built-in PowerShell scripts for automated version bumping and MSIX packaging:

*   **Bump Version & Build**:
    ```powershell
    powershell -File scripts/bump_version.ps1 -Type <major|minor|patch|revision> -Build
    ```
    *   `-Type`: Specifies the version component to increment (e.g. `patch` increments `3.0.6.0` to `3.0.7.0`).
    *   `-Build`: Automatically triggers the Native AOT two-stage build, compiles PRI resources, signs the package, and generates the final `Clickra.msix` in the root directory.
*   **Standalone MSIX Packaging**:
    If you only want to rebuild the MSIX package without bumping the version:
    ```powershell
    powershell -File scripts/build_msix.ps1
    ```

## How to Add New Features
1.  **Core Logic**: Add new command handling in `src/Clickra.CLI/Program.cs`.
2.  **UI Menu**: Modify the `SubTitles` and `SubArgs` arrays in `src/ClickraShell/ShellExtension.cs`.
3.  **Re-build**: Re-publish `Clickra.exe` following the build sequence above.

## Git & Development Workflow
To keep the `main` branch stable, direct pushes to `main` are prohibited. All changes must be made via **Pull Requests (PR)**.
*   **Feature Development (`feature/...`)**: For all new features or bug fixes, branch off from `main` to `feature/<branch-name>`.
*   **Hotfixes (`hotfix/...`)**: For urgent bugs in the released version, branch off to `hotfix/<branch-name>`.
*   **Merge Rules**: Once development is done, push your branch and open a PR. Merge into `main` after review.
*   **Git Tags & Releases**: When releasing a new version, create a tag locally with the format `vX.Y.Z.0`. Push the tag directly using `git push origin vX.Y.Z.0`. Direct branch pushes to the remote main/release branches are strictly prohibited.

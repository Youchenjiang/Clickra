# Local Build Notes

## Windows SDK Tools
This project requires `makepri.exe` and `makeappx.exe`. On this machine, they are located at:
`C:\Windows Kits\10\bin\10.0.26100.0\x64`

The `scripts/build_msix.ps1` script has been updated to automatically detect this path.

## Packaging Requirements
- Version revision number (4th digit) MUST be 0 for Microsoft Store.
- App must handle zero-argument launch without crashing (handled in `Clickra.CLI/Program.cs`).

## v3.2.0 Technical Context (For AI Handoff)
- **Architecture**: The entire project (`ClickraShell` and `Clickra.CLI`) is now **100% Native AOT**. Standard WinForms/WPF cannot be used.
- **UI Rendering**: The Dashboard (`DashboardWindow*.cs`) and Progress Window (`ProgressWindow.cs`) use raw Win32 APIs (`CreateWindowExW`) and GDI+.
  - *Rule 1*: Always use `W` (Unicode) suffixed APIs and `Marshal.StringToHGlobalUni` to prevent MSIX title bar truncation (the "C" bug).
  - *Rule 2*: Do not attempt Mica/Acrylic rendering. We use a solid `#202020` dark background because GDI+ cannot blend with DWM Mica properly.
  - *Rule 3*: ProgressWindow uses `WM_TIMER` (16ms) to drive smooth cubic easing animations and shimmer glow overlays. It dynamically fetches system accent color via `DwmGetColorizationColor`.
  - *Rule 4*: For fonts on high-DPI screens, always initialize with `GraphicsUnit.Pixel` to prevent double/quadratic scaling (overlapping text), and use language-adaptive font names.
  - *Rule 5 (v3.2.0)*: The history record table layout dynamically calculates column widths (Adaptive layout) and uses custom-drawn text truncation to prevent column contents from overlapping.
- **Translation Languages (v3.2.0)**:
  - Redundant translation targets (English, Japanese, Korean, Simplified Chinese) have been removed from the Settings panel. Only the active dropdown and the functional "Traditional Chinese" (繁體中文) option are retained.
- **Office Detection in MSIX**: Standard `Type.GetTypeFromProgID` fails inside the MSIX container. You must use direct registry checks (`HKLM\SOFTWARE\Classes\PowerPoint.Application`).
- **Cross-process Sync & Locking**:
  - We use explicit argument passing for command tags and start times (`CompleteActiveRecord` signature in `ClickraStorage.cs` / `ClickraCli.cs`) instead of reading `active.tmp` inside the storage class, resolving concurrency race conditions.
  - Named Mutexes (`Global\Clickra_Language_Mutex`, `Global\Clickra_Dashboard_Mutex`) and file locks are utilized to normalize language, enforce single-instance dashboard check, and prevent settings race conditions in MSIX.
- **UI Interactions & Horizontal Scrollbars**:
  - Minimize-to-tray is supported in the progress window via `WM_SYSCOMMAND` + `SC_MINIMIZE`.
  - Conversions can be aborted via `WM_CLOSE` and prompt confirmation, terminating spawned PowerShell Office background processes using process tree kill.
  - Horizontal scrolling is supported in history card details and progress status overflow text using custom-drawn scrollbars, dragging thumb tracking, `WM_MOUSEWHEEL` handling, and GDI+ clipping regions.
- **Code Layout Restrictions (`ClickraStorage.cs`)**:
  - Avoid moving private helper methods (`WriteActiveFileInternal`/`ReadActiveFileInternal`) above public active record methods. Keep the original method ordering to prevent noisy, massive diff blocks in git history.

## v3.3.0 Technical Context (For AI Handoff)
- **Inline Password Input (decrypt-pdf)**:
  - The password prompt for `decrypt-pdf` is rendered **inline** inside the `ProgressWindow` Win32 window — **not** as a separate dialog. Child `EDIT` (with `ES_PASSWORD`) and `BUTTON` controls are created via `CreateWindowExW` directly on `_hwnd`.
  - `WS_CLIPCHILDREN` is set on the parent window to prevent GDI+ paint calls from overwriting the child controls and causing flickering.
  - The main message loop uses `IsDialogMessageW` to enable Tab/Enter/Esc navigation for the child controls, and `TranslateMessage` is called for all non-dialog messages to ensure `WM_CHAR` is generated and text can be typed.
  - The `EDIT` control is subclassed via `SetWindowLongPtrW(GWL_WNDPROC)` to intercept `VK_RETURN` (submit) and `VK_ESCAPE` (cancel) key presses.
  - Cross-thread signaling: the background processing thread calls `PostMessageW(WM_USER_SHOW_PASSWORD_INPUT)` and then blocks on `_passwordEvent.WaitOne()`. The UI thread handles the event, creates the controls, and signals back via `_passwordEvent.Set()` after the user confirms or cancels.
  - `WM_CTLCOLOREDIT` (0x0133) is handled to paint the edit control background `#2D2D2D` with white foreground text to match the dark theme.
  - Encryption pre-check: `decrypt-pdf` validates whether the input PDF is actually encrypted before prompting. Unencrypted files display a red-cross error message and abort without prompting.
- **bump_version.ps1 Encoding Fix**:
  - All file read/write operations in `bump_version.ps1` now use `[System.IO.File]::ReadAllText` / `::WriteAllText` with `New-Object System.Text.UTF8Encoding($false)` (no-BOM UTF-8). This prevents PowerShell 5.1's default ANSI encoding and `[System.Text.Encoding]::UTF8`'s implicit BOM from corrupting Markdown and XML files.

## How to Build (Manual Compilation)
Since we use asset embedding, the build is a two-stage process:

1.  **Stage 1: Build the Shell Extension (DLL)**:
    ```powershell
    dotnet publish src\ClickraShell\ClickraShell.csproj -c Release -r win-x64 -p:PublishAot=true --output .
    ```
2.  **Stage 2: Build the Main App (CLI)**:
    This embeds the DLL and assets from `src/resources` into the final executable using NativeAOT:
    ```powershell
    dotnet publish src\Clickra.CLI\Clickra.csproj -c Release -r win-x64 -p:PublishAot=true --output .
    ```

## Automated Packaging & Versioning Scripts
The project provides built-in PowerShell scripts for automated version bumping and MSIX packaging:

*   **Bump Version & Build**:
    By default, the script increments the `patch` version (3rd digit) and resets the `revision` (4th digit) to `0` for Microsoft Store compatibility.
    ```powershell
    powershell -File scripts/bump_version.ps1 -Build
    ```
    *   `-Type`: Optional. Specifies the version component to increment (`major`, `minor`, `patch`). Avoid using `revision` as it generates a non-zero 4th version digit, which is rejected by the Microsoft Store.
    *   `-Build`: Automatically triggers the Native AOT two-stage build, compiles PRI resources, signs the package, and generates the final `Clickra.msix` in the root directory.
*   **Standalone MSIX Packaging**:
    If you only want to rebuild the MSIX package without bumping the version:
    ```powershell
    powershell -File scripts/build_msix.ps1
    ```
    > [!NOTE]
    > **Automated Certificate Validation**: The packaging script automatically checks whether the local `ClickraDev.pfx` matches the Publisher identity defined in `AppxManifest.xml` (e.g. `CN=CBF59877-21AD-4BC4-8F91-FE8DA520A138`). If it detects a mismatch or if the certificate is missing, it will automatically call `scripts/setup/create_dev_cert.ps1` to regenerate a matching certificate. You do not need to manually manage local development PFX certs.

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

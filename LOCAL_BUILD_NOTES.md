# Local Build Notes

## Windows SDK Tools
This project requires `makepri.exe` and `makeappx.exe`. On this machine, they are located at:
`C:\Windows Kits\10\bin\10.0.26100.0\x64`

The `scripts/build_msix.ps1` script has been updated to automatically detect this path.

## Packaging Requirements
- Version revision number (4th digit) MUST be 0 for Microsoft Store.
- App must handle zero-argument launch without crashing (handled in `Clickra.CLI/Program.cs`).

## Technical Context (For AI Handoff)
- **Architecture**: The entire project (`ClickraShell` and `Clickra.CLI`) is now **100% Native AOT**. Standard WinForms/WPF cannot be used.
- **UI Rendering**: The Dashboard (`DashboardWindow*.cs`) and Progress Window (`ProgressWindow.cs`) use raw Win32 APIs (`CreateWindowExW`) and GDI+.
  - *Rule 1*: Always use `W` (Unicode) suffixed APIs and `Marshal.StringToHGlobalUni` to prevent MSIX title bar truncation (the "C" bug).
  - *Rule 2*: Do not attempt Mica/Acrylic rendering. We use a solid `#202020` dark background because GDI+ cannot blend with DWM Mica properly.
  - *Rule 3*: ProgressWindow uses `WM_TIMER` (16ms) to drive smooth cubic easing animations and shimmer glow overlays. It dynamically fetches system accent color via `DwmGetColorizationColor`.
  - *Rule 4*: For fonts on high-DPI screens, always initialize with `GraphicsUnit.Pixel` to prevent double/quadratic scaling (overlapping text), and use language-adaptive font names.
  - *Rule 5*: The history record table layout dynamically calculates column widths (Adaptive layout) and uses custom-drawn text truncation to prevent column contents from overlapping.
- **Translation Languages**:
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

## Inline Password Input Technical Context
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

Adding a new conversion command (e.g. `excel2pdf`) requires changes across **6 layers and 13+ files**. Missing any step will cause silent failures — the feature may compile but not appear in the right-click menu, not execute, or produce no output.

### Checklist: Adding a New Conversion Command

Use this checklist every time you add a new file conversion command. The Excel to PDF implementation (`feature/excel-to-pdf`) serves as the reference example.

---

#### Layer 1: Core Processor

| # | File | What to do |
|---|------|------------|
| 1 | `src/Clickra.Core/Processors/<New>Processor.cs` | Create processor class extending `SingleFileProcessorBase` or `MultiFileProcessorBase`. Implement `GetOutputSuffix()` and `ProcessSingleFile()`. |
| 2 | `src/Clickra.Core/FileProcessor.cs` | Add a public static method (e.g. `ConvertExcelToPdf`) that instantiates your processor and calls `Process()`. |

#### Layer 2: CLI Command Routing

| # | File | What to do |
|---|------|------------|
| 3 | `src/Clickra.CLI/Cli/ClickraCli.cs` | Add `case "newcmd":` in the main switch (line ~113). Validate extensions, call `FileProcessor.ConvertXxx()` in quiet mode or `ProgressWindow.Show()` in GUI mode. |
| 4 | `src/Clickra.CLI/Cli/ClickraCli.cs` | **Line 46**: Add your command to the help/version string: `"Commands: ppt2pdf, word2pdf, newcmd, ..."` |
| 5 | `src/Clickra.CLI/Cli/ClickraCli.Arguments.cs` | **Line 37-44**: Add your command to the `ExpandDirectoryArguments()` switch: `"newcmd" => new[] { ".ext1", ".ext2" }`. Without this, passing a folder as CLI input silently returns 0 files. |

#### Layer 3: Progress Window (GUI Execution)

| # | File | What to do |
|---|------|------------|
| 6 | `src/Clickra.CLI/Progress/ProgressWindow.Process.cs` | **Line ~58 switch**: Add `case "newcmd":` calling `FileProcessor.ConvertXxx()`. **Without this, right-click conversion does NOTHING** — the progress window shows "completed" but no file is created. |
| 7 | `src/Clickra.CLI/Progress/ProgressWindow.Process.cs` | **Line ~232 `GetOutputPath()`**: Add your command to the output path switch so history logs the correct output file path. |

#### Layer 4: Dashboard UI

| # | File | What to do |
|---|------|------------|
| 8 | `src/Clickra.CLI/Dashboard/DashboardWindow.State.cs` | Add your command string to the `ConvertCommands` array at the correct index. |
| 9 | `src/Clickra.CLI/Dashboard/DashboardWindow.Convert.cs` | **Line ~105 `HandleDroppedFiles()`**: Add an `else if` branch for your file extensions so drag-and-drop auto-selects the correct card. |
| 10 | `src/Clickra.CLI/Dashboard/DashboardWindow.Paint.Overview.cs` | **Line ~32**: Add a `DrawEngineRow()` call for your engine status (e.g. `IsOfficeInstalled("Excel")`). Also shift the Statistics section Y-coordinates down by ~20px to make room. |
| 11 | `src/Clickra.CLI/Dashboard/DashboardWindow.Paint.About.cs` | **Line ~182 `IsOfficeInstalled()`**: If your engine uses Office COM, add a new case to the progId switch (e.g. `"Excel" => "Excel.Application"`). |
| 12 | `src/Clickra.CLI/Dashboard/DashboardWindow.Paint.History.cs` | Add `case "newcmd":` in the history tag color switch with an appropriate color. |

#### Layer 5: Localization (5 Languages)

| # | File | What to do |
|---|------|------------|
| 13 | `src/Clickra.Core/Localization/Localization.cs` | Add keys in **all 5 language dictionaries** (zh-TW, zh-CN, en-US, ja-JP, ko-KR): `cmd_xxx` (card label), `engine_xxx` (status row label). |

#### Layer 6: Windows Shell Extension (Right-Click Menu)

| # | File | What to do |
|---|------|------------|
| 14 | `src/ClickraShell/ComMethods.cs` **Line 26 `MenuKeys`** | Add your menu resource key (e.g. `"Menu_NewCmd"`) at the correct index. |
| 15 | `src/ClickraShell/ComMethods.cs` **Line 27 `SubArgs`** | Add your CLI sub-command (e.g. `"newcmd"`) at the **same index** as MenuKeys. |
| 16 | `src/ClickraShell/ComMethods.cs` **Line 97-111 `IsSupported()`** | Add a case for your index mapping file extensions. |
| 17 | `src/ClickraShell/ComMethods.cs` **Line 125-128 `GetState()`** | If your command requires a minimum file count (e.g. merge requires 2+), add it to the `countOk` switch. **WARNING**: After inserting a new command, ALL existing indices shift — update the indices for existing commands too. |
| 18 | `packaging/msix/Strings/*/Resources.resw` (5 files) | Add `Menu_NewCmd` resource string in all 5 languages (en-us, zh-tw, zh-cn, ja-jp, ko-kr). |

---

### Common Pitfalls (Lessons Learned)

| Pitfall | Symptom | Root Cause |
|---------|---------|------------|
| Missing `ProgressWindow.Process.cs` case | Right-click shows progress then "completed" but **no output file created** | Switch falls through without executing any code |
| Missing `ExpandDirectoryArguments` case | CLI: `Clickra newcmd C:\Folder` returns "找不到可處理的檔案" | Directory expansion yields 0 files for unknown commands |
| Wrong `GetState` indices | Menu item doesn't appear or appears for wrong file types | Inserting a new command shifts all subsequent indices |
| Missing `engine_xxx` key | Dashboard crashes or shows raw key name | Localization dictionary missing key for all 5 languages |
| Missing `GetOutputPath` case | History log shows output path as directory instead of file | Default case returns `outputDir` instead of specific file path |
| Missing `IsOfficeInstalled` progId | Engine status always shows "未安裝" even when Office is installed | Registry check uses wrong ProgID |

### Index Management Rule

The `ComMethods.cs` arrays (`MenuKeys`, `SubArgs`) and the `IsSupported()`/`GetState()` index switches are **positionally coupled**. When you insert a new command at index N:

1. All commands at index >= N shift right by 1
2. Update `IsSupported()` cases for all shifted commands
3. Update `GetState()` count-check cases for all shifted commands
4. Update `DashboardWindow.State.cs` `ConvertCommands` array order to match
5. Update `DashboardWindow.Convert.cs` `HandleDroppedFiles()` index references

**Always verify the index mapping by counting from 0 after your change.**

## Git & Development Workflow
To keep the `main` branch stable, direct pushes to `main` are prohibited. All changes must be made via **Pull Requests (PR)**.
*   **Feature Development (`feature/...`)**: For all new features or bug fixes, branch off from `main` to `feature/<branch-name>`.
*   **Hotfixes (`hotfix/...`)**: For urgent bugs in the released version, branch off to `hotfix/<branch-name>`.
*   **Merge Rules**: Once development is done, push your branch and open a PR. Merge into `main` after review.
*   **Git Tags & Releases**: When releasing a new version, create a tag locally with the format `vX.Y.Z.0`. Push the tag directly using `git push origin vX.Y.Z.0`. Direct branch pushes to the remote main/release branches are strictly prohibited.

### Pull Request Convention

**Title** — follow Conventional Commits, keep under 50 chars:

```
<type>(<scope>): <description>
```

- For release PRs, include version: `feat(pdf): v3.4.0.0 - Excel to PDF conversion`
- Scope = primary area changed (`pdf`, `cli`, `shell`, `ui`, `i18n`, `build`, `docs`), not always `release`
- Always English, no mixed languages

**Description** — required for every PR, even 1-line fixes. Structure by PR size:

| Size | Files | Structure |
|------|-------|-----------|
| Small | <10 | `## Summary` (1-2 sentences) + numbered list |
| Medium | 10-50 | `## Summary` + `## Key Changes` (bullets grouped by area) |
| Large | 50+ | `## Overview` + `## Key Changes` (numbered sections with subsections) + `## Verification` |

**Section rules**:

| Section | Rules |
|---------|-------|
| `## Summary` / `## Overview` | 1-2 sentences: what changed + why. No file names. |
| `## Key Changes` | Group by area (Core / CLI / UI / Docs). Use `*` bullets with technical detail (API names, file names) for significant changes. |
| `## Verification` | Checklist `[x]` format. Describe how changes were verified (build, manual QA, etc.) |
| `## Notes` | Optional. Add when there's a non-obvious decision the reviewer should know. |

**Language**: Always English, consistent with title.

**What to avoid** (from past mistakes):
- Empty body (PR #1, #3)
- Title over 50 chars (PR #6 was 46)
- Mixed Chinese/English (PR #2)
- Using `####` for top-level sections (PR #10)
- Scope always `release` regardless of content (PR #12)
- Marketing language like "Key Achievements" (PR #4, use "Key Changes" instead)

### Commit Message Convention

This project follows [Conventional Commits](https://www.conventionalcommits.org/). Every commit message must use the format:

```
<type>(<scope>): <description>
```

**Type** (required):

| Type | When to use |
|------|-------------|
| `feat` | A new feature (new command, new UI element, new processor) |
| `fix` | A bug fix (correct logic, fix crash, fix layout) |
| `refactor` | Code restructuring without changing behavior (extract method, rename, split file) |
| `docs` | Documentation only (README, ROADMAP, inline comments) |
| `test` | Adding or updating tests |
| `chore` | Build scripts, CI/CD, dependency updates, version bumps |
| `style` | Formatting, whitespace, no logic change |
| `perf` | Performance improvement |

**Scope** (optional but recommended):

| Scope | Area |
|-------|------|
| `core` | `Clickra.Core` (processors, storage, localization) |
| `cli` | `Clickra.CLI` (dashboard, progress window, CLI args) |
| `pdf` | PDF translation pipeline specifically |
| `shell` | `ClickraShell` (Windows context menu, COM) |
| `ui` | Dashboard/Progress window rendering |
| `font` | Font resolver and font utilities |
| `i18n` | Localization keys and resources |
| `build` | Build scripts, MSIX packaging |
| `release` | Version bumps, changelog, store listings |
| `tests` | Test suites and test infrastructure |

**Examples**:
```
feat(excel): add Excel to PDF conversion processor
fix(shell): correct GetState index offset after inserting excel2pdf
refactor(pdf): extract paragraph analysis helpers
docs(roadmap): update v3.3.3.0 milestones
chore(deps): update NuGet packages to latest versions
```

**Rules**:
- Description must be in **English**, lowercase after the colon, no period at the end
- One commit = one logical change (atomic commits)
- Use `git mv` for file renames, never delete-then-recreate

### Commit Granularity Rules

Each commit must contain **only one type of change**. Do NOT mix different change types in a single commit.

**By change type — separate commits for each**:

| Change type | What it means | Example |
|-------------|---------------|---------|
| **Add** | Adding new files or new code to existing files | `feat(excel): add ExcelToPdfProcessor` |
| **Modify** | Changing existing logic without adding/removing files | `fix(shell): correct GetState index offset` |
| **Delete** | Removing files or removing code from existing files | `refactor(cli): remove obsolete ClickraCli.cs` |

**Refactoring — split into multiple commits**:

When refactoring that involves **deleting whole files with no replacement** (e.g. removing a deprecated module), that's its own commit:

```
# CORRECT — delete standalone, add new standalone:
refactor(pdf): remove monolithic FileProcessor
  (deletes FileProcessor.cs)

feat(pdf): add individual processor classes
  (adds ImageToPdfProcessor, PdfMergeProcessor, etc.)
```

When **moving/extracting code between files** (the code still exists, just relocated), this is ONE logical change → **ONE commit**:

```
# CORRECT — one commit, two file changes:
refactor(pdf): extract paragraph helpers to PdfParagraphAnalysis
  (modifies FileProcessor.cs to remove the method AND adds PdfParagraphAnalysis.cs with the method)
```

The key question: **does the code still exist after the change?**
- Yes → one commit (move/extract)
- No → separate commit (delete)

**By concern — separate commits for each**:

If a change touches multiple concerns (e.g. refactoring + fixing a bug + adding a feature), split into separate commits:

```
# WRONG — three concerns in one commit:
feat(excel): add excel2pdf, fix GetState index, add engine status

# CORRECT — three separate commits:
feat(excel): add Excel to PDF conversion support
fix(shell): correct GetState index offset after inserting excel2pdf
feat(dashboard): add Excel engine status row to overview tab
```

**Quick reference**:

| Scenario | How to commit |
|----------|---------------|
| Rename a file | `git mv` → 1 commit: `refactor(core): rename X to Y` |
| Rename + change logic | 2 commits: first rename, then modify logic |
| Extract method from A to new file B | 1 commit: `refactor(core): extract X from A to B` |
| Delete entire file + add replacement (same code, different location) | 1 commit: `refactor(core): move X to Y` |
| Delete file with no replacement | 1 commit: `refactor(core): remove unused X` |
| Add new feature with 3 files | 1 commit if same concern: `feat(excel): add Excel to PDF support` |
| Add feature + fix unrelated bug | 2 commits: one `feat`, one `fix` |

---

## Partner Center API — Known Behaviors & Gotchas

These notes were discovered through extensive testing of `scripts/publish_store.py`
against the Microsoft Store Submission API (`manage.devcenter.microsoft.com`).

### Authentication
- OAuth2 client_credentials flow with resource `https://manage.devcenter.microsoft.com`.
- Token TTL is ~60 min; the script acquires once per run.

### Submission Lifecycle (Status Flow)
```
(POST) → Draft → PendingCommit → CommitStarted → PreProcessing → Certification → Published
                                                                   ↘ CommitFailed
```
- **CommitStarted ≠ submitted.** It only means the backend *started* processing the
  commit.  The script MUST poll until `PreProcessing` / `Certification` / `Published`
  to confirm real acceptance.  Checking for `CommitStarted` alone is a false positive.
- The API returns `202` with `{"status":"CommitStarted"}` immediately — this is async.
- Typical processing time: 15–30 minutes.  The post-commit verification loop runs
  9 checks × 20 s = 3 min; it may time out before the backend finishes, which is
  normal and not a failure.

### Creating a Submission (POST)
- `POST /v1.0/my/applications/{productId}/submissions` with **empty body** (`b''`).
  - Using `b'{}'` causes HTTP 400: *"The size of Listings must be 1 or more"*.
- The response clones the last *published* version's listings and packages.
- Returns `fileUploadUrl` (Azure Blob SAS URL) for the ZIP upload.
- **409 Conflict** means a pending submission already exists — delete it first.

### Uploading the Package (PUT to SAS URL)
- Upload a single ZIP containing: MSIX + screenshots + any other assets.
- All files go to the root of the ZIP (no subdirectories).
- Response: `201 Created`.

### Updating Metadata (PUT)
- `PUT /v1.0/my/applications/{productId}/submissions/{submissionId}` with the full
  submission JSON.
- **Key casing**: The API returns **camelCase** keys (`releaseNotes`, `baseListing`,
  `images`), NOT PascalCase (`ReleaseNotes`, `BaseListing`, `Images`).  The script
  must match camelCase when reading/writing to avoid creating duplicate keys.
- PUT response `200` means accepted; does not guarantee persistence — the commit
  may still reject changes.

### Package References (`applicationPackages`)
- The cloned submission has one package with `fileStatus: "Uploaded"` and a real `id`.
- The API **requires** keeping existing package entries.  You must:
  1. Copy each existing package, set `fileStatus: "PendingDelete"` (preserve `id`).
  2. Append a new entry: `{fileName: "Clickra.msix", fileStatus: "PendingUpload"}`.
- Omitting old entries → HTTP 400: *"Please keep all file entries for existing packages."*
- During commit, the backend removes the old package and processes the new one.
  Both entries may temporarily coexist with the same filename — this is normal.

### Screenshot / Image References (`images` in `baseListing`)
- **Key behavior**: The API **silently ignores** `PendingDelete` on screenshots that
  have `fileStatus: "Uploaded"` from a previous published submission.  You cannot
  replace or delete them via the API.
- **Working strategy**: Keep all existing images untouched and **append** new
  `PendingUpload` screenshots.  The backend uploads the new ones and gives them real
  IDs.  Old screenshots remain alongside.
- **Cleanup**: Old/redundant screenshots must be removed manually in the Partner
  Center UI.
- Screenshot requirements: `.png` format, ≤ 50 MB, recommended ≥ 1366×768 pixels.
  Smaller sizes (e.g. 1140×733) are accepted but not ideal.

### `zh-cn` Listing
- The cloned submission may not include a `zh-cn` listing.
- The script creates one via `add_missing_zh_cn()` (deep-copied from `zh-tw` with
  cleared content).  This works because the new listing has no pre-existing screenshots,
  so the API accepts `PendingUpload` entries without conflict.

### Retry / Timeout
- Microsoft's ingestion gateway is slow and unreliable.  GET requests frequently
  time out (504) or take 60–180 seconds.
- The `api_request()` helper uses exponential backoff (5 retries, starting at 15 s).
- For critical calls (GET submission, PUT metadata), a minimum timeout of 180 s
  is recommended.
- DELETE also times out frequently; retry with 15 s intervals.

### Error Patterns
| HTTP Code | Meaning | Action |
|-----------|---------|--------|
| 400 | Bad request (missing listings, missing package IDs, etc.) | Fix the JSON payload |
| 409 | Conflict — pending submission exists | Delete existing submission first |
| 504 | Gateway timeout | Retry after 15–30 s |
| 202 | Accepted (commit started) | Normal — poll for final status |

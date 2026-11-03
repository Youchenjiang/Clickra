# New Convert Command End-to-End Wiring Checklist

When introducing a new conversion capability or command into Clickra, follow this 6-stage lifecycle to ensure end-to-end completeness, architectural compliance, and 100% test guard coverage.

---

## 1. Core Module Implementation (`Clickra.Core/Modules/<Module>/`)

- [ ] **Application Layer**: Create `Application/<Command>UseCase.cs` inheriting from `PerFileConversionUseCaseBase` or `SingleOutputConversionUseCaseBase`.
  - Declare a public constant `public const string CommandName = "<command-id>";`.
- [ ] **Processing Layer**: Create `Processing/<Command>Processor.cs` inheriting from `SingleFileProcessorBase` or `MultiFileProcessorBase`.
  - Implement conversion logic with cancellation and progress reporting.
- [ ] **Unit Tests**: Add unit tests in `tests/Clickra.Core.Tests/TestSuite.<Module>.cs`.

---

## 2. Central Use Case Registration (`Clickra.Core/Application/`)

- [ ] Register the new use case in `src/Clickra.Core/Application/ConversionUseCases.cs`:
  - Add instance to `Registry` in `ConversionUseCases`.
  - Verify `ConversionUseCases.TryGet(command, ...)` and `ConversionUseCases.Commands` include the new command ID.

---

## 3. Shared Metadata & Visual Governance (`src/Shared/`)

- [ ] In `src/Shared/ConvertCommandMetadata.cs`:
  - Register the command in `Commands` with source file type, valid extensions, minimum files, and localization label key.
  - **Color Governance Policy**: Verify the command's color strictly derives from its source input file type (`Word`, `Excel`, `PowerPoint`, `Markdown`, `Image`, or `PDF`). Never define ad-hoc target output colors.

---

## 4. UI & Dispatch Adapters (`src/Clickra.CLI/` & `src/Clickra.Fluent/`)

- [ ] **Native Execution Adapter**:
  - `src/Clickra.CLI/Progress/NativeConversionExecutionAdapter.cs`: Ensure the command is routed in `TryRun`.
  - Simple commands automatically fall back to `ConversionUseCases.TryGet` with `RunSimple`.
  - If interactive prompts (password, page ranges) are required, implement a dedicated `Run<Command>` flow.
- [ ] **CLI Dispatch**:
  - `src/Clickra.CLI/Cli/ClickraCli.cs`: Wire into the appropriate `Dispatch<Type>Command` branch (`DispatchPdfCommand`, `DispatchImageCommand`, etc.).
- [ ] **Fluent UI**:
  - `src/Clickra.Fluent/MainPage.xaml`: Add UI button/card with matching `Tag="<command-id>"`.
  - `src/Clickra.Fluent/MainPage.xaml.cs`: Wire button click handler and localization.

---

## 5. Shell Extension & MSIX Packaging Assets

- [ ] **Win11 Shell Extension**:
  - `src/ClickraShell/ComMethods.cs`: Add command string to `SubArgs`, label key to `MenuKeys`, and icon filename to `IconFiles`.
- [ ] **Localization Strings**:
  - `packaging/msix/Strings/<culture>/Resources.resw`: Add menu item translations for all 5 supported languages (`en-us`, `zh-tw`, `zh-cn`, `ja-jp`, `ko-kr`).
- [ ] **Icon Assets**:
  - `packaging/msix/Layout/menu-<command-id>.ico`: Place standard 256x256 multi-tier icon.
  - Run `pwsh scripts/verify_icon_assets.ps1` to ensure all 21+ menu icons are verified.

---

## 6. Architecture Guard Verification

Run the full local test suite:
```powershell
dotnet test tests/Clickra.Core.Tests/Clickra.Core.Tests.csproj
```
Verify all guards pass:
1. `Convert execution: every ConvertCommandMetadata command is covered in NativeConversionExecutionAdapter and ConversionUseCases`
2. `Shell menu: commands reconcile with ConvertCommandRegistry metadata`
3. `Convert registry: every command label key is declared and translated in all 5 languages`
4. `Color governance: every command tag color strictly adheres to input source file policy`
5. `Icon verification`: `pwsh scripts/verify_icon_assets.ps1`

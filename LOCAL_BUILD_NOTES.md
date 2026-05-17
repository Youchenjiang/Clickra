# Local Build Notes

## Windows SDK Tools
This project requires `makepri.exe` and `makeappx.exe`. On this machine, they are located at:
`C:\Windows Kits\10\bin\10.0.26100.0\x64`

The `scripts/build_msix.ps1` script has been updated to automatically detect this path.

## Packaging Requirements
- Version revision number (4th digit) MUST be 0 for Microsoft Store.
- App must handle zero-argument launch without crashing (handled in `Clickra.CLI/Program.cs`).

## v3.0.6 Technical Context (For AI Handoff)
- **Architecture**: The entire project (`ClickraShell` and `Clickra.CLI`) is now **100% Native AOT**. Standard WinForms/WPF cannot be used.
- **UI Rendering**: The Dashboard (`DashboardForm.cs`) uses raw Win32 APIs (`CreateWindowExW`) and GDI+. 
  - *Rule 1*: Always use `W` (Unicode) suffixed APIs and `Marshal.StringToHGlobalUni` to prevent MSIX title bar truncation (the "C" bug).
  - *Rule 2*: Do not attempt Mica/Acrylic rendering. We use a solid `#202020` dark background because GDI+ cannot blend with DWM Mica properly.
- **Office Detection in MSIX**: Standard `Type.GetTypeFromProgID` fails inside the MSIX container. You must use direct registry checks (`HKLM\SOFTWARE\Classes\PowerPoint.Application`).
- **Git Workflow**: Direct commits to `main` are forbidden. Use `feature/...` or `hotfix/...` branches and submit Pull Requests.

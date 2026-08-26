using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Clickra.Launcher;

/// <summary>
/// Clickra 單一 MSIX 啟動器 (Single MSIX UI Launcher)
///
/// 單一 MSIX 內同時包含 Fluent（WinUI 3）與 NativeAOT Win32 兩個介面，
/// 啟動時依本機 runtime 決定要開哪一個：
///   - 有 .NET 8+ Desktop Runtime 與 Windows App Runtime → 啟動 Fluent
///     （framework-dependent WinUI 3；launcher 為 packaged 時 framework 由
///     manifest dependency 提供，unpackaged 時需本機已安裝 runtime）。
///   - 任一條件缺失（例如完全沒有 .NET 的機器）→ 啟動 NativeAOT Win32 儀表板
///     （Clickra.exe，零依賴，不需要任何 runtime）。
///
/// 命令列參數（例如右鍵選單的「指令 + 檔案清單」）原樣轉交給目標介面，
/// 因此 GUI 啟動與右鍵轉檔都會得到與機器配置一致的介面。
/// </summary>
internal static class Program
{
    private const uint LoadLibrarySearchSystem32 = 0x00000800;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetCurrentPackageFullName(ref int len, IntPtr buf);

    private static int Main(string[] args)
    {
        string exeDir = AppContext.BaseDirectory;
        bool canRunFluent = DetectFluentCapability();

        string target = canRunFluent ? "Clickra.Fluent.exe" : "Clickra.exe";

        try
        {
            string targetPath = Path.Combine(exeDir, target);
            if (!File.Exists(targetPath))
                targetPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? exeDir, target);

            // 刻意用 CreateProcess（UseShellExecute=false）而不是 ShellExecute：
            // 由套件的 launcher 直接建立的子程序會繼承套件身分並留在套件的
            // 程序樹中，解除安裝時 Windows 才能自動關閉/終止 UI。
            var psi = new ProcessStartInfo(targetPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory,
            };
            foreach (string arg in args)
                psi.ArgumentList.Add(arg);
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            try
            {
                string logDir = Path.Combine(Path.GetTempPath(), "ClickraLauncher");
                Directory.CreateDirectory(logDir);
                File.WriteAllText(Path.Combine(logDir, "launcher-error.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} Failed to start {target}: {ex}");
            }
            catch { /* Ignore secondary failure when writing crash log to temp dir. */ }
            return 1;
        }

        return 0;
    }

    // ------------------------------------------------------------------
    // Runtime detection
    // ------------------------------------------------------------------

    private static bool DetectFluentCapability()
    {
        bool hasDotNet = Probe(() =>
        {
            Version? ver = ScanInstalledDotNetVersions();
            return ver is not null && ver >= new Version(8, 0);
        });

        bool hasWinAppRuntime = Probe(() =>
        {
            IntPtr h = LoadLibraryExW("Microsoft.WindowsAppRuntime.Bootstrap.dll",
                                      IntPtr.Zero, LoadLibrarySearchSystem32);
            return h != IntPtr.Zero;
        });

        bool isPackaged = Probe(() =>
        {
            int len = 0;
            int hr = GetCurrentPackageFullName(ref len, IntPtr.Zero);
            return hr == 0 || hr == 0x7A;
        });

        return hasDotNet && (hasWinAppRuntime || isPackaged);
    }

    /// <summary>
    /// Scans both registry keys and shared framework directories to find the
    /// highest installed .NET Desktop Runtime version.
    /// </summary>
    private static Version? ScanInstalledDotNetVersions()
    {
        // 1) Registry lookup: official runtime installers write here.
        Version? fromReg = ScanRegistry();
        if (fromReg is not null) return fromReg;

        // 2) Filesystem fallback for SDK / zip installs that skip registry.
        return Probe(() =>
        {
            string sharedFx = "shared/Microsoft.WindowsDesktop.App";
            string[] roots =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "dotnet", sharedFx),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "dotnet", sharedFx),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "dotnet", sharedFx),
            ];

            Version? best = null;
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string child in Directory.GetDirectories(root))
                {
                    if (Version.TryParse(Path.GetFileName(child), out Version? v) &&
                        (best is null || v > best))
                        best = v;
                }
            }
            return best;
        });
    }

    private static Version? ScanRegistry()
    {
        Version? best = null;
        string fxName = "Microsoft.WindowsDesktop.App";

        foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (string arch in new[] { "x64", "arm64", "x86" })
                {
                    best = PickBest(best, TryReadRegValue(hive, view, arch, fxName, "Version"));
                    foreach (string subKeyName in TryGetRegSubKeys(hive, view, arch, fxName))
                    {
                        best = PickBest(best, TryParseVer(subKeyName));
                    }
                }
            }
        }

        return best;
    }

    private static IEnumerable<string> TryGetRegSubKeys(
        RegistryHive hive, RegistryView view, string arch, string fxName)
    {
        string path = $@"SOFTWARE\dotnet\Setup\InstalledVersions\{arch}\sharedfx\{fxName}";
        try
        {
            using var key = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path);
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    private static string? TryReadRegValue(
        RegistryHive hive, RegistryView view, string arch, string fxName, string valueName)
    {
        string path = $@"SOFTWARE\dotnet\Setup\InstalledVersions\{arch}\sharedfx\{fxName}";
        try
        {
            using var key = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path);
            return key?.GetValue(valueName) as string;
        }
        catch { return null; }
    }

    private static Version? TryParseVer(string s) =>
        Version.TryParse(s, out Version? v) ? v : null;

    private static Version? PickBest(Version? current, Version? candidate) =>
        candidate is not null && (current is null || candidate > current) ? candidate : current;

    private static T Probe<T>(Func<T> fn, T fallback = default)
    {
        try { return fn(); }
        catch { return fallback; } // Intentionally swallowed — probe methods are best-effort.
    }
}

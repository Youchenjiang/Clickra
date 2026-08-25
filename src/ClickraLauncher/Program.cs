using System.Diagnostics;
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
    private static readonly Version RequiredDotNetVersion = new(8, 0);

    // LoadLibraryEx with LOAD_LIBRARY_SEARCH_SYSTEM32: 只在 System32 找檔，不載入工作目錄同名檔案。
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);
    private const uint LoadLibrarySearchSystem32 = 0x00000800;

    private static int Main(string[] args)
    {
        // ---- 1. 偵測 ----（registry 讀取 + System32 DLL load + 檔案探測，皆 <2ms）
        string exeDir = AppContext.BaseDirectory;
        Version? dotNetVersion = FindLatestDotNetDesktopRuntime();
        bool hasDotNet = dotNetVersion is not null && dotNetVersion >= RequiredDotNetVersion;

        // 套件內的 Fluent 是 framework-dependent WinUI 3：需要 .NET 8+ 與 Windows App
        // Runtime。launcher 為 packaged（Start menu / 套件 activation）時 framework 由
        // manifest dependency 提供（繼承的 package graph 含該 framework）；unpackaged
        // （右鍵 COM 鏈路 / 直接雙擊）時需本機已安裝 runtime（System32 bootstrap DLL）。
        bool canRunFluent = hasDotNet && (HasWindowsAppRuntime() || IsPackagedProcess());

        // ---- 2. 決定目標 ----（任一條件缺失 → 零依賴 Win32）
        string target = canRunFluent ? "Clickra.Fluent.exe" : "Clickra.exe";

        // ---- 3. 啟動目標並退出 ----（GUI subsystem，無 console 閃現）
        try
        {
            string targetPath = Path.Combine(exeDir, target);
            if (!File.Exists(targetPath))
            {
                // Fallback：與 launcher 同目錄找不到時，試執行檔所在目錄的同層。
                targetPath = Path.Combine(
                    Path.GetDirectoryName(Environment.ProcessPath) ?? exeDir,
                    target);
            }
            // 刻意用 CreateProcess（UseShellExecute=false）而不是 ShellExecute：
            // 由套件（packaged）的 launcher 直接建立的子程序會繼承套件身分並留在套件的
            // 程序樹中，解除安裝時 Windows 才能自動關閉/終止 UI；ShellExecute 會把啟動
            // 交給 shell，產生的子程序成為孤兒，解除安裝會被「應用程式仍在執行」擋住。
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
            // 啟動失敗必須能被診斷，但不能讓 launcher 掛著：寫錯誤檔並回傳非零。
            // （刻意不用 EventLog：NativeAOT 下會引入 System.Diagnostics.EventLog 依賴。）
            try
            {
                string logDir = Path.Combine(Path.GetTempPath(), "ClickraLauncher");
                Directory.CreateDirectory(logDir);
                File.WriteAllText(
                    Path.Combine(logDir, "launcher-error.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} Failed to start {target}: {ex}");
            }
            catch
            {
                // 連寫檔都失敗時只能靜默，仍以非零結束。
            }
            return 1;
        }

        return 0;
    }

    // ------------------------------------------------------------------
    // 偵測
    // ------------------------------------------------------------------

    /// <summary>
    /// 查詢已安裝的 .NET Desktop Runtime 最高版本。
    /// 先讀登錄檔（官方 runtime 安裝程式會寫
    /// HKLM/HKCU\SOFTWARE\dotnet\Setup\InstalledVersions\{arch}\sharedfx\Microsoft.WindowsDesktop.App），
    /// 再以磁碟上的 shared framework 資料夾補強（某些 SDK/zip 安裝方式不會寫登錄檔）。
    /// </summary>
    private static Version? FindLatestDotNetDesktopRuntime()
    {
        Version? best = FindDotNetDesktopFromRegistry();
        if (best is not null) return best;

        // Fallback：直接列舉 shared framework 資料夾（某些 SDK/zip 安裝方式不會寫登錄檔）。
        foreach (string dir in SharedFrameworkRoots())
            best = MaxVersion(best, ScanSharedFrameworkDir(dir));
        return best;
    }

    private static IEnumerable<string> SharedFrameworkRoots()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.WindowsDesktop.App");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "shared", "Microsoft.WindowsDesktop.App");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet", "shared", "Microsoft.WindowsDesktop.App");
    }

    /// <summary>嘗試解析 Version，失敗時回傳 null。</summary>
    private static Version? ParseVersion(string s) =>
        Version.TryParse(s, out Version? v) ? v : null;

    private static Version? ScanSharedFrameworkDir(string dir) => TryGuard<Version?>(() =>
    {
        if (!Directory.Exists(dir)) return null;
        Version? best = null;
        foreach (string sub in Directory.GetDirectories(dir))
            best = MaxVersion(best, ParseVersion(Path.GetFileName(sub)));
        return best;
    });

    private static Version? FindDotNetDesktopFromRegistry()
    {
        Version? best = null;
        string[] arches = { "x64", "arm64", "x86" };
        RegistryView[] views = { RegistryView.Registry64, RegistryView.Registry32 };
        RegistryHive[] hives = { RegistryHive.LocalMachine, RegistryHive.CurrentUser };

        foreach (RegistryHive hive in hives)
            foreach (RegistryView view in views)
                foreach (string arch in arches)
                    best = MaxVersion(best, ReadInstalledVersion(hive, view, arch));
        return best;
    }

    private static Version? ReadInstalledVersion(RegistryHive hive, RegistryView view, string arch) =>
        TryGuard<Version?>(() =>
        {
            string subPath = $@"SOFTWARE\dotnet\Setup\InstalledVersions\{arch}\sharedfx\Microsoft.WindowsDesktop.App";
            using RegistryKey? key = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(subPath);
            if (key is null) return null;
            Version? best = null;
            foreach (string name in key.GetSubKeyNames())
                best = MaxVersion(best, ParseVersion(name));
            if (key.GetValue("Version") is string versionString)
                best = MaxVersion(best, ParseVersion(versionString));
            return best;
        });

    private static Version? MaxVersion(Version? current, Version? candidate)
    {
        return candidate is not null && (current is null || candidate > current) ? candidate : current;
    }

    /// <summary>嘗試執行 probe，回傳結果；失敗時回傳 default。</summary>
    private static T TryGuard<T>(Func<T> probe, T @default = default!)
    {
        try { return probe(); }
        catch { return @default; }
    }

    /// <summary>
    /// Windows App Runtime 的偵測：官方 redistributable 安裝程式會把
    /// Microsoft.WindowsAppRuntime.Bootstrap.dll 放到 System32，
    /// 因此能從 System32 載入該 DLL 就代表 framework 套件已安裝。
    /// </summary>
    private static bool HasWindowsAppRuntime() => TryGuard(() =>
    {
        IntPtr handle = LoadLibraryExW(
            "Microsoft.WindowsAppRuntime.Bootstrap.dll",
            IntPtr.Zero,
            LoadLibrarySearchSystem32);
        return handle != IntPtr.Zero;
    });

    // GetCurrentPackageFullName: kernel32 (AppModel)。回傳碼：
    //   ERROR_SUCCESS (0) / ERROR_INSUFFICIENT_BUFFER (0x7A) → 有 package identity；
    //   ERROR_NO_PACKAGE (0x73C) → unpackaged。
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, IntPtr packageFullName);

    /// <summary>
    /// 目前程序是否為 packaged（有 package identity）。launcher 從 Start menu / 套件
    /// activation 啟動時是 packaged，其 package graph 含 manifest 宣告的 framework
    /// dependency（Store 軌）→ Fluent 繼承後可解析 Windows App Runtime；右鍵 COM 鏈路
    /// （Explorer in-proc 載入 ClickraShell.dll）spawn 出的 launcher 則為 unpackaged，
    /// 需回歸 System32 bootstrap 偵測。
    /// </summary>
    private static bool IsPackagedProcess() => TryGuard(() =>
    {
        int length = 0;
        int hr = GetCurrentPackageFullName(ref length, IntPtr.Zero);
        return hr == 0 || hr == 0x7A;
    });

}

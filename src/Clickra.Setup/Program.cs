using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Clickra.Setup;

/// <summary>
/// Clickra 雙軌安裝程式 (Dual-Track Bootstrapper)
///
/// 決定邏輯：
///   - 本機同時具備 .NET 8+ Desktop Runtime 與 Windows App Runtime 2.x → 安裝 Fluent 軌道
///     （WinUI 3 儀表板，framework-dependent）。
///   - 任一缺失（例如完全沒有 .NET 的乾淨機器）→ 安裝 NativeAOT 軌道
///     （零依賴原生 Win32 儀表板，不需要任何 runtime）。
///
/// 使用方式：
///   ClickraSetup.exe                   自動偵測並安裝最適合的軌道
///   ClickraSetup.exe --check           僅輸出偵測結果（exit code 0=可裝 Fluent, 1=建議 Native）
///   ClickraSetup.exe --fluent          強制 Fluent 軌道
///   ClickraSetup.exe --native          強制 NativeAOT 軌道
///   ClickraSetup.exe --local <msix>    使用本機 MSIX 安裝（跳過下載）
///   ClickraSetup.exe --release-url <base> 自訂下載來源（預設 GitHub latest/download）
///   ClickraSetup.exe --quiet           精簡輸出
/// </summary>
internal static class Program
{
    private const string PackageFamilyName = "g1014308.Clickra";
    private const string FluentAssetName = "Clickra.msix";
    private const string NativeAssetName = "Clickra-Native.msix";
    private const string DefaultReleaseBase =
        "https://github.com/Youchenjiang/Clickra/releases/latest/download/";

    private static readonly Version RequiredDotNetVersion = new(8, 0);

    // LoadLibraryEx with LOAD_LIBRARY_SEARCH_SYSTEM32: 只在 System32 找檔，不載入工作目錄同名檔案。
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);
    private const uint LoadLibrarySearchSystem32 = 0x00000800;

    private static async Task<int> Main(string[] args)
    {
        SetupOptions? options = ParseArguments(args);
        if (options is null)
            return 0;
        if (options.Error is not null)
        {
            await Console.Error.WriteLineAsync(options.Error);
            return 2;
        }

        // ---- 1. 偵測 ----
        Version? dotNetVersion = FindLatestDotNetDesktopRuntime();
        bool hasWinAppRuntime = HasWindowsAppRuntime();
        bool hasDotNet = dotNetVersion is not null && dotNetVersion >= RequiredDotNetVersion;

        if (!options.Quiet)
            PrintDetectionReport(dotNetVersion, hasWinAppRuntime);

        if (options.CheckOnly)
            return hasDotNet && hasWinAppRuntime ? 0 : 1;

        return await InstallSelectedTrackAsync(options, hasDotNet, hasWinAppRuntime);
    }

    private static async Task<int> InstallSelectedTrackAsync(
        SetupOptions options,
        bool hasDotNet,
        bool hasWinAppRuntime)
    {
        // ---- 2. 決定軌道 ----
        bool useFluent = DecideTrack(options.ForceFluent, options.ForceNative, hasDotNet, hasWinAppRuntime);

        if (useFluent && (!hasDotNet || !hasWinAppRuntime))
        {
            await Console.Out.WriteLineAsync("[Clickra Setup] 警告：強制安裝 Fluent，但本機未完整具備所需 runtime，安裝可能失敗。");
        }

        if (!options.Quiet)
        {
            string trackName = useFluent
                ? "Fluent（WinUI 3，需要 .NET 8+ 與 Windows App Runtime 2.x）"
                : "NativeAOT（零依賴原生版，不需要 .NET）";
            Console.WriteLine($"[Clickra Setup] 決定安裝軌道：{trackName}");
        }

        // ---- 3. 取得 MSIX ----
        string msixPath = await AcquireMsixAsync(options.LocalMsix, options.ReleaseBase, useFluent);
        if (msixPath == MissingMsixMarker)
            return 3;

        // ---- 4. 安裝 ----
        int installExit = InstallMsix(msixPath);
        if (installExit != 0)
        {
            await Console.Error.WriteLineAsync(
                "[Clickra Setup] MSIX 安裝失敗。若為 Fluent 軌道，可能是本機缺少 .NET 8 或 Windows App Runtime；" +
                "可改跑 ClickraSetup.exe --native 安裝零依賴版本。");
            return installExit;
        }

        await Console.Out.WriteLineAsync("[Clickra Setup] 安裝完成！您可以從開始功能表或檔案右鍵選單使用 Clickra。");
        return 0;
    }

    private const string MissingMsixMarker = "<missing-msix>";

    /// <summary>Resolves the MSIX to install: the --local file when given (erroring
    /// when it is missing), otherwise the release asset for the chosen track.</summary>
    private static async Task<string> AcquireMsixAsync(string? localMsix, string releaseBase, bool useFluent)
    {
        if (localMsix is not null)
        {
            if (!File.Exists(localMsix))
            {
                await Console.Error.WriteLineAsync($"[Clickra Setup] 找不到本機 MSIX：{localMsix}");
                return MissingMsixMarker;
            }
            return Path.GetFullPath(localMsix);
        }

        string assetName = useFluent ? FluentAssetName : NativeAssetName;
        return await DownloadMsixAsync(releaseBase, assetName);
    }

    private sealed record SetupOptions(bool ForceFluent, bool ForceNative, bool CheckOnly, bool Quiet, string? LocalMsix, string ReleaseBase, string? Error);

    /// <summary>Parses CLI arguments. Returns null when --help was requested, an
    /// options record otherwise (Error is set for invalid combinations).</summary>
    private static SetupOptions? ParseArguments(string[] args)
    {
        bool forceFluent = false;
        bool forceNative = false;
        bool checkOnly = false;
        bool quiet = false;
        string? localMsix = null;
        string releaseBase = DefaultReleaseBase;

        foreach (string raw in args)
        {
            string arg = raw.ToLowerInvariant();
            switch (arg)
            {
                case "--fluent": forceFluent = true; break;
                case "--native": forceNative = true; break;
                case "--check": checkOnly = true; break;
                case "--quiet": quiet = true; break;
                case "--help":
                case "-h":
                    return null;
            }

            if (arg == "--local" || arg == "--release-url")
            {
                int idx = Array.IndexOf(args, raw);
                if (idx + 1 >= args.Length)
                    return new SetupOptions(false, false, false, false, null, DefaultReleaseBase, $"[Clickra Setup] 缺少 {raw} 的參數值。");
                string value = args[idx + 1];
                if (arg == "--local") localMsix = value;
                else releaseBase = value;
            }
        }

        if (forceFluent && forceNative)
            return new SetupOptions(true, true, checkOnly, quiet, localMsix, releaseBase, "[Clickra Setup] --fluent 與 --native 不能同時指定。");

        return new SetupOptions(forceFluent, forceNative, checkOnly, quiet, localMsix, releaseBase, null);
    }

    /// <summary>Decides which track to install: explicit --fluent/--native wins over
    /// the automatic runtime-based selection.</summary>
    private static bool DecideTrack(bool forceFluent, bool forceNative, bool hasDotNet, bool hasWinAppRuntime)
    {
        return forceFluent || (!forceNative && hasDotNet && hasWinAppRuntime);
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

    private static Version? ScanSharedFrameworkDir(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;
            Version? best = null;
            foreach (string sub in Directory.GetDirectories(dir))
                best = MaxVersion(best, Version.TryParse(Path.GetFileName(sub), out Version? v) ? v : null);
            return best;
        }
        catch
        {
            // 忽略無權限的資料夾。
        }
        return null;
    }

    private static Version? FindDotNetDesktopFromRegistry()
    {
        Version? best = null;
        string[] arches = { "x64", "arm64", "x86" };
        RegistryView[] views = { RegistryView.Registry64, RegistryView.Registry32 };
        RegistryHive[] hives = { RegistryHive.LocalMachine, RegistryHive.CurrentUser };

        foreach (RegistryHive hive in hives)
        {
            foreach (RegistryView view in views)
            {
                foreach (string arch in arches)
                    best = MaxVersion(best, ReadInstalledVersion(hive, view, arch));
            }
        }
        return best;
    }

    private static Version? ReadInstalledVersion(RegistryHive hive, RegistryView view, string arch)
    {
        try
        {
            string subPath = $@"SOFTWARE\dotnet\Setup\InstalledVersions\{arch}\sharedfx\Microsoft.WindowsDesktop.App";
            using RegistryKey? key = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(subPath);
            if (key is null) return null;

            Version? best = null;
            // 每個已安裝版本是該鍵下的一個子鍵（鍵名即版本號）。
            foreach (string name in key.GetSubKeyNames())
                best = MaxVersion(best, Version.TryParse(name, out Version? v) ? v : null);
            // 部分安裝會直接寫 "Version" 值。
            if (key.GetValue("Version") is string versionString)
                best = MaxVersion(best, Version.TryParse(versionString, out Version? direct) ? direct : null);
            return best;
        }
        catch
        {
            // 忽略無權限或格式問題的鍵。
        }
        return null;
    }

    private static Version? MaxVersion(Version? current, Version? candidate)
    {
        return candidate is not null && (current is null || candidate > current) ? candidate : current;
    }

    /// <summary>
    /// Windows App Runtime 的偵測：官方 redistributable 安裝程式會把
    /// Microsoft.WindowsAppRuntime.Bootstrap.dll 放到 System32，
    /// 因此能從 System32 載入該 DLL 就代表 framework 套件已安裝。
    /// </summary>
    private static bool HasWindowsAppRuntime()
    {
        try
        {
            IntPtr handle = LoadLibraryExW(
                "Microsoft.WindowsAppRuntime.Bootstrap.dll",
                IntPtr.Zero,
                LoadLibrarySearchSystem32);
            // 故意不 FreeLibrary：安裝程式生命週期極短，避免卸載競態。
            return handle != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private static void PrintDetectionReport(Version? dotNetVersion, bool hasWinAppRuntime)
    {
        bool hasDotNet = dotNetVersion is not null && dotNetVersion >= RequiredDotNetVersion;
        Console.WriteLine("=== Clickra Setup 偵測結果 ===");
        Console.WriteLine($"  .NET Desktop Runtime 8+ : {(hasDotNet ? "有 (" + dotNetVersion + ")" : "無")}");
        Console.WriteLine($"  Windows App Runtime 2.x : {(hasWinAppRuntime ? "有" : "無")}");
        string recommendation = hasDotNet && hasWinAppRuntime
            ? "Fluent（完整 WinUI 3 儀表板）"
            : "NativeAOT（零依賴原生版）";
        Console.WriteLine($"  建議軌道               : {recommendation}");
        Console.WriteLine("==============================");
    }

    // ------------------------------------------------------------------
    // 下載與安裝
    // ------------------------------------------------------------------

    private static async Task<string> DownloadMsixAsync(string releaseBase, string assetName)
    {
        string dir = Path.Combine(Path.GetTempPath(), "ClickraSetup");
        Directory.CreateDirectory(dir);
        string dest = Path.Combine(dir, assetName);
        string url = Path.Combine(releaseBase.TrimEnd('/'), assetName);

        Console.WriteLine($"[Clickra Setup] 正在下載 {url} ...");
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClickraSetup/3.6");

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"下載失敗：HTTP {(int)response.StatusCode} ({url})");
        }

        await using (Stream source = await response.Content.ReadAsStreamAsync())
        await using (Stream target = File.Create(dest))
        {
            await source.CopyToAsync(target);
        }

        double sizeMb = new FileInfo(dest).Length / (1024.0 * 1024.0);
        Console.WriteLine($"[Clickra Setup] 已下載 {sizeMb:F1} MB → {dest}");
        return dest;
    }

    private static int InstallMsix(string msixPath)
    {
        string escapedPath = msixPath.Replace("'", "''");
        string command = $"Add-AppxPackage -Path '{escapedPath}'";

        if (IsPackageInstalled())
        {
            // 同版本切換軌道時強制取代（例如 Native → Fluent 或反向）。
            command += " -ForceUpdateFromAnyVersion -ForceApplicationShutdown";
        }

        Console.WriteLine("[Clickra Setup] 正在安裝 MSIX（視套件大小約需 10~60 秒）...");
        string powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var psi = new ProcessStartInfo(powerShell)
        {
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using Process? process = Process.Start(psi);
        if (process is null)
        {
            Console.Error.WriteLine("[Clickra Setup] 無法啟動 PowerShell。");
            return 3;
        }

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine(output.TrimEnd());
        if (!string.IsNullOrWhiteSpace(error)) Console.Error.WriteLine(error.TrimEnd());
        return process.ExitCode;
    }

    private static bool IsPackageInstalled()
    {
        // 安裝程式以管理員權限執行，因此可以直接讀取 %ProgramFiles%\WindowsApps。
        string windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WindowsApps");
        try
        {
            return Directory.Exists(windowsApps) &&
                   Directory.GetDirectories(windowsApps, PackageFamilyName + "_*").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Clickra Setup — 自動選擇安裝軌道");
        Console.WriteLine();
        Console.WriteLine("軌道決定：本機有 .NET 8+ 與 Windows App Runtime 2.x → 安裝 Fluent（WinUI 3）；");
        Console.WriteLine("          任一缺失 → 安裝 NativeAOT（零依賴原生版）。");
        Console.WriteLine();
        Console.WriteLine("用法：");
        Console.WriteLine("  ClickraSetup.exe                  自動偵測並安裝最適合的軌道");
        Console.WriteLine("  ClickraSetup.exe --check          僅輸出偵測結果（不安裝）");
        Console.WriteLine("  ClickraSetup.exe --fluent         強制安裝 Fluent 軌道");
        Console.WriteLine("  ClickraSetup.exe --native         強制安裝 NativeAOT 軌道");
        Console.WriteLine("  ClickraSetup.exe --local <msix>   使用本機 MSIX 安裝（跳過下載）");
        Console.WriteLine("  ClickraSetup.exe --release-url <base>  自訂下載來源");
        Console.WriteLine("  ClickraSetup.exe --quiet          精簡輸出");
    }
}

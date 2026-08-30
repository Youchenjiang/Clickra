using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Clickra.Launcher;

/// <summary>
/// Clickra Launcher ??NativeAOT entry point, zero dependency.
/// COM activation via IApplicationActivationManager ??Fluent optional.
/// Fallback ??AOT Dashboard.
/// </summary>
internal static class Program
{
    private const string AotExe = "Clickra.exe";
    private const int FluentTimeoutMs = 5000;

    private static int Main(string[] args)
    {
        string? exeDir = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(exeDir))
            exeDir = Path.GetDirectoryName(Environment.ProcessPath);

        // Try COM activation for Fluent optional package
        if (TryActivateFluent(out uint pid))
        {
            Thread.Sleep(FluentTimeoutMs);
            try
            {
                var proc = Process.GetProcessById((int)pid);
                if (!proc.HasExited)
                    return 0;
            }
            catch { }
        }

        // Fallback: NativeAOT Dashboard
        if (exeDir == null) return 1;
        string? aotPath = FindExe(exeDir, AotExe);
        if (aotPath == null) return 1;

        var psi = new ProcessStartInfo(aotPath) { UseShellExecute = false };
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);
        Process.Start(psi)?.Dispose();
        return 0;
    }

    private static bool TryActivateFluent(out uint processId)
    {
        processId = 0;
        int hr = CoInitializeEx(IntPtr.Zero, 0x2);
        if (hr != 0 && hr != 1 && hr != 0x80010106) return false;

        try
        {
            Guid clsid = new(0x45ba127d, 0x10a8, 0x46ea, 0x8a, 0xb7, 0x56, 0xea, 0x90, 0x78, 0x94, 0x3c);
            Guid iid = new(0x2e941141, 0x7f97, 0x4756, 0xba, 0x1d, 0x9d, 0xec, 0xde, 0x89, 0x4a, 0x3d);

            hr = CoCreateInstance(ref clsid, IntPtr.Zero, 0x1, ref iid, out IntPtr pMgr);
            if (hr < 0 || pMgr == IntPtr.Zero) return false;

            try
            {
                string? familyName = GetPackageFamilyName();
                if (string.IsNullOrEmpty(familyName)) return false;

                string aumid = familyName + "!FluentApp";
                hr = ActivateApplication(pMgr, aumid, string.Empty, 0, out processId);
                return hr == 0 && processId != 0;
            }
            finally { Marshal.Release(pMgr); }
        }
        finally { CoUninitialize(); }
    }

    private static string? GetPackageFamilyName()
    {
        try
        {
            uint len = 0;
            int r = GetCurrentPackageFamilyName(ref len, null);
            if (r != 120 || len == 0) return null;
            Span<char> name = stackalloc char[(int)len];
            r = GetCurrentPackageFamilyName(ref len, name);
            return r == 0 ? name.ToString() : null;
        }
        catch { return null; }
    }

    [SuppressMessage("SonarQube", "S6640", Justification = "NativeAOT COM vtable interop requires unsafe code")]
    private static unsafe int ActivateApplication(IntPtr p, string a, string? b, uint c, out uint d)
    {
        d = 0;
        IntPtr vt = Marshal.ReadIntPtr(p);
        IntPtr fn = Marshal.ReadIntPtr(vt, 3 * IntPtr.Size);
        delegate* unmanaged[Stdcall]<IntPtr, char*, char*, uint, out uint, int> act =
            (delegate* unmanaged[Stdcall]<IntPtr, char*, char*, uint, out uint, int>)fn;
        fixed (char* pa = a) fixed (char* pb = b ?? "")
            return act(p, pa, pb, c, out d);
    }

    private static string? FindExe(string dir, string name)
    {
        string p = Path.Combine(dir, name);
        if (File.Exists(p)) return p;
        string? pd = Path.GetDirectoryName(Environment.ProcessPath);
        if (pd != null) { p = Path.Combine(pd, name); if (File.Exists(p)) return p; }
        return null;
    }

    // skipcq: CS-R1138
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoInitializeEx(IntPtr r, uint m);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void CoUninitialize();
    // skipcq: CS-R1138
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);
    // skipcq: CS-R1138
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFamilyName(ref uint l, Span<char> n);
}

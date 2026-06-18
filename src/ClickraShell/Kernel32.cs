using System.Runtime.InteropServices;

internal static partial class Kernel32
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetModuleHandleExW(uint dwFlags, IntPtr lpModuleName, out IntPtr phModule);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static unsafe extern uint GetModuleFileNameW(IntPtr hModule, char* lpFilename, uint nSize);
}

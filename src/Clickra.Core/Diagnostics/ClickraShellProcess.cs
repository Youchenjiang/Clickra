using System;
using System.Runtime.InteropServices;

namespace Clickra.Core
{
    /// <summary>
    /// 清除殘留的 ClickraShell COM surrogate（dllhost.exe）。
    ///
    /// 右鍵選單的 ClickraShell.dll 是以 com:SurrogateServer 註冊，由 Windows 載入到
    /// dllhost.exe 中執行。dllhost 會帶著套件身分存活數分鐘（COM 閒置逾時）才結束，
    /// 因此即使使用者關閉了 Clickra 視窗，Windows 設定仍會把 Clickra 視為「正在執行」，
    /// 解除安裝會被擋住，必須先「強制停止」才能移除。
    ///
    /// 此方法列舉所有載入 ClickraShell.dll 的 dllhost.exe 並將其結束，讓套件內不殘留
    /// 任何程序。只會影響我們自己的 surrogate，不會動到其他程式的 dllhost。
    /// </summary>
    public static class ClickraShellProcess
    {
        private const uint Th32csSnapProcess = 0x00000002;
        private const uint Th32csSnapModule = 0x00000008;
        private const uint ProcessTerminate = 0x0001;
        private const string DllHostExe = "dllhost.exe";
        private const string ShellDllName = "ClickraShell.dll";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ModuleEntry32W
        {
            public uint dwSize;
            public uint th32ModuleID;
            public uint th32ProcessID;
            public uint GlblcntUsage;
            public uint ProccntUsage;
            public IntPtr modBaseAddr;
            public uint modBaseSize;
            public IntPtr hModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExePath;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32FirstW(IntPtr hSnapshot, ref ProcessEntry32W lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32NextW(IntPtr hSnapshot, ref ProcessEntry32W lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Module32FirstW(IntPtr hSnapshot, ref ModuleEntry32W lpme);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Module32NextW(IntPtr hSnapshot, ref ModuleEntry32W lpme);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        /// <summary>終止所有載入 ClickraShell.dll 的 dllhost.exe surrogate host。</summary>
        public static void KillSurrogateHosts()
        {
            uint currentPid = (uint)Environment.ProcessId;
            IntPtr snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
            if (snapshot == new IntPtr(-1)) return;
            try
            {
                var entry = new ProcessEntry32W { dwSize = (uint)Marshal.SizeOf<ProcessEntry32W>() };
                if (!Process32FirstW(snapshot, ref entry)) return;
                do
                {
                    if (entry.th32ProcessID == currentPid) continue;
                    if (!entry.szExeFile.Equals(DllHostExe, StringComparison.OrdinalIgnoreCase)) continue;
                    if (HasModuleLoaded(entry.th32ProcessID, ShellDllName))
                    {
                        Terminate(entry.th32ProcessID);
                    }
                } while (Process32NextW(snapshot, ref entry));
            }
            catch
            {
                // 清理失敗絕不能影響主流程。
            }
            finally
            {
                CloseHandle(snapshot);
            }
        }

        /// <summary>該程序是否已載入指定模組（Toolhelp 模組快照）。</summary>
        private static bool HasModuleLoaded(uint processId, string moduleName)
        {
            IntPtr snapshot = CreateToolhelp32Snapshot(Th32csSnapModule, processId);
            if (snapshot == new IntPtr(-1)) return false;
            try
            {
                var entry = new ModuleEntry32W { dwSize = (uint)Marshal.SizeOf<ModuleEntry32W>() };
                if (!Module32FirstW(snapshot, ref entry)) return false;
                do
                {
                    if (entry.szModule.Equals(moduleName, StringComparison.OrdinalIgnoreCase)) return true;
                } while (Module32NextW(snapshot, ref entry));
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                CloseHandle(snapshot);
            }
        }

        private static void Terminate(uint processId)
        {
            try
            {
                IntPtr handle = OpenProcess(ProcessTerminate, false, processId);
                if (handle == IntPtr.Zero) return;
                try
                {
                    TerminateProcess(handle, 0);
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
            catch
            {
                // 個別失敗就略過，繼續處理其餘 surrogate。
            }
        }
    }
}

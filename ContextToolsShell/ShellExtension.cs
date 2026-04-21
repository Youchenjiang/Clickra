using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;

namespace ContextToolsShell
{
    // --- COM COM Interfaces ---

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    public interface IShellItem
    {
        void BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
    public interface IShellItemArray
    {
        void BindToHandler(IntPtr pbc, [In] ref Guid rbhid, [In] ref Guid riid, out IntPtr ppvOut);
        void GetPropertyStore(uint flags, [In] ref Guid riid, out IntPtr ppvOut);
        void GetPropertyDescriptionList([In] ref Guid keyType, [In] ref Guid riid, out IntPtr ppvOut);
        void GetAttributes(uint AttribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        void GetCount(out uint pdwNumItems);
        void GetItemAt(uint dwIndex, out IShellItem ppsi);
        void EnumItems(out IntPtr ppenumShellItems);
    }

    public enum EXPCMDFLAGS : uint
    {
        ECF_DEFAULT = 0x000,
        ECF_HASSUBCOMMANDS = 0x001,
        ECF_HASSPLITBUTTON = 0x002,
        ECF_HIDELABEL = 0x004,
        ECF_ISSEPARATOR = 0x008,
        ECF_HASLUXURYUI = 0x010,
        ECF_SEPARATORBEFORE = 0x020,
        ECF_SEPARATORAFTER = 0x040,
        ECF_ISNONUSERCOMMAND = 0x080,
        ECF_TOGGLEABLE = 0x100,
        ECF_AUTOMENUICONS = 0x200,
    }

    public enum EXPCMDSTATE : uint
    {
        ECS_ENABLED = 0,
        ECS_DISABLED = 1,
        ECS_HIDDEN = 2,
        ECS_CHECKBOX = 4,
        ECS_CHECKED = 8,
        ECS_RADIOCHECK = 16,
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bc141877-0130-4ad3-9111-92a2a0de599c")]
    public interface IEnumExplorerCommand
    {
        [PreserveSig] int Next(uint celt, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Interface, SizeParamIndex = 0)] IExplorerCommand[] rgelt, out uint pceltFetched);
        void Skip(uint celt);
        void Reset();
        void Clone(out IEnumExplorerCommand ppenum);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
    public interface IExplorerCommand
    {
        void GetTitle(IntPtr psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetIcon(IntPtr psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszIcon);
        void GetToolTip(IntPtr psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszToolTip);
        void GetCanonicalName(out Guid pguidCommandName);
        void GetState(IntPtr psiItemArray, bool fOkToBeSlow, out EXPCMDSTATE pViewState);
        void Invoke(IntPtr psiItemArray, IntPtr pbc);
        void GetFlags(out EXPCMDFLAGS pFlags);
        void EnumSubCommands(out IEnumExplorerCommand ppEnum);
    }

    // --- NativeAOT COM Export Logic ---

    public class Exporter
    {
        [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject")]
        public static int DllGetClassObject(ref Guid rclsid, ref Guid riid, out IntPtr ppv)
        {
            ppv = IntPtr.Zero;
            if (rclsid == new Guid("FA2159B5-1234-4567-89AB-CDEF12345678"))
            {
                var factory = new ContextToolsClassFactory();
                return Marshal.QueryInterface(Marshal.GetIUnknownForObject(factory), ref riid, out ppv);
            }
            return -2147221231; // CLASS_E_CLASSNOTAVAILABLE
        }

        [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow")]
        public static int DllCanUnloadNow() => 0; // S_OK
    }

    [ComVisible(true)]
    public class ContextToolsClassFactory : IClassFactory
    {
        public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppv)
        {
            ppv = IntPtr.Zero;
            if (pUnkOuter != IntPtr.Zero) return -2147221232; // CLASS_E_NOAGGREGATION
            
            var command = new ContextToolsCommand();
            return Marshal.QueryInterface(Marshal.GetIUnknownForObject(command), ref riid, out ppv);
        }

        public int LockServer(bool fLock) => 0;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("00000001-0000-0000-C000-000000000046")]
    public interface IClassFactory
    {
        [PreserveSig] int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int LockServer(bool fLock);
    }

    // --- Main Shell Extension Implementation ---

    [Guid("FA2159B5-1234-4567-89AB-CDEF12345678")]
    public class ContextToolsCommand : IExplorerCommand
    {
        public void GetTitle(IntPtr psiItemArray, out string ppszName) => ppszName = "ContextTools (⚡)";
        public void GetIcon(IntPtr psiItemArray, out string ppszIcon) 
        {
             string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
             ppszIcon = Path.Combine(appData, "ContextTools", "app.ico");
        }
        public void GetToolTip(IntPtr psiItemArray, out string ppszToolTip) => ppszToolTip = "高效能快速轉換工具";
        public void GetCanonicalName(out Guid pguidCommandName) => pguidCommandName = Guid.Empty;
        public void GetState(IntPtr psiItemArray, bool fOkToBeSlow, out EXPCMDSTATE pViewState) => pViewState = EXPCMDSTATE.ECS_ENABLED;
        
        public void Invoke(IntPtr psiItemArray, IntPtr pbc) { }
        
        public void GetFlags(out EXPCMDFLAGS pFlags) => pFlags = EXPCMDFLAGS.ECF_HASSUBCOMMANDS;
        
        public void EnumSubCommands(out IEnumExplorerCommand ppEnum)
        {
            ppEnum = new CommandEnumerator(new IExplorerCommand[]
            {
                new SubCommand("簡報轉 PDF", "ppt2pdf"),
                new SubCommand("PDF 合併", "merge-pdf"),
                new SubCommand("圖片合併成 PDF", "img2pdf"),
                new SubCommand("圖片垂直拼接", "img-stitch")
            });
        }
    }

    public class SubCommand : IExplorerCommand
    {
        private readonly string _title;
        private readonly string _cmd;

        public SubCommand(string title, string cmd)
        {
            _title = title;
            _cmd = cmd;
        }

        public void GetTitle(IntPtr psiItemArray, out string ppszName) => ppszName = _title;
        public void GetIcon(IntPtr psiItemArray, out string ppszIcon) => ppszIcon = "";
        public void GetToolTip(IntPtr psiItemArray, out string ppszToolTip) => ppszToolTip = "";
        public void GetCanonicalName(out Guid pguidCommandName) => pguidCommandName = Guid.Empty;
        public void GetState(IntPtr psiItemArray, bool fOkToBeSlow, out EXPCMDSTATE pViewState) => pViewState = EXPCMDSTATE.ECS_ENABLED;

        public void Invoke(IntPtr psiItemArray, IntPtr pbc)
        {
            if (psiItemArray == IntPtr.Zero) return;

            var array = (IShellItemArray)Marshal.GetTypedObjectForIUnknown(psiItemArray, typeof(IShellItemArray));
            array.GetCount(out uint count);
            
            var paths = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                array.GetItemAt(i, out var item);
                item.GetDisplayName(0x80058000, out var path); // SIGDN_FILESYSPATH
                paths.Add($"\"{path}\"");
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appPath = Path.Combine(appData, "ContextTools", "ContextTools.exe");
            
            string args = $"{_cmd} {string.Join(" ", paths)}";
            
            Process.Start(new ProcessStartInfo(appPath, args) { UseShellExecute = true });
        }

        public void GetFlags(out EXPCMDFLAGS pFlags) => pFlags = EXPCMDFLAGS.ECF_DEFAULT;
        public void EnumSubCommands(out IEnumExplorerCommand ppEnum) => ppEnum = null!;
    }

    public class CommandEnumerator : IEnumExplorerCommand
    {
        private readonly IExplorerCommand[] _commands;
        private uint _index = 0;

        public CommandEnumerator(IExplorerCommand[] commands) => _commands = commands;

        public int Next(uint celt, IExplorerCommand[] rgelt, out uint pceltFetched)
        {
            uint i = 0;
            while (i < celt && _index < _commands.Length)
            {
                rgelt[i++] = _commands[_index++];
            }
            pceltFetched = i;
            return i == celt ? 0 : 1;
        }

        public void Skip(uint celt) => _index += celt;
        public void Reset() => _index = 0;
        public void Clone(out IEnumExplorerCommand ppenum) => ppenum = new CommandEnumerator(_commands);
    }
}

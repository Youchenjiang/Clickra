using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Xml.Linq;

// Win32 API for dynamic DLL path resolution
internal static partial class Kernel32
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetModuleHandleExW(uint dwFlags, IntPtr lpModuleName, out IntPtr phModule);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static unsafe extern uint GetModuleFileNameW(IntPtr hModule, char* lpFilename, uint nSize);
}

namespace ClickraShell
{
    internal enum ComObjectType { Factory, Command, Enum }

    [StructLayout(LayoutKind.Sequential)]
    internal struct UniversalObject
    {
        public IntPtr PrimaryVTable;
        public IntPtr SelectionVTable;
        public int RefCount;
        public ComObjectType Type;
        public int Data;
        public IntPtr ShellItems;
    }

    internal static class Guids
    {
        public static readonly Guid Clsid = new("B17A34D2-55E0-4D6F-8D1F-7A6E9C2B30A1");
        public static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
        public static readonly Guid IID_IClassFactory = new("00000001-0000-0000-C000-000000000046");
        
        // IExplorerCommand IIDs (including compatibility variations found in logs)
        public static readonly Guid IID_IExplorerCommand = new("a08ce4d0-fa25-44ab-b57c-c7b3c3ef1cf0");
        public static readonly Guid IID_IExplorerCommand_Compat = new("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9");
        
        // IEnumExplorerCommand IIDs (including compatibility variations found in logs)
        public static readonly Guid IID_IEnumExplorerCommand = new("c5740441-fa60-492d-944c-354313f8c7b6");
        public static readonly Guid IID_IEnumExplorerCommand_Compat = new("a88826f8-186f-4987-aade-ea0cef8fbfe8");
        
        public static readonly Guid IID_IObjectWithSelection = new("b196b287-bab4-101a-b69c-00aa00341d07");
    }

    public class Exporter
    {
        private static IntPtr _factoryVt = IntPtr.Zero;
        private static IntPtr _commandVt = IntPtr.Zero;
        private static IntPtr _selectionVt = IntPtr.Zero;
        private static IntPtr _enumVt = IntPtr.Zero;

        [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject", CallConvs = new[] { typeof(CallConvStdcall) })]
        public static unsafe int DllGetClassObject(Guid* rclsid, Guid* riid, IntPtr* ppv)
        {
            *ppv = IntPtr.Zero;
            if (*rclsid != Guids.Clsid) return -2147221231; // CLASS_E_CLASSNOTAVAILABLE
            
            if (_factoryVt == IntPtr.Zero) _factoryVt = CreateVTable(5, new IntPtr[] {
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)&ComMethods.PrimaryQI,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&ComMethods.PrimaryAddRef,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&ComMethods.PrimaryRelease,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)&ComMethods.CreateInstance,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, int, int>)&ComMethods.LockServer
            });
            return ComMethods.CreateObject(_factoryVt, riid, ppv, ComObjectType.Factory);
        }

        [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow", CallConvs = new[] { typeof(CallConvStdcall) })]
        public static int DllCanUnloadNow() => 0;

        public static unsafe IntPtr GetCommandVt()
        {
            if (_commandVt == IntPtr.Zero) _commandVt = CreateVTable(11, new IntPtr[] {
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)&ComMethods.PrimaryQI,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&ComMethods.PrimaryAddRef,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&ComMethods.PrimaryRelease,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)&ComMethods.GetTitle,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)&ComMethods.GetIcon,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)&ComMethods.GetToolTip,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, Guid*, int>)&ComMethods.GetCanonicalName,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, uint*, int>)&ComMethods.GetState,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int>)&ComMethods.Invoke,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)&ComMethods.GetFlags,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&ComMethods.EnumSubCommands
            });
            return _commandVt;
        }

        public static unsafe IntPtr GetSelectionVt()
        {
            if (_selectionVt == IntPtr.Zero) _selectionVt = CreateVTable(5, new IntPtr[] {
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)&ComMethods.SelectionQI,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&ComMethods.SelectionAddRef,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&ComMethods.SelectionRelease,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)&ComMethods.SetSelection,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)&ComMethods.GetSelection
            });
            return _selectionVt;
        }

        public static unsafe IntPtr GetEnumVt()
        {
            if (_enumVt == IntPtr.Zero) _enumVt = CreateVTable(7, new IntPtr[] {
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)&ComMethods.PrimaryQI,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&ComMethods.PrimaryAddRef,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&ComMethods.PrimaryRelease,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, uint*, int>)&ComMethods.EnumNext,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, int>)&ComMethods.EnumSkip,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, int>)&ComMethods.EnumReset,
                (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&ComMethods.EnumClone
            });
            return _enumVt;
        }

        private static unsafe IntPtr CreateVTable(int size, IntPtr[] methods)
        {
            IntPtr vtable = Marshal.AllocCoTaskMem(IntPtr.Size * size);
            var vt = (IntPtr*)vtable;
            for (int i = 0; i < size; i++) vt[i] = methods[i];
            return vtable;
        }
    }

    internal static class ShellUtils
    {
        public static unsafe string GetModuleDir()
        {
            IntPtr fnPtr = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&ComMethods.PrimaryAddRef;
            if (Kernel32.GetModuleHandleExW(6, fnPtr, out IntPtr hModule))
            {
                unsafe
                {
                    char* buf = stackalloc char[260];
                    uint len = Kernel32.GetModuleFileNameW(hModule, buf, 260);
                    if (len > 0) return Path.GetDirectoryName(new string(buf, 0, (int)len)) ?? string.Empty;
                }
            }
            return string.Empty;
        }

        public static string GetString(string key)
        {
            try
            {
                string dir = GetModuleDir();
                bool isEn = CultureInfo.CurrentUICulture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase);
                string resPath = Path.Combine(dir, "Strings", isEn ? "en-us" : "zh-tw", "Resources.resw");
                
                if (!File.Exists(resPath)) return key;

                var doc = XDocument.Load(resPath);
                var data = doc.Root?.Elements("data").FirstOrDefault(e => e.Attribute("name")?.Value == key);
                return data?.Element("value")?.Value ?? key;
            }
            catch { return key; }
        }
    }

    internal static class ComMethods
    {
        private static readonly string[] MenuKeys = { "Menu_Ppt2Pdf", "Menu_MergePdf", "Menu_Img2Pdf", "Menu_ImgMerge", "Menu_ImgStitch" };
        private static readonly string[] SubArgs = { "ppt2pdf", "merge-pdf", "img2pdf", "img-merge", "img-stitch" };

        internal static unsafe int CreateObject(IntPtr vt, Guid* riid, IntPtr* ppv, ComObjectType type, int data = -1)
        {
            IntPtr instance = Marshal.AllocCoTaskMem(Marshal.SizeOf<UniversalObject>());
            var obj = new UniversalObject { PrimaryVTable = vt, SelectionVTable = Exporter.GetSelectionVt(), RefCount = 1, Type = type, Data = data, ShellItems = IntPtr.Zero };
            Marshal.StructureToPtr(obj, instance, false);
            int hr = QIInternal(instance, riid, ppv);
            ReleaseInternal(instance);
            return hr;
        }

        internal static unsafe int QIInternal(IntPtr basePtr, Guid* riid, IntPtr* ppv)
        {
            var p = (UniversalObject*)basePtr;
            Guid req = *riid;
            *ppv = IntPtr.Zero;
            bool primary = false;
            if (req == Guids.IID_IUnknown) primary = true;
            else if (p->Type == ComObjectType.Factory && req == Guids.IID_IClassFactory) primary = true;
            else if (p->Type == ComObjectType.Command && (req == Guids.IID_IExplorerCommand || req == Guids.IID_IExplorerCommand_Compat)) primary = true;
            else if (p->Type == ComObjectType.Enum && (req == Guids.IID_IEnumExplorerCommand || req == Guids.IID_IEnumExplorerCommand_Compat)) primary = true;

            if (primary) { *ppv = basePtr; AddRefInternal(basePtr); return 0; }
            if (req == Guids.IID_IObjectWithSelection && (p->Type == ComObjectType.Command || p->Type == ComObjectType.Enum)) {
                *ppv = basePtr + IntPtr.Size; AddRefInternal(basePtr); return 0;
            }
            return -2147467262; // E_NOINTERFACE
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int PrimaryQI(IntPtr _this, Guid* riid, IntPtr* ppv) => QIInternal(_this, riid, ppv);
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int SelectionQI(IntPtr _this, Guid* riid, IntPtr* ppv) => QIInternal(_this - IntPtr.Size, riid, ppv);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe uint PrimaryAddRef(IntPtr _this) => AddRefInternal(_this);
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe uint SelectionAddRef(IntPtr _this) => AddRefInternal(_this - IntPtr.Size);
        internal static unsafe uint AddRefInternal(IntPtr basePtr) => (uint)Interlocked.Increment(ref ((UniversalObject*)basePtr)->RefCount);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe uint PrimaryRelease(IntPtr _this) => ReleaseInternal(_this);
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe uint SelectionRelease(IntPtr _this) => ReleaseInternal(_this - IntPtr.Size);
        internal static unsafe uint ReleaseInternal(IntPtr basePtr) { uint c = (uint)Interlocked.Decrement(ref ((UniversalObject*)basePtr)->RefCount); if (c == 0) Marshal.FreeCoTaskMem(basePtr); return c; }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int CreateInstance(IntPtr _this, IntPtr outer, Guid* riid, IntPtr* ppv) => CreateObject(Exporter.GetCommandVt(), riid, ppv, ComObjectType.Command);
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static int LockServer(IntPtr _this, int fLock) => 0;

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int SetSelection(IntPtr _this, IntPtr psi) { ((UniversalObject*)(_this - IntPtr.Size))->ShellItems = psi; return 0; }
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int GetSelection(IntPtr _this, Guid* riid, IntPtr* ppv) { var items = ((UniversalObject*)(_this - IntPtr.Size))->ShellItems; if (items == IntPtr.Zero) return -2147467259; *ppv = items; return 0; }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static unsafe int GetTitle(IntPtr _this, IntPtr psi, IntPtr* ppsz)
        {
            int idx = ((UniversalObject*)_this)->Data;
            string t = (idx == -1) ? "Clickra" : ShellUtils.GetString(MenuKeys[idx]);
            *ppsz = Marshal.StringToCoTaskMemUni(t); return 0;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static unsafe int GetIcon(IntPtr _this, IntPtr psi, IntPtr* ppsz)
        {
            *ppsz = IntPtr.Zero;
            if (((UniversalObject*)_this)->Data == -1)
            {
                string iconPath = Path.Combine(ShellUtils.GetModuleDir(), "app.png");
                if (File.Exists(iconPath)) { *ppsz = Marshal.StringToCoTaskMemUni(iconPath); return 0; }
            }
            return -2147467263; // E_NOTIMPL
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int GetToolTip(IntPtr _this, IntPtr psi, IntPtr* p) { *p = IntPtr.Zero; return 0; }
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int GetCanonicalName(IntPtr _this, Guid* p) { *p = Guid.Empty; return 0; }
        
        private static bool IsSupported(string path, int idx)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) return false;

            return idx switch
            {
                -1 => new[] { ".ppt", ".pptx", ".pdf", ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" }.Contains(ext),
                0 => ext == ".ppt" || ext == ".pptx",
                1 => ext == ".pdf",
                2 or 3 or 4 => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" }.Contains(ext),
                _ => false
            };
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static unsafe int GetState(IntPtr _this, IntPtr psi, int slow, uint* p)
        {
            var obj = (UniversalObject*)_this;
            int idx = obj->Data;
            *p = 2; // Default: Hidden (ECS_HIDDEN)

            var files = GetFiles(psi);
            if (files.Count == 0) return 0;

            // Specific logic for multi-file commands
            bool countOk = idx switch {
                1 or 4 => files.Count > 1, // Merge PDF and Image Stitch require at least 2 files
                _ => true
            };

            if (countOk && files.Any(f => IsSupported(f, idx)))
            {
                *p = 0; // ECS_ENABLED
            }

            return 0;
        }

        private static unsafe List<string> GetFiles(IntPtr psi)
        {
            var files = new List<string>();
            if (psi != IntPtr.Zero)
            {
                IntPtr vt = *(IntPtr*)psi;
                delegate* unmanaged[Stdcall]<IntPtr, uint*, int> getCount = (delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)(*(IntPtr*)(vt + 7 * IntPtr.Size));
                delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int> getItemAt = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)(*(IntPtr*)(vt + 8 * IntPtr.Size));
                uint count = 0;
                if (getCount(psi, &count) == 0) {
                    for (uint i = 0; i < count; i++) {
                        IntPtr item = IntPtr.Zero;
                        if (getItemAt(psi, i, &item) == 0) {
                            IntPtr ivt = *(IntPtr*)item;
                            delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int> getName = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)(*(IntPtr*)(ivt + 5 * IntPtr.Size));
                            IntPtr namePtr = IntPtr.Zero;
                            if (getName(item, 0x80058000, &namePtr) == 0) {
                                string? path = Marshal.PtrToStringUni(namePtr);
                                if (!string.IsNullOrEmpty(path)) files.Add(path);
                                Marshal.FreeCoTaskMem(namePtr);
                            }
                            delegate* unmanaged[Stdcall]<IntPtr, uint> releaseChild = (delegate* unmanaged[Stdcall]<IntPtr, uint>)(*(IntPtr*)(ivt + 2 * IntPtr.Size));
                            releaseChild(item);
                        }
                    }
                }
            }
            return files;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int GetFlags(IntPtr _this, uint* p) { *p = (uint)(((UniversalObject*)_this)->Data == -1 ? 1 : 0); return 0; }
        
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static unsafe int EnumSubCommands(IntPtr _this, IntPtr* ppEnum)
        {
            var p = (UniversalObject*)_this;
            if (p->Data != -1) { *ppEnum = IntPtr.Zero; return 1; }
            Guid iid = Guids.IID_IEnumExplorerCommand;
            return CreateObject(Exporter.GetEnumVt(), &iid, ppEnum, ComObjectType.Enum, 0);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static unsafe int Invoke(IntPtr _this, IntPtr psi, IntPtr pbc)
        {
            int idx = ((UniversalObject*)_this)->Data;
            if (idx == -1) return 0;
            
            StringBuilder sb = new StringBuilder();
            sb.Append(SubArgs[idx]);
            var files = GetFiles(psi);
            foreach (var f in files) sb.Append(" \"").Append(f).Append("\"");

            string app = Path.Combine(ShellUtils.GetModuleDir(), "Clickra.exe");
            if (File.Exists(app)) Process.Start(new ProcessStartInfo(app, sb.ToString()) { UseShellExecute = true });
            return 0;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static unsafe int EnumNext(IntPtr _this, uint celt, IntPtr* rgelt, uint* pcelt)
        {
            var p = (UniversalObject*)_this;
            uint f = 0; Guid iid = Guids.IID_IExplorerCommand;
            while (f < celt && p->Data < SubArgs.Length) {
                CreateObject(Exporter.GetCommandVt(), &iid, &rgelt[f], ComObjectType.Command, p->Data);
                p->Data++; f++;
            }
            if (pcelt != null) *pcelt = f;
            return f == celt ? 0 : 1;
        }
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int EnumSkip(IntPtr _this, uint c) { ((UniversalObject*)_this)->Data += (int)c; return 0; }
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int EnumReset(IntPtr _this) { ((UniversalObject*)_this)->Data = 0; return 0; }
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int EnumClone(IntPtr _this, IntPtr* ppv) { *ppv = IntPtr.Zero; return -2147467263; }
    }
}

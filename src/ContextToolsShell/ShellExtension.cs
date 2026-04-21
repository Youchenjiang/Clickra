using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.CompilerServices;

namespace ContextToolsShell
{
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "ContextTools.log");
        public static void Log(string message)
        {
            try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n"); } catch { }
        }
    }

    internal static class Guids
    {
        public static readonly Guid Clsid = new Guid("FA2159B5-1234-4567-89AB-CDEF12345678");
        public static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
        public static readonly Guid IID_IClassFactory = new Guid("00000001-0000-0000-C000-000000000046");
        public static readonly Guid IID_IExplorerCommand = new Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9");
        public static readonly Guid IID_IExplorerCommand_Alt = new Guid("ea5d0de4-770d-4da0-a9f8-d7f9a140ff79");
        public static readonly Guid IID_IEnumExplorerCommand = new Guid("bc141877-0130-4ad3-9111-92a2a0de599c");
        public static readonly Guid IID_IEnumExplorerCommand_Alt = new Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8");
        public static readonly Guid IID_IObjectWithSelection = new Guid("1ac7516e-e6bb-4a69-b63f-e841904dc5a6");
    }

    internal enum ComObjectType { Factory = 0, Command = 1, Enum = 2 }

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
            if (*rclsid != Guids.Clsid) return -2147221231;
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

    internal static class ComMethods
    {
        private static readonly string[] SubTitles = { "簡報轉 PDF", "PDF 合併", "圖片合併成 PDF", "圖片垂直拼接" };
        private static readonly string[] SubArgs = { "ppt2pdf", "merge-pdf", "img2pdf", "img-stitch" };

        internal static unsafe int CreateObject(IntPtr vt, Guid* riid, IntPtr* ppv, ComObjectType type, int data = -1)
        {
            IntPtr instance = Marshal.AllocCoTaskMem(Marshal.SizeOf<UniversalObject>());
            var obj = new UniversalObject { PrimaryVTable = vt, SelectionVTable = Exporter.GetSelectionVt(), RefCount = 1, Type = type, Data = data, ShellItems = IntPtr.Zero };
            Marshal.StructureToPtr(obj, instance, false);
            int hr = QIInternal(instance, riid, ppv);
            ReleaseInternal(instance);
            return hr;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int PrimaryQI(IntPtr _this, Guid* riid, IntPtr* ppv) => QIInternal(_this, riid, ppv);
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int SelectionQI(IntPtr _this, Guid* riid, IntPtr* ppv) => QIInternal(_this - IntPtr.Size, riid, ppv);

        internal static unsafe int QIInternal(IntPtr basePtr, Guid* riid, IntPtr* ppv)
        {
            var p = (UniversalObject*)basePtr;
            Guid req = *riid;
            *ppv = IntPtr.Zero;
            bool primary = false;
            if (req == Guids.IID_IUnknown) primary = true;
            else if (p->Type == ComObjectType.Factory && req == Guids.IID_IClassFactory) primary = true;
            else if (p->Type == ComObjectType.Command && (req == Guids.IID_IExplorerCommand || req == Guids.IID_IExplorerCommand_Alt)) primary = true;
            else if (p->Type == ComObjectType.Enum && (req == Guids.IID_IEnumExplorerCommand || req == Guids.IID_IEnumExplorerCommand_Alt)) primary = true;

            if (primary) { *ppv = basePtr; AddRefInternal(basePtr); return 0; }
            if (req == Guids.IID_IObjectWithSelection && (p->Type == ComObjectType.Command || p->Type == ComObjectType.Enum)) {
                *ppv = basePtr + IntPtr.Size; AddRefInternal(basePtr); return 0;
            }
            return -2147467262; 
        }

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
            string t = (idx == -1) ? "ContextTools (⚡)" : SubTitles[idx];
            *ppsz = Marshal.StringToCoTaskMemUni(t); return 0;
        }
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static unsafe int GetIcon(IntPtr _this, IntPtr psi, IntPtr* ppsz)
        {
            if (((UniversalObject*)_this)->Data == -1) {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextTools", "app.png");
                *ppsz = Marshal.StringToCoTaskMemUni(path); return 0;
            }
            return -2147467263;
        }
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int GetToolTip(IntPtr _this, IntPtr psi, IntPtr* p) { *p = IntPtr.Zero; return 0; }
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int GetCanonicalName(IntPtr _this, Guid* p) { *p = Guid.Empty; return 0; }
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int GetState(IntPtr _this, IntPtr psi, int slow, uint* p) { *p = 0; return 0; }
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
            try {
                if (psi != IntPtr.Zero) {
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
                                    if (!string.IsNullOrEmpty(path)) sb.Append(" \"").Append(path).Append("\"");
                                    Marshal.FreeCoTaskMem(namePtr);
                                }
                                delegate* unmanaged[Stdcall]<IntPtr, uint> releaseChild = (delegate* unmanaged[Stdcall]<IntPtr, uint>)(*(IntPtr*)(ivt + 2 * IntPtr.Size));
                                releaseChild(item);
                            }
                        }
                    }
                }
            } catch { }
            try {
                string app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextTools", "ContextTools.exe");
                Process.Start(new ProcessStartInfo(app, sb.ToString()) { UseShellExecute = true });
            } catch { }
            return 0;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static unsafe int EnumNext(IntPtr _this, uint celt, IntPtr* rgelt, uint* pcelt)
        {
            var p = (UniversalObject*)_this;
            uint f = 0; Guid iid = Guids.IID_IExplorerCommand;
            while (f < celt && p->Data < SubTitles.Length) {
                CreateObject(Exporter.GetCommandVt(), &iid, &rgelt[f], ComObjectType.Command, p->Data);
                p->Data++; f++;
            }
            if (pcelt != null) *pcelt = f;
            return f == celt ? 0 : 1;
        }
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int EnumSkip(IntPtr _this, uint c) { ((UniversalObject*)_this)->Data += (int)c; return 0; }
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int EnumReset(IntPtr _this) { ((UniversalObject*)_this)->Data = 0; return 0; }
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] public static unsafe int EnumClone(IntPtr _this, IntPtr* p) { *p = IntPtr.Zero; return -2147467263; }
    }
}

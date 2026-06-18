using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace ClickraShell
{
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
}

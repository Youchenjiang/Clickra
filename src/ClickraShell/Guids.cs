using System;

namespace ClickraShell
{
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
}

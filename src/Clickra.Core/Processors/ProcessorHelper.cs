namespace Clickra.Core.Processors
{
    public static class ProcessorHelper
    {
        public static int GetProgressBase(int fileIndex) => fileIndex * 100;
        public static int GetProgressMax(int totalFiles) => totalFiles * 100;
    }
}

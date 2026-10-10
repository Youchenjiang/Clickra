using Clickra.Core;
using Clickra.Core.Processors;

namespace Clickra_Fluent;

internal static class OfficeEnginePreflight
{
    public static bool TryValidate(string command, Func<string, string> localize, out string error)
    {
        error = "";
        if (OfficeEngineDetector.IsCommandReady(command)) return true;

        error = localize(OfficeEngineDetector.GetUnavailableErrorKey());
        return false;
    }
}

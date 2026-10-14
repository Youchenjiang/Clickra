using Clickra.Core;
using Clickra.Core.Processors;

namespace Clickra_Fluent;

internal static class OfficeEnginePreflight
{
    public static bool TryValidate(string command, Func<string, string> localize, out string error)
    {
        error = "";
        string engine = SettingsController.OfficeEngineValue;
        if (OfficeEngineDetector.IsCommandReady(
                command,
                engine,
                SettingsController.LibreOfficeRemovalPendingRestart,
                SettingsController.LibreOfficePath))
            return true;

        error = localize(OfficeEngineDetector.GetUnavailableErrorKey(engine));
        return false;
    }
}

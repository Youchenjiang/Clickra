using Clickra.Core;
using Clickra.Core.Processors;

namespace Clickra_Fluent;

internal static class OfficeEnginePreflight
{
    public static bool TryValidate(string command, Func<string, string> localize, out string error)
    {
        error = "";
        string engine = ClickraStorage.GetSetting(ClickraSettings.OfficeEngine);
        if (OfficeEngineDetector.IsCommandReady(
                command,
                engine,
                ClickraStorage.GetSettingBool(ClickraSettings.LibreOfficeRemovalPendingRestart),
                ClickraStorage.GetSetting(ClickraSettings.LibreOfficePath)))
            return true;

        error = localize(OfficeEngineDetector.GetUnavailableErrorKey(engine));
        return false;
    }
}

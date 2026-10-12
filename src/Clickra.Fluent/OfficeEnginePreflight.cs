using Clickra.Core;

namespace Clickra_Fluent;

internal static class OfficeEnginePreflight
{
    public static bool TryValidate(string command, Func<string, string> localize, out string error)
    {
        error = "";
        if (SettingsPresentationController.Shared.IsOfficeCommandReady(command))
            return true;

        error = localize(SettingsPresentationController.Shared.GetOfficeUnavailableErrorKey());
        return false;
    }
}

using Clickra.Core;
using Clickra.Core.Processors;

namespace Clickra_Fluent;

internal static class OfficeEnginePreflight
{
    public static bool TryValidate(string command, Func<string, string> localize, out string error)
    {
        error = "";
        string app = command switch
        {
            "ppt2pdf" => "PowerPoint",
            "word2pdf" => "Word",
            "excel2pdf" => "Excel",
            _ => ""
        };
        if (string.IsNullOrWhiteSpace(app)) return true;

        string engine = ClickraStorage.GetSetting("OfficeEngine");
        bool libreOfficeReady = !string.IsNullOrWhiteSpace(LibreOfficeHelper.GetResolvedExecutablePath());
        bool microsoftReady = IsOfficeInstalled(app);
        bool ready = engine.Equals("libreoffice", StringComparison.OrdinalIgnoreCase)
            ? libreOfficeReady
            : engine.Equals("microsoft", StringComparison.OrdinalIgnoreCase)
                ? microsoftReady
                : microsoftReady || libreOfficeReady;
        if (ready) return true;

        error = localize(engine.Equals("libreoffice", StringComparison.OrdinalIgnoreCase)
            ? "error_libreoffice_not_ready"
            : engine.Equals("microsoft", StringComparison.OrdinalIgnoreCase)
                ? "error_microsoftoffice_not_ready"
                : "setting_engine_none_available");
        return false;
    }

    private static bool IsOfficeInstalled(string app)
    {
        string progId = app switch
        {
            "PowerPoint" => "PowerPoint.Application",
            "Excel" => "Excel.Application",
            _ => "Word.Application"
        };
        return Type.GetTypeFromProgID(progId) != null;
    }
}

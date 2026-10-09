using System;
using Microsoft.Win32;

namespace Clickra.Core.Processors;

/// <summary>Shared Office-engine readiness contract used by conversion execution and UI preflight.</summary>
public static class OfficeEngineDetector
{
    public static string? GetOfficeAppForCommand(string command) => command switch
    {
        "ppt2pdf" => "PowerPoint",
        "word2pdf" => "Word",
        "excel2pdf" => "Excel",
        _ => null
    };

    public static string? GetMicrosoftOfficeProgId(string appType) => appType switch
    {
        "Word" => "Word.Application",
        "Excel" => "Excel.Application",
        "PowerPoint" => "PowerPoint.Application",
        _ => null
    };

    public static bool IsMicrosoftOfficeReady(string appType)
    {
        string? progId = GetMicrosoftOfficeProgId(appType);
        if (progId is null) return false;

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Classes\{progId}");
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsMicrosoftSuiteReady() =>
        IsMicrosoftOfficeReady("Word") &&
        IsMicrosoftOfficeReady("Excel") &&
        IsMicrosoftOfficeReady("PowerPoint");

    public static bool IsLibreOfficeReady() =>
        !string.IsNullOrWhiteSpace(LibreOfficeHelper.GetResolvedExecutablePath());

    public static bool IsCommandReady(string command)
    {
        string? app = GetOfficeAppForCommand(command);
        if (app is null) return true;

        return IsSelectedEngineReady(IsMicrosoftOfficeReady(app), IsLibreOfficeReady());
    }

    public static bool IsSelectedEngineReady(bool microsoftReady, bool libreOfficeReady)
    {
        string engine = ClickraStorage.GetSetting(ClickraSettings.OfficeEngine);
        if (engine.Equals(ClickraSettings.OfficeEngineLibreOffice, StringComparison.OrdinalIgnoreCase))
            return libreOfficeReady;
        if (engine.Equals(ClickraSettings.OfficeEngineMicrosoft, StringComparison.OrdinalIgnoreCase))
            return microsoftReady;
        return microsoftReady || libreOfficeReady;
    }

    public static string GetUnavailableErrorKey()
    {
        string engine = ClickraStorage.GetSetting(ClickraSettings.OfficeEngine);
        if (engine.Equals(ClickraSettings.OfficeEngineLibreOffice, StringComparison.OrdinalIgnoreCase))
            return "error_libreoffice_not_ready";
        if (engine.Equals(ClickraSettings.OfficeEngineMicrosoft, StringComparison.OrdinalIgnoreCase))
            return "error_microsoftoffice_not_ready";
        return "setting_engine_none_available";
    }

    /// <summary>Engine label value to show for the configured mode and currently available engines.</summary>
    public static string GetPreferredReadyEngine(bool microsoftReady, bool libreOfficeReady)
    {
        string engine = ClickraStorage.GetSetting(ClickraSettings.OfficeEngine);
        if (engine.Equals(ClickraSettings.OfficeEngineLibreOffice, StringComparison.OrdinalIgnoreCase))
            return ClickraSettings.OfficeEngineLibreOffice;
        if (engine.Equals(ClickraSettings.OfficeEngineMicrosoft, StringComparison.OrdinalIgnoreCase) || microsoftReady)
            return ClickraSettings.OfficeEngineMicrosoft;
        if (libreOfficeReady)
            return ClickraSettings.OfficeEngineLibreOffice;
        return ClickraSettings.DefaultOfficeEngineAuto;
    }
}
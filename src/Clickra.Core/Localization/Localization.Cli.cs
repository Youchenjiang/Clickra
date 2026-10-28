using Clickra.Core.Modules.Settings.Application;

namespace Clickra.Core;

public static partial class Localization
{
    /// <summary>
    /// 組裝本地化的診斷回報郵件主旨與內文範本。
    /// </summary>
    public static (string Subject, string Body) BuildDiagnosticsEmail(string version, string time, string? langCode = null)
    {
        string lang = langCode ?? SettingsController.Language;
        string subject = T("diag_email_subject", lang);
        string body =
            T("diag_email_thanks", lang) + "\r\n\r\n" +
            T("diag_email_attachment_hint", lang) + "\r\n\r\n" +
            T("diag_email_system_info", lang) + "\r\n" +
            T("diag_email_os", lang) + "\r\n" +
            string.Format(T("diag_email_version", lang), version) + "\r\n" +
            string.Format(T("diag_email_time", lang), time) + "\r\n\r\n" +
            T("diag_email_problem_desc", lang) + "\r\n" +
            T("diag_email_problem_placeholder", lang);

        return (subject, body);
    }
}

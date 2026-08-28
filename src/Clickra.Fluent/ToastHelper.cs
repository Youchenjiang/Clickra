using System;
using System.Diagnostics;
using Clickra.Core;

namespace Clickra_Fluent;

/// <summary>Shared Windows Toast notification helper used by both MainPage and TaskProgressPage.</summary>
internal static class ToastHelper
{
    /// <summary>Sends a Windows Toast notification unless notifications are disabled.</summary>
    internal static void Show(string title, string body)
    {
        if (ClickraStorage.GetSetting("Notification").Equals("false", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            static string Escape(string value) => value.Replace("'", "''").Replace("`", "``").Replace("\"", "`\"");
            var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
$textNodes = $template.GetElementsByTagName('text')
$textNodes.Item(0).AppendChild($template.CreateTextNode('{Escape(title)}')) | Out-Null
$textNodes.Item(1).AppendChild($template.CreateTextNode('{Escape(body)}')) | Out-Null
$toast = [Windows.UI.Notifications.ToastNotification]::new($template)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Clickra').Show($toast)";

            Process.Start(new ProcessStartInfo
            {
                FileName = SystemPaths.PowerShell,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.Dispose();
        }
        catch { /* Ignored: a failed toast must not break the conversion flow. */ }
    }
}

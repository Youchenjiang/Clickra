using System;
using Clickra.Core;
using Clickra.Core.Modules.Settings.Application;
using Clickra.Core.Modules.Settings.Model;

namespace Clickra.UI;

public static partial class DashboardWindow
{
    private static void HandleDynamicTextEdit(IntPtr hwnd, SettingDescriptor descriptor)
    {
        string subtitle = GetText("dialog_edit_setting_title");
        string prompt = $"{GetText(descriptor.TitleKey)} - {GetText("dialog_edit_setting_prompt")}";
        string currentValue = SettingsPresentationController.Shared.ReadText(descriptor);
        bool isPassword = string.Equals(descriptor.Key, SettingsContract.TranslateAiApiKey, StringComparison.OrdinalIgnoreCase);

        string? result = NativeDialogs.PromptText(
            parentHwnd: hwnd,
            subtitle: subtitle,
            prompt: prompt,
            initialValue: currentValue,
            isPassword: isPassword,
            dpiScale: _dpiScale,
            language: SettingsPresentationController.Shared.Language);

        if (result != null)
        {
            SettingsPresentationController.Shared.WriteText(descriptor, result);
        }
    }
}

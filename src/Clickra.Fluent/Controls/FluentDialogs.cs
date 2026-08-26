using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace Clickra_Fluent;

/// <summary>Shared modal dialogs used by multiple Fluent pages.</summary>
internal static class FluentDialogs
{
    /// <summary>Asks for a PDF password; returns null when the user cancels.</summary>
    public static async Task<string?> PromptPasswordAsync(XamlRoot xamlRoot, Func<string, string> localize)
    {
        var box = new PasswordBox { PlaceholderText = localize("fluent_pdf_password_placeholder") };
        var dialog = new ContentDialog
        {
            Title = localize("fluent_pdf_password"),
            Content = box,
            PrimaryButtonText = localize("fluent_ok"),
            CloseButtonText = localize("dialog_cancel"),
            XamlRoot = xamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Password : null;
    }
}

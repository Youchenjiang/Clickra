using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Clickra.Desktop.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        PdfCompressSlider.Steps = ["小檔案", "平衡", "高品質"];
        AppearanceBox.SelectionChanged += (_, _) => ApplyAppearance(AppearanceBox.SelectedIndex);
    }

    // Order matches the spec: 淺色 (default), 深色, 跟隨系統.
    private static void ApplyAppearance(int index)
    {
        if (Application.Current is null) return;
        Application.Current.RequestedThemeVariant = index switch
        {
            1 => ThemeVariant.Dark,
            2 => ThemeVariant.Default,
            _ => ThemeVariant.Light,
        };
    }
}

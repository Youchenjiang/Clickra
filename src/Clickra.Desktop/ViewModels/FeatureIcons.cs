using Avalonia;
using Avalonia.Media;

namespace Clickra.Desktop.ViewModels;

/// <summary>
/// The official feature icons (assets/features/*.svg), converted to vector resources by
/// scripts/generate_desktop_feature_icons.py into Assets/FeatureIcons.axaml. Same icons as the
/// Explorer context menu (spec 4.1).
/// </summary>
public static class FeatureIcons
{
    public static IImage? Get(string command)
    {
        if (string.IsNullOrEmpty(command) || Application.Current is not { } app) return null;
        return app.TryGetResource("Feature." + command, null, out object? value) ? value as IImage : null;
    }
}

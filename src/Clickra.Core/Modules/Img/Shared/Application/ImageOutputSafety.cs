using Clickra.Core.Modules.Sys.Settings.Application;
using Clickra.Core.Application;
namespace Clickra.Core.Modules.Img.Shared.Application;

internal static class ImageOutputSafety
{
    public static void EnsureUniqueOutputs(IEnumerable<string> outputs, string? language = null)
    {
        var duplicate = outputs
            .GroupBy(Path.GetFullPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is null) return;

        throw new InvalidOperationException(Localization.Format(
            "error_image_output_collision",
            language ?? SettingsController.Language,
            ("name", Path.GetFileName(duplicate.Key))));
    }

    public static void EnsureOutputsDoNotOverwriteInputs(
        IEnumerable<string> inputs,
        IEnumerable<string> outputs,
        string? language = null)
    {
        var inputPaths = inputs
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? collision = outputs
            .Select(Path.GetFullPath)
            .FirstOrDefault(inputPaths.Contains);
        if (collision is null) return;

        throw new InvalidOperationException(Localization.Format(
            "error_image_output_overwrites_input",
            language ?? SettingsController.Language,
            ("name", Path.GetFileName(collision))));
    }
}

namespace Clickra.Core.Application;

internal static class ImageOutputSafety
{
    public static void EnsureUniqueOutputs(IEnumerable<string> outputs)
    {
        var duplicate = outputs
            .GroupBy(Path.GetFullPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is null) return;

        string template = Localization.T(
            "error_image_output_collision",
            ClickraStorage.GetSetting(ClickraSettings.Language));
        throw new InvalidOperationException(string.Format(template, duplicate.Key));
    }

    public static void EnsureOutputsDoNotOverwriteInputs(
        IEnumerable<string> inputs,
        IEnumerable<string> outputs)
    {
        var inputPaths = inputs
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? collision = outputs
            .Select(Path.GetFullPath)
            .FirstOrDefault(inputPaths.Contains);
        if (collision is null) return;

        string template = Localization.T(
            "error_image_output_overwrites_input",
            ClickraStorage.GetSetting(ClickraSettings.Language));
        throw new InvalidOperationException(string.Format(template, collision));
    }
}

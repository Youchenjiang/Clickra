using System;
using Avalonia.Markup.Xaml;

namespace Clickra.Desktop.L10n;

/// <summary>
/// <c>Text="{l:T ui_key}"</c> in XAML: the text is looked up once when the view loads. No reflection,
/// so it is NativeAOT-safe.
/// </summary>
public sealed class TExtension : MarkupExtension
{
    public TExtension(string key) => Key = key;

    public string Key { get; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.T(Key);
}

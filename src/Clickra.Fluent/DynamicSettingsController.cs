using Clickra.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Clickra_Fluent;

/// <summary>Owns descriptor-driven Fluent settings controls, their persistence, synchronization, and localization.</summary>
internal sealed class DynamicSettingsController
{
    private sealed record DynamicSettingControl(
        SettingDescriptor Descriptor,
        FrameworkElement Card,
        TextBlock TitleBlock,
        TextBlock? DescBlock,
        FrameworkElement InputControl,
        TextBlock? ValueLabel);

    private static readonly HashSet<string> StaticSettingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ClickraSettings.OutputDir,
        ClickraSettings.OfficeEngine,
        ClickraSettings.Language,
        ClickraSettings.TranslateTargetLang,
        ClickraSettings.QuietMode,
        ClickraSettings.Notification,
        ClickraSettings.PdfCompressImageLevel,
        ClickraSettings.PdfCompressStripFonts,
        ClickraSettings.PdfCompressMinifyContent,
        ClickraSettings.ParkedTaskRetention,
    };

    private readonly Grid _settingsLayout;
    private readonly FrameworkElement _insertionAnchor;
    private readonly Func<string, string> _localize;
    private readonly Func<bool> _isLoading;
    private readonly Action _applyResponsiveLayout;
    private readonly List<DynamicSettingControl> _controls = new();

    internal DynamicSettingsController(
        Grid settingsLayout,
        FrameworkElement insertionAnchor,
        Func<string, string> localize,
        Func<bool> isLoading,
        Action applyResponsiveLayout)
    {
        _settingsLayout = settingsLayout;
        _insertionAnchor = insertionAnchor;
        _localize = localize;
        _isLoading = isLoading;
        _applyResponsiveLayout = applyResponsiveLayout;
    }

    internal void Build()
    {
        if (_controls.Count > 0) return;

        int insertIndex = _settingsLayout.Children.IndexOf(_insertionAnchor);
        if (insertIndex < 0) insertIndex = _settingsLayout.Children.Count;

        foreach (SettingDescriptor descriptor in SettingPageRegistry.AllDescriptors)
        {
            if (StaticSettingKeys.Contains(descriptor.Key)) continue;

            var card = new Grid
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(18),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            };
            var stack = new StackPanel { Spacing = 8 };
            var titleBlock = new TextBlock
            {
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Text = _localize(descriptor.TitleKey),
            };
            stack.Children.Add(titleBlock);

            TextBlock? descriptionBlock = null;
            if (!string.IsNullOrEmpty(descriptor.DescriptionKey))
            {
                descriptionBlock = new TextBlock
                {
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    Text = _localize(descriptor.DescriptionKey),
                    TextWrapping = TextWrapping.Wrap,
                };
                stack.Children.Add(descriptionBlock);
            }

            var (inputControl, valueLabel) = CreateInput(descriptor, stack);
            if (inputControl is null) continue;

            card.Children.Add(stack);
            _settingsLayout.Children.Insert(insertIndex++, card);
            _controls.Add(new DynamicSettingControl(
                descriptor,
                card,
                titleBlock,
                descriptionBlock,
                inputControl,
                valueLabel));
        }

        _applyResponsiveLayout();
    }

    internal void Sync()
    {
        foreach (DynamicSettingControl control in _controls)
        {
            switch (control.Descriptor.EditorKind)
            {
                case SettingEditorKind.Toggle:
                    SyncToggle(control);
                    break;
                case SettingEditorKind.Slider:
                    SyncSlider(control);
                    break;
                case SettingEditorKind.Number:
                    SyncNumber(control);
                    break;
                case SettingEditorKind.Choice:
                    SyncChoice(control);
                    break;
            }
        }
    }

    internal void ApplyLanguage()
    {
        foreach (DynamicSettingControl control in _controls)
        {
            control.TitleBlock.Text = _localize(control.Descriptor.TitleKey);
            if (control.DescBlock != null && !string.IsNullOrEmpty(control.Descriptor.DescriptionKey))
            {
                control.DescBlock.Text = _localize(control.Descriptor.DescriptionKey);
            }

            if (control.Descriptor.EditorKind == SettingEditorKind.Slider &&
                control.InputControl is Slider slider &&
                control.ValueLabel != null)
            {
                UpdateSliderLabel(control.ValueLabel, control.Descriptor, (int)slider.Value);
            }

            if (control.Descriptor.EditorKind == SettingEditorKind.Choice && control.InputControl is ComboBox comboBox)
            {
                ApplyChoiceLanguage(control.Descriptor, comboBox);
            }
        }
    }

    private (FrameworkElement? InputControl, TextBlock? ValueLabel) CreateInput(
        SettingDescriptor descriptor,
        StackPanel stack) => descriptor.EditorKind switch
        {
            SettingEditorKind.Toggle => (CreateToggle(descriptor, stack), null),
            SettingEditorKind.Slider => CreateSlider(descriptor, stack),
            SettingEditorKind.Number => (CreateNumber(descriptor, stack), null),
            SettingEditorKind.Choice => (CreateChoice(descriptor, stack), null),
            _ => (null, null),
        };

    private ToggleSwitch CreateToggle(SettingDescriptor descriptor, StackPanel stack)
    {
        var toggle = new ToggleSwitch { VerticalAlignment = VerticalAlignment.Center };
        toggle.Toggled += (_, _) =>
        {
            if (_isLoading()) return;
            ClickraStorage.SaveSetting(
                descriptor.Key,
                toggle.IsOn ? ClickraSettings.ValueTrue : ClickraSettings.ValueFalse);
        };
        stack.Children.Add(toggle);
        return toggle;
    }

    private (FrameworkElement InputControl, TextBlock ValueLabel) CreateSlider(
        SettingDescriptor descriptor,
        StackPanel stack)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        NumericSettingRange range = descriptor.GetEffectiveNumericRange() ?? new NumericSettingRange(0, 1, 0);
        var slider = new Slider
        {
            Minimum = range.Min,
            Maximum = range.Max,
            StepFrequency = 1,
            TickFrequency = 1,
        };
        var valueLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
        };
        Grid.SetColumn(slider, 0);
        Grid.SetColumn(valueLabel, 1);
        grid.Children.Add(slider);
        grid.Children.Add(valueLabel);

        slider.ValueChanged += (_, _) =>
        {
            if (_isLoading()) return;
            int level = Math.Clamp((int)slider.Value, range.Min, range.Max);
            ClickraStorage.SaveSetting(descriptor.Key, level.ToString());
            UpdateSliderLabel(valueLabel, descriptor, level);
        };

        stack.Children.Add(grid);
        return (slider, valueLabel);
    }

    private NumberBox CreateNumber(SettingDescriptor descriptor, StackPanel stack)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        NumericSettingRange range = descriptor.GetEffectiveNumericRange() ?? new NumericSettingRange(0, 100, 0);
        var numberBox = new NumberBox
        {
            Minimum = range.Min,
            Maximum = range.Max,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 160,
        };
        Grid.SetColumn(numberBox, 1);
        grid.Children.Add(numberBox);
        numberBox.ValueChanged += (_, _) =>
        {
            if (_isLoading() || double.IsNaN(numberBox.Value)) return;
            int clamped = Math.Clamp((int)numberBox.Value, range.Min, range.Max);
            ClickraStorage.SaveSetting(descriptor.Key, clamped.ToString());
        };

        stack.Children.Add(grid);
        return numberBox;
    }

    private ComboBox CreateChoice(SettingDescriptor descriptor, StackPanel stack)
    {
        var comboBox = new ComboBox { MinWidth = 260 };
        foreach (SettingOption option in descriptor.Options ?? Array.Empty<SettingOption>())
        {
            comboBox.Items.Add(new ComboBoxItem { Content = GetOptionLabel(option), Tag = option.Value });
        }
        comboBox.SelectionChanged += (_, _) =>
        {
            if (_isLoading()) return;
            if (comboBox.SelectedItem is ComboBoxItem item && item.Tag is string value)
            {
                ClickraStorage.SaveSetting(descriptor.Key, value);
            }
        };

        stack.Children.Add(comboBox);
        return comboBox;
    }

    private void SyncToggle(DynamicSettingControl control)
    {
        if (control.InputControl is ToggleSwitch toggle)
        {
            toggle.IsOn = ClickraStorage.GetSettingBool(control.Descriptor.Key);
        }
    }

    private void SyncSlider(DynamicSettingControl control)
    {
        if (control.InputControl is not Slider slider) return;
        SettingDescriptor descriptor = control.Descriptor;
        NumericSettingRange range = descriptor.GetEffectiveNumericRange() ?? new NumericSettingRange(0, 1, 0);
        int value = Math.Clamp(ClickraStorage.GetSettingInt(descriptor.Key), range.Min, range.Max);
        slider.Minimum = range.Min;
        slider.Maximum = range.Max;
        slider.Value = value;
        if (control.ValueLabel != null)
        {
            UpdateSliderLabel(control.ValueLabel, descriptor, value);
        }
    }

    private static void SyncNumber(DynamicSettingControl control)
    {
        if (control.InputControl is not NumberBox numberBox) return;
        SettingDescriptor descriptor = control.Descriptor;
        NumericSettingRange range = descriptor.GetEffectiveNumericRange() ?? new NumericSettingRange(0, 100, 0);
        numberBox.Minimum = range.Min;
        numberBox.Maximum = range.Max;
        numberBox.Value = Math.Clamp(ClickraStorage.GetSettingInt(descriptor.Key), range.Min, range.Max);
    }

    private static void SyncChoice(DynamicSettingControl control)
    {
        if (control.InputControl is not ComboBox comboBox) return;
        string value = ClickraStorage.GetSetting(control.Descriptor.Key);
        for (int i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag as string, value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void ApplyChoiceLanguage(SettingDescriptor descriptor, ComboBox comboBox)
    {
        IReadOnlyList<SettingOption> options = descriptor.Options ?? Array.Empty<SettingOption>();
        int count = Math.Min(comboBox.Items.Count, options.Count);
        for (int i = 0; i < count; i++)
        {
            if (comboBox.Items[i] is ComboBoxItem item)
            {
                item.Content = GetOptionLabel(options[i]);
            }
        }
    }

    private string GetOptionLabel(SettingOption option)
    {
        string label = _localize(option.LabelKey);
        return string.Equals(label, option.LabelKey, StringComparison.Ordinal) && !string.IsNullOrEmpty(option.FallbackText)
            ? option.FallbackText
            : label;
    }

    private void UpdateSliderLabel(TextBlock label, SettingDescriptor descriptor, int level)
    {
        label.Text = descriptor.SliderLabels != null && level >= 0 && level < descriptor.SliderLabels.Count
            ? _localize(descriptor.SliderLabels[level])
            : level.ToString();
    }
}

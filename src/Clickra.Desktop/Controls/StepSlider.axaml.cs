using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace Clickra.Desktop.Controls;

/// <summary>Slider that snaps to named steps and shows each step's name under its stop.</summary>
public partial class StepSlider : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<string>?> StepsProperty =
        AvaloniaProperty.Register<StepSlider, IReadOnlyList<string>?>(nameof(Steps));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<StepSlider, int>(nameof(SelectedIndex), defaultBindingMode: BindingMode.TwoWay);

    private bool _syncing;

    public StepSlider()
    {
        InitializeComponent();
        PART_Slider.PropertyChanged += OnSliderPropertyChanged;
    }

    /// <summary>Names of the steps, from "smaller / less" on the left to "larger / more" on the right.</summary>
    public IReadOnlyList<string>? Steps
    {
        get => GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }

    /// <summary>Index of the selected step.</summary>
    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StepsProperty)
        {
            RebuildLabels();
            SyncSlider();
        }
        else if (change.Property == SelectedIndexProperty)
        {
            SyncSlider();
            UpdateLabelEmphasis();
        }
    }

    private void OnSliderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_syncing || e.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty) return;
        int index = (int)Math.Round(PART_Slider.Value);
        if (index != SelectedIndex) SelectedIndex = index;
    }

    private void SyncSlider()
    {
        int count = Steps?.Count ?? 0;
        _syncing = true;
        try
        {
            PART_Slider.Maximum = Math.Max(1, count - 1);
            PART_Slider.Value = Math.Clamp(SelectedIndex, 0, Math.Max(0, count - 1));
            PART_Slider.IsEnabled = count > 1;
        }
        finally
        {
            _syncing = false;
        }

        string current = count > 0 ? Steps![Math.Clamp(SelectedIndex, 0, count - 1)] : string.Empty;
        Avalonia.Automation.AutomationProperties.SetName(PART_Slider, current);
    }

    private void RebuildLabels()
    {
        PART_Labels.Children.Clear();
        if (Steps is null) return;
        foreach (string step in Steps)
        {
            PART_Labels.Children.Add(new TextBlock { Text = step, FontSize = 12 });
        }

        UpdateLabelEmphasis();
    }

    private void UpdateLabelEmphasis()
    {
        for (int i = 0; i < PART_Labels.Children.Count; i++)
        {
            if (PART_Labels.Children[i] is not TextBlock label) continue;
            bool current = i == SelectedIndex;
            label.FontWeight = current ? FontWeight.SemiBold : FontWeight.Normal;
            label.Classes.Set("secondary", !current);
        }
    }
}

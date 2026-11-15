using Avalonia;
using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls.Primitives;
using Avalonia.Automation;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media.Transformation;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Clickra.Desktop.ViewModels;

using Clickra.Desktop.Controls;

namespace Clickra.Desktop.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        PdfCompressSlider.Steps = ["小檔案", "平衡", "高品質"];
        ImageCompressSlider.Steps = ["最小檔案", "小檔案", "標準", "高品質"];
        TextScaleSlider.Steps = ["0.9 倍", "1 倍", "1.12 倍"];

        TabSwitch.SelectionChanged += (_, _) =>
        {
            bool defaults = TabSwitch.SelectedIndex == 1;
            GeneralPanel.IsVisible = !defaults;
            DefaultsPanel.IsVisible = defaults;
        };
        AppearanceBox.SelectionChanged += (_, _) => ApplyAppearance(AppearanceBox.SelectedIndex);
        BuildContextMenuChecklist();
    }

    // ---- Right-click menu checklist (spec 4.4). UI state only; saving to settings lands in branch 7. ----

    private static readonly (string Group, string[] Items)[] MenuItems =
    [
        ("文件", ["Word → PDF", "Excel → PDF", "PPT → PDF", "MD → PDF", "MD → Word"]),
        ("PDF", ["合併 PDF", "分割 PDF", "壓縮 PDF", "PDF → 圖片", "翻譯 PDF", "解密 PDF", "列印 PDF"]),
        ("圖片", ["圖片 → PDF", "轉換圖片格式", "壓縮圖片", "合併圖片", "拼接圖片"]),
        ("整理", ["批次重新命名"]),
    ];

    private readonly List<(CheckBox Group, List<ToggleButton> Items)> _menuGroups = [];
    private bool _menuSyncing;

    private const string CheckGeometry = "M5,12.5 L9.5,17 L19,7.5";
    private const string PlusGeometry = "M12,6 V18 M6,12 H18";

    private void BuildContextMenuChecklist()
    {
        foreach ((string group, string[] items) in MenuItems)
        {
            var groupBox = new CheckBox
            {
                Content = new TextBlock { Text = group, FontSize = 15, FontWeight = FontWeight.SemiBold },
                MinHeight = 36,
                VerticalAlignment = VerticalAlignment.Top,
            };
            ToolTip.SetTip(groupBox, "全選；已全選時再點一下全部取消");

            var chips = new WrapPanel { Orientation = Orientation.Horizontal };
            var toggles = new List<ToggleButton>();
            foreach (string item in items)
            {
                var chip = new ToggleButton
                {
                    Classes = { "chip" },
                    IsChecked = true,
                    Margin = new Thickness(0, 0, 8, 8),
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children =
                        {
                            new Path { Classes = { "checkMark" }, Data = Geometry.Parse(CheckGeometry) },
                            new Path { Classes = { "plusMark" }, Data = Geometry.Parse(PlusGeometry) },
                            new TextBlock { Text = item, VerticalAlignment = VerticalAlignment.Center },
                        },
                    },
                };
                AutomationProperties.SetName(chip, item);
                chip.IsCheckedChanged += (_, _) => OnMenuItemChanged();
                toggles.Add(chip);
                chips.Children.Add(chip);
            }

            // Not all on -> select all; all on -> clear all (a partly selected box shows "-").
            // Decided from the chips, not the box's own toggle (which would send a partial box to "off").
            groupBox.Click += (_, _) =>
            {
                bool on = !toggles.All(c => c.IsChecked.GetValueOrDefault());
                _menuSyncing = true;
                foreach (ToggleButton chip in toggles) chip.IsChecked = on;
                _menuSyncing = false;
                OnMenuItemChanged();
            };

            _menuGroups.Add((groupBox, toggles));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("88,*"), Margin = new Thickness(0, 6, 0, 0) };
            Grid.SetColumn(chips, 1);
            row.Children.Add(groupBox);
            row.Children.Add(chips);
            MenuGroups.Children.Add(row);
        }

        MenuChevron.RenderTransform = TransformOperations.Parse("rotate(0deg)");
        MenuChevron.Transitions = Motion.TransformTransitions();
        MenuExpandButton.Click += (_, _) => SetMenuExpanded(MenuChecklist, MenuEnabledBox.IsChecked.GetValueOrDefault(), MenuChevron, !MenuChecklist.IsVisible);
        MenuEnabledBox.IsCheckedChanged += (_, _) => UpdateMenuState();
        UpdateMenuState();
    }

    private static void SetMenuExpanded(Control checklist, bool isEnabled, Control chevron, bool expanded)
    {
        bool wasVisible = checklist.IsVisible;
        checklist.IsVisible = expanded && isEnabled;
        chevron.RenderTransform = TransformOperations.Parse(checklist.IsVisible ? "rotate(180deg)" : "rotate(0deg)");
        if (checklist.IsVisible && !wasVisible) Motion.Enter(checklist, 0, -6, Motion.Fast);
    }

    private void OnMenuItemChanged()
    {
        if (_menuSyncing) return;
        UpdateMenuState();
    }

    private void UpdateMenuState()
    {
        int total = _menuGroups.Sum(g => g.Items.Count);
        int shown = _menuGroups.Sum(g => g.Items.Count(b => b.IsChecked.GetValueOrDefault()));

        _menuSyncing = true;
        foreach (var (groupBox, items) in _menuGroups)
        {
            int on = items.Count(b => b.IsChecked.GetValueOrDefault());
            if (on == items.Count)
            {
                groupBox.IsChecked = true;
            }
            else if (on == 0)
            {
                groupBox.IsChecked = false;
            }
            else
            {
                groupBox.IsChecked = null;
            }
        }
        _menuSyncing = false;

        bool enabled = MenuEnabledBox.IsChecked.GetValueOrDefault();
        MenuExpandButton.IsVisible = enabled;
        if (!enabled) SetMenuExpanded(MenuChecklist, false, MenuChevron, false);
        MenuSummary.Text = FormatMenuSummary(enabled, shown, total);
    }

    private static string FormatMenuSummary(bool enabled, int shown, int total)
    {
        if (!enabled) return "已關閉";
        if (shown == total) return $"{total} 項全部顯示（預設）";
        if (shown == 0) return "沒有勾選任何功能，右鍵選單不會出現 Clickra";
        return $"顯示 {shown} 項，已隱藏 {total - shown} 項";
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

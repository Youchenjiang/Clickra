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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S1075", Justification = "Project homepage URI is fixed and documented.")]
    private const string RepositoryUrl = "https://github.com/Youchenjiang/Clickra";

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
        // 轉好的檔案存到: choosing 自訂… opens a folder picker; cancelling keeps the previous choice.
        OutputDirBox.SelectionChanged += async (_, _) =>
        {
            if (DataContext is not SettingsViewModel vm || OutputDirBox.SelectedIndex == vm.OutputDirKind) return;
            if (OutputDirBox.SelectedIndex != 3)
            {
                vm.OutputDirKind = OutputDirBox.SelectedIndex;
                return;
            }
            if (TopLevel.GetTopLevel(this) is not { } top) return;
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "選擇轉好的檔案要存到哪裡",
                AllowMultiple = false,
            });
            string? path = folders.Count > 0 ? Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(folders[0]) : null;
            if (string.IsNullOrWhiteSpace(path))
            {
                OutputDirBox.SelectedIndex = vm.OutputDirKind; // cancelled
                return;
            }
            vm.CustomOutputDir = path;
            vm.OutputDirKind = 3;
        };
        ViewLogsButton.Click += (_, _) => SettingsViewModel.OpenLogsFolder();
        SendReportButton.Click += (_, _) => SettingsViewModel.SendDiagnosticsEmail();
        GitHubButton.Click += (_, _) => OpenExternalUri(RepositoryUrl);
        BuildContextMenuChecklist();
    }

    private MainWindowViewModel? _subscribedVm;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToViewModel((TopLevel.GetTopLevel(this) as Window)?.DataContext as MainWindowViewModel ?? DataContext as MainWindowViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        SubscribeToViewModel(null);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        SubscribeToViewModel((TopLevel.GetTopLevel(this) as Window)?.DataContext as MainWindowViewModel ?? DataContext as MainWindowViewModel);
    }

    private static void OpenExternalUri(string uri) => Clickra.Desktop.ViewModels.ExternalOpen.Path(uri);

    private void SubscribeToViewModel(MainWindowViewModel? vm)
    {
        if (ReferenceEquals(_subscribedVm, vm)) return;
        if (_subscribedVm != null)
        {
            _subscribedVm.SettingsSectionRequested -= OnSettingsSectionRequested;
        }
        _subscribedVm = vm;
        if (_subscribedVm != null)
        {
            _subscribedVm.SettingsSectionRequested += OnSettingsSectionRequested;
        }
    }

    private void OnSettingsSectionRequested(object? sender, SettingsSectionEventArgs e)
    {
        if (!ReferenceEquals(_subscribedVm, sender)) return;
        if (e.Section == SettingsSection.ContextMenu)
        {
            TabSwitch.SelectedIndex = 0;
            Dispatcher.UIThread.Post(() =>
            {
                ContextMenuGroup.BringIntoView();
                HighlightContextMenuGroup(ContextMenuHighlight);
            }, DispatcherPriority.Loaded);
        }
    }

    private static void HighlightContextMenuGroup(Control highlight)
    {
        if (!Motion.Enabled) return;
        highlight.Opacity = 0.25;
        DispatcherTimer.RunOnce(() => highlight.Opacity = 0, TimeSpan.FromMilliseconds(500));
    }

    // ---- Right-click menu checklist (spec 4.4). Saved to settings; the shell extension (ClickraShell.dll)
    // reads the same settings on every right-click, so a change applies the next time the menu opens. ----

    private static readonly string[] ImageFormats = [.. Clickra.Shared.ContextMenuVisibility.ImageFormatCommands];

    private static readonly (string Group, (string Label, string[] Commands)[] Items)[] MenuItems =
    [
        ("文件", [("Word → PDF", ["word2pdf"]), ("Excel → PDF", ["excel2pdf"]), ("PPT → PDF", ["ppt2pdf"]),
                  ("MD → PDF", ["md2pdf"]), ("MD → Word", ["md2word"])]),
        ("PDF", [("合併 PDF", ["merge-pdf"]), ("分割 PDF", ["split-pdf"]), ("壓縮 PDF", ["compress-pdf"]),
                 ("PDF → 圖片", ["pdf2img"]), ("翻譯 PDF", ["translate-pdf"]), ("解密 PDF", ["decrypt-pdf"]),
                 ("列印 PDF", ["print-pdf"])]),
        ("圖片", [("圖片 → PDF", ["img2pdf"]), ("轉換圖片格式", ImageFormats), ("壓縮圖片", ["img-compress"]),
                 ("合併圖片", ["img-merge"]), ("拼接圖片", ["img-stitch"])]),
        ("整理", [("批次重新命名", ["batch-rename"])]),
    ];

    private readonly List<(CheckBox Group, List<ToggleButton> Items)> _menuGroups = [];
    private readonly Dictionary<ToggleButton, string[]> _menuCommands = [];
    private bool _menuLoaded;
    private bool _menuSyncing;

    private const string CheckGeometry = "M5,12.5 L9.5,17 L19,7.5";
    private const string PlusGeometry = "M12,6 V18 M6,12 H18";

    /// <summary>A chip mark on the shared 24 x 24 icon grid (Styles/Controls.axaml, Viewbox.ic).</summary>
    private static Viewbox Mark(string role, string geometry)
    {
        var box = new Viewbox
        {
            Child = new Canvas { Width = 24, Height = 24, Children = { new Path { Data = Geometry.Parse(geometry) } } },
        };
        box.Classes.Add("ic");
        box.Classes.Add(role);
        return box;
    }

    private void BuildContextMenuChecklist()
    {
        HashSet<string> hidden = Clickra.Shared.ContextMenuVisibility.ParseHidden(
            Clickra.Core.Storage.ClickraStorage.GetSetting(Clickra.Core.Storage.ClickraSettings.ContextMenuHidden));
        MenuEnabledBox.IsChecked = Clickra.Core.Storage.ClickraStorage.GetSettingBool(Clickra.Core.Storage.ClickraSettings.ContextMenuEnabled);

        foreach ((string group, (string Label, string[] Commands)[] items) in MenuItems)
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
            foreach ((string item, string[] commands) in items)
            {
                var chip = new ToggleButton
                {
                    Classes = { "chip" },
                    IsChecked = !commands.All(hidden.Contains),
                    Margin = new Thickness(0, 0, 8, 8),
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children =
                        {
                            Mark("checkMark", CheckGeometry),
                            Mark("plusMark", PlusGeometry),
                            new TextBlock { Text = item, VerticalAlignment = VerticalAlignment.Center },
                        },
                    },
                };
                AutomationProperties.SetName(chip, item);
                chip.IsCheckedChanged += (_, _) => OnMenuItemChanged();
                _menuCommands[chip] = commands;
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
        _menuLoaded = true;
    }

    /// <summary>Writes the master switch and the unchecked commands to settings.</summary>
    private void SaveMenuSettings()
    {
        if (!_menuLoaded) return;
        bool enabled = MenuEnabledBox.IsChecked.GetValueOrDefault();
        string hidden = Clickra.Shared.ContextMenuVisibility.JoinHidden(
            _menuCommands.Where(pair => !pair.Key.IsChecked.GetValueOrDefault()).SelectMany(pair => pair.Value));
        try
        {
            Clickra.Core.Storage.ClickraStorage.SaveSetting(Clickra.Core.Storage.ClickraSettings.ContextMenuEnabled,
                enabled ? Clickra.Core.Storage.ClickraSettings.ValueTrue : Clickra.Core.Storage.ClickraSettings.ValueFalse);
            Clickra.Core.Storage.ClickraStorage.SaveSetting(Clickra.Core.Storage.ClickraSettings.ContextMenuHidden, hidden);
        }
        catch
        {
            // Best effort: the checklist still shows the user's choice.
        }
    }

    private static void SetMenuExpanded(Control checklist, bool isEnabled, Control chevron, bool expanded)
    {
        bool wasVisible = checklist.IsVisible;
        checklist.IsVisible = expanded && isEnabled;
        chevron.Transitions = Motion.TransformTransitions();
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
        SaveMenuSettings();
    }

    private static string FormatMenuSummary(bool enabled, int shown, int total)
    {
        if (!enabled) return "已關閉";
        if (shown == total) return $"{total} 項全部顯示（預設）";
        if (shown == 0) return "沒有勾選任何功能，右鍵選單不會出現 Clickra";
        return $"顯示 {shown} 項，已隱藏 {total - shown} 項";
    }
}

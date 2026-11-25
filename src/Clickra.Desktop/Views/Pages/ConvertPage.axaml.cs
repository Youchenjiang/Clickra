using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Data;
using Avalonia.Threading;
using Clickra.Desktop.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Clickra.Desktop.ViewModels;
using Clickra.Desktop.L10n;

namespace Clickra.Desktop.Views.Pages;

public partial class ConvertPage : UserControl
{
    private const double MinOptionGap = 14, MaxOptionGap = 24;
    private bool _optionGapQueued;

    private void QueueFitOptionGap()
    {
        if (_optionGapQueued) return;
        _optionGapQueued = true;
        Dispatcher.UIThread.Post(() => { _optionGapQueued = false; FitOptionGap(); }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Spec 4.9: when the window has room, the gaps between options grow (14 up to 24) instead of leaving
    /// empty space under the card; when it is tight they stay at 14 so the card fits without scrolling
    /// for as long as possible. The card is measured at two gaps to know how many gaps it has.
    /// </summary>
    private void FitOptionGap()
    {
        if (!OptionsScroller.IsVisible) return;
        double viewport = OptionsScroller.Bounds.Height, width = OptionsScroller.Bounds.Width;
        if (viewport <= 0 || width <= 0) return;

        List<StackPanel> stacks = OptionsCard.GetVisualDescendants().OfType<StackPanel>()
            .Where(p => p.Classes.Contains("optGap") && p.IsEffectivelyVisible).ToList();
        List<OptionRow> rows = OptionsCard.GetVisualDescendants().OfType<OptionRow>()
            .Where(r => r.IsEffectivelyVisible).ToList();
        double current = stacks.Count > 0 ? stacks[0].Spacing : MinOptionGap;

        double HeightAt(double gap)
        {
            foreach (StackPanel p in stacks) p.Spacing = gap;
            foreach (OptionRow r in rows) r.RowSpacing = gap;
            // A parent keeps its cached size until it is invalidated itself, so invalidate the whole card.
            foreach (Layoutable l in OptionsCard.GetVisualDescendants().OfType<Layoutable>()) l.InvalidateMeasure();
            OptionsCard.InvalidateMeasure();
            OptionsCard.Measure(new Size(width, double.PositiveInfinity));
            return OptionsCard.DesiredSize.Height;
        }

        double atMin = HeightAt(MinOptionGap);
        double perGap = HeightAt(MinOptionGap + 1) - atMin; // how many gaps the visible card has
        double gap = perGap <= 0 ? MinOptionGap
            : Math.Clamp(MinOptionGap + Math.Floor((viewport - atMin) / perGap), MinOptionGap, MaxOptionGap);
        HeightAt(gap);
        if (Math.Abs(gap - current) > 0.1) OptionsCard.InvalidateMeasure();
    }

    public ConvertPage()
    {
        InitializeComponent();

        SizeChanged += (_, e) => LimitBottom(BottomSection, e.NewSize.Height, _startRows);
        CatalogScroll.SizeChanged += (_, _) => FitCatalog();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ConvertViewModel vm)
            {
                vm.Options.TemplatePickRequested -= OnTemplatePickRequested;
                vm.Options.TemplatePickRequested += OnTemplatePickRequested;
            }
        };
        OrderList.AddHandler(PointerPressedEvent, OnOrderPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        OrderList.AddHandler(PointerMovedEvent, OnOrderPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        OrderList.AddHandler(PointerReleasedEvent, (_, _) => _dragItem = null, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        OrderList.AddHandler(PointerCaptureLostEvent, (_, _) => _dragItem = null);
        CatalogItems.LayoutUpdated += (_, _) => FitCatalog();
        AddHandler(KeyDownEvent, OnSplitPreviewKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        SplitPreviewScroll.SizeChanged += (_, _) => UpdateSplitPreviewViewport();
        SplitPreviewScroll.AddHandler(PointerWheelChangedEvent, OnSplitPreviewWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        OptionsScroller.SizeChanged += (_, _) => QueueFitOptionGap();
        OptionsCard.SizeChanged += (_, _) => QueueFitOptionGap();

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);

        PickFilesButton.Click += async (_, _) => await PickFilesAsync();
        ManageFilesButton.Click += (_, _) => GetViewModel()?.ToggleManageFiles();
        DoneManageFilesButton.Click += (_, _) => GetViewModel()?.ToggleManageFiles();
        BackToStartButton.Click += (_, _) => GetViewModel()?.ResetToStart();
        BackToCatalogButton.Click += (_, _) => GetViewModel()?.BackToCatalog();
        StartConversionButton.Click += async (_, _) =>
        {
            if (GetViewModel() is { } vm)
            {
                await vm.StartConversionAsync();
            }
        };
        CancelConversionButton.Click += (_, _) => GetViewModel()?.CancelConversion();
        ProcessOtherFilesButton.Click += (_, _) => GetViewModel()?.ResetToStart();
        OpenOutputFolderButton.Click += (_, _) => GetViewModel()?.OpenOutputDirectory();

        MenuSettingsLink.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel mainVm })
            {
                mainVm.OpenSettings(SettingsSection.ContextMenu);
            }
        };

        ViewAllHistoryButton.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel mainVm })
            {
                mainVm.CurrentPage = MainPage.History;
            }
        };
    }

    // Spec 4.1 / 6: with no files the drop zone takes the height that 最近處理 leaves, but never less than 240;
    // the 最近處理 section hugs its rows (at most 3) and drops rows that no longer fit.
    private const double DropZoneMin = 240;
    private const double PageBottomMargin = 24, RowGap = 12;

    private bool _startRows = true;

    /// <summary>選擇 Word 範本: a .docx picker; cancelling keeps the previous template (spec 4.9).</summary>
    private async void OnTemplatePickRequested(object? sender, EventArgs e)
    {
        if (sender is not ActionOptionsViewModel options || TopLevel.GetTopLevel(this) is not { } top) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.T("ui_markdown_choose_a_word_template"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Strings.T("ui_markdown_word_file_type")) { Patterns = ["*.docx"] }],
        });
        string? path = files.Count > 0 ? files[0].Path.LocalPath : null;
        if (string.IsNullOrWhiteSpace(path)) options.CancelTemplatePick();
        else options.SetTemplate(path);
    }

    // ---- Dragging rows of the merge/stitch order list by their handle (spec 4.6). ----
    private OrderItemViewModel? _dragItem;

    private void OnOrderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source) return;
        Border? handle = source as Border ?? source.FindAncestorOfType<Border>(includeSelf: true);
        while (handle is not null && !handle.Classes.Contains("dragHandle")) handle = handle.FindAncestorOfType<Border>();
        if (handle?.DataContext is not OrderItemViewModel item || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
        _dragItem = item;
        e.Pointer.Capture(OrderList);
        e.Handled = true;
    }

    private void OnOrderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem is null || GetViewModel() is not { } vm) return;
        double y = e.GetPosition(OrderList).Y;
        int index = 0;
        foreach (Control row in OrderList.GetRealizedContainers())
        {
            if (y > row.Bounds.Y + row.Bounds.Height / 2) index++;
        }
        int from = vm.Order.Items.IndexOf(_dragItem);
        int target = index > from ? index - 1 : index;
        if (target != from) vm.Order.Move(_dragItem, target);
    }

    private double _normalCardHeight;

    /// <summary>
    /// Spec 6: up to two rows of action cards must not scroll. When the window opens shorter than 660
    /// (it is limited to 80% of the screen, e.g. 588 on a 1080p screen at 140%), two rows of normal cards
    /// may not fit; the cards then switch to a compact size instead of showing a scroll bar. More than two
    /// rows (over 8 actions) still scroll inside the card area.
    /// </summary>
    private void FitCatalog()
    {
        if (!CatalogPanel.IsVisible) return;
        int count = CatalogItems.ItemCount;
        int rows = (count + 3) / 4;
        bool compact = CatalogItems.Classes.Contains("compact");
        // The tallest card sets the row (a card waiting for 解密 has one more line); includes the card margin.
        double tallest = compact ? 0 : CatalogItems.GetRealizedContainers().Select(c => c.DesiredSize.Height).DefaultIfEmpty(0).Max();
        if (tallest > 0) _normalCardHeight = tallest;
        if (rows is 0 or > 2 || _normalCardHeight <= 0)
        {
            if (compact) CatalogItems.Classes.Remove("compact");
            return;
        }
        bool needCompact = rows * _normalCardHeight > CatalogScroll.Bounds.Height + 0.5;
        if (needCompact != compact) CatalogItems.Classes.Set("compact", needCompact);
    }

    private static void LimitBottom(Border bottomSection, double pageHeight, bool start)
    {
        bottomSection.MaxHeight = start
            ? Math.Max(0, pageHeight - PageBottomMargin - RowGap - DropZoneMin)
            : double.PositiveInfinity;
    }

    /// <summary>
    /// State A: drop zone row fills (min 240) and the bottom row hugs its content.
    /// Other states: the 72 file row on top and the bottom section fills the rest.
    /// </summary>
    private void ApplyRows(bool start)
    {
        RowDefinition top = PageGrid.RowDefinitions[0], bottom = PageGrid.RowDefinitions[2];
        top.Height = start ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        top.MinHeight = start ? DropZoneMin : 0;
        bottom.Height = start ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        TopArea.VerticalAlignment = start ? Avalonia.Layout.VerticalAlignment.Stretch : Avalonia.Layout.VerticalAlignment.Top;
        _startRows = start;
        LimitBottom(BottomSection, Bounds.Height, start);
    }

    private ConvertViewModel? GetViewModel() => DataContext as ConvertViewModel;

    // ===== State transitions (spec 4.1) =====

    private const double FileRowHeight = 72;
    private static readonly TimeSpan CollapseTime = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PageSlideTime = TimeSpan.FromMilliseconds(360);
    private static readonly TimeSpan CardStep = TimeSpan.FromMilliseconds(40);

    private ConvertViewModel? _observedVm;
    private ConvertWorkflowState _shownState = ConvertWorkflowState.DropZone;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_observedVm is not null) _observedVm.PropertyChanged -= OnViewModelPropertyChanged;
        _observedVm = GetViewModel();
        if (_observedVm is null) return;
        _observedVm.PropertyChanged += OnViewModelPropertyChanged;
        _shownState = _observedVm.State;
        ApplyRows(_observedVm.IsDropZone);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConvertViewModel.State)) return;
        double topHeight = TopArea.Bounds.Height; // before the new state is laid out
        // Run after the view model has raised all of its Is* properties (so every panel's IsVisible is final),
        // and before the next frame is drawn.
        Dispatcher.UIThread.Post(() =>
        {
            if (GetViewModel() is not { } vm) return;
            ConvertWorkflowState from = _shownState, to = vm.State;
            _shownState = to;
            bool startChanged = (from == ConvertWorkflowState.DropZone) != (to == ConvertWorkflowState.DropZone);
            if (startChanged) ApplyRows(to == ConvertWorkflowState.DropZone);
            if (from != to && IsEffectivelyVisible && Motion.Enabled) Animate(from, to, topHeight);
        }, DispatcherPriority.Normal);
    }

    private void Animate(ConvertWorkflowState from, ConvertWorkflowState to, double topHeight)
    {
        if (from == ConvertWorkflowState.DropZone)
        {
            // A -> B: the drop zone collapses into the file row, the bottom content fades out,
            // then the action cards fade in one by one from below.
            ResizeTop(DropZone, FileNarrowRow, topHeight, FileRowHeight);
            // 最近處理 keeps its size while it fades: the section grows as the box shrinks, and the row list
            // would otherwise show a third row for a moment.
            StartBottom.Height = StartBottom.Bounds.Height;
            StartBottom.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            Motion.FadeOut(StartBottom, Motion.Fast);
            // The catalog appears only after 最近處理 has faded, so the two titles never overlap, and its
            // scroll bar stays hidden while the area is still too small.
            CatalogScroll.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden;
            Motion.Enter(CatalogPanel, 0, 0, Motion.Normal, delay: Motion.Fast);
            if (to == ConvertWorkflowState.ActionCatalog) StaggerCards(offsetY: 16, start: TimeSpan.FromMilliseconds(180));
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                StartBottom.ClearValue(HeightProperty);
                StartBottom.ClearValue(VerticalAlignmentProperty);
                CatalogScroll.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            }, CollapseTime + TimeSpan.FromMilliseconds(50));
            return;
        }

        if (to == ConvertWorkflowState.DropZone)
        {
            // 重新選: the same animation played backwards — the box grows downward from the 72 row and
            // pushes the section below down with it, instead of the section jumping to the bottom first.
            UpdateLayout(); // lay out state A once to learn how tall the drop zone will be
            double target = TopArea.Bounds.Height;
            ApplyRows(start: false); // keep the B layout while growing; state A rows return when done
            ResizeTop(FileNarrowRow, DropZone, FileRowHeight, target);
            if (BottomPanelFor(from) is { } old) Motion.FadeOut(old, Motion.Fast);
            Motion.Enter(StartBottom, 0, 12, TimeSpan.FromMilliseconds(260), delay: TimeSpan.FromMilliseconds(200));
            return;
        }

        if (from == ConvertWorkflowState.ActionCatalog && to == ConvertWorkflowState.ActionPage)
        {
            // Into an action: the action page slides in from the right and its icon pops slightly.
            Motion.Enter(ActionPagePanel, 40, 0, PageSlideTime);
            Motion.Pop(ActionIcon);
            return;
        }

        if (from == ConvertWorkflowState.ActionPage && to == ConvertWorkflowState.ActionCatalog)
        {
            // ‹ 所有動作: the catalog slides back from the left and the cards fade in one by one.
            Motion.Enter(CatalogPanel, -40, 0, PageSlideTime);
            StaggerCards(offsetY: 8, start: TimeSpan.FromMilliseconds(60));
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S2325:Methods and properties that don't access instance data should be static", Justification = "Accesses generated XAML controls")]
    private Control? BottomPanelFor(ConvertWorkflowState state) => state switch
    {
        ConvertWorkflowState.DropZone => StartBottom,
        ConvertWorkflowState.ActionCatalog => CatalogPanel,
        ConvertWorkflowState.ActionPage => ActionPagePanel,
        _ => null,
    };

    /// <summary>
    /// Shrinks or grows the top box between the two top states (0.5 s, fast then slow) while the old content
    /// fades out over the first half and the new content fades in over the second. This is the one animation
    /// that changes a size (spec 7 "動畫的限制"), so it is limited to this single box.
    /// </summary>
    private void ResizeTop(Control leaving, Control arriving, double fromHeight, double toHeight)
    {
        IDisposable? keepLeaving = leaving.SetValue(IsVisibleProperty, true, BindingPriority.Animation);
        // The drop zone keeps its full size while the box shrinks or grows around it (clipped, not squeezed).
        Control dropZone = ReferenceEquals(leaving, DropZone) ? leaving : arriving;
        dropZone.Height = Math.Max(fromHeight, toHeight);
        dropZone.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        TopArea.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        leaving.IsHitTestVisible = false;
        leaving.Transitions = null;  // opacity is set per frame below
        arriving.Transitions = null;
        arriving.Opacity = 0;
        Motion.Tween(TopArea, CollapseTime, new CubicEaseOut(), (eased, linear) =>
        {
            TopArea.Height = fromHeight + (toHeight - fromHeight) * eased;
            leaving.Opacity = Math.Max(0, 1 - linear * 2);
            arriving.Opacity = Math.Clamp((linear - 0.4) / 0.6, 0, 1);
        }, () =>
        {
            keepLeaving?.Dispose();
            leaving.ClearValue(OpacityProperty);
            leaving.ClearValue(IsHitTestVisibleProperty);
            arriving.ClearValue(OpacityProperty);
            TopArea.ClearValue(HeightProperty);
            dropZone.ClearValue(HeightProperty);
            dropZone.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            ApplyRows(GetViewModel()?.IsDropZone ?? true);
        });
    }

    private void StaggerCards(double offsetY, TimeSpan start)
    {
        UpdateLayout(); // the catalog was hidden until now, so its cards are created by this layout pass
        int index = 0;
        foreach (Control card in CatalogItems.GetRealizedContainers())
        {
            Motion.Enter(card, 0, offsetY, TimeSpan.FromMilliseconds(260), delay: start + CardStep * index++);
        }
    }

    private async System.Threading.Tasks.Task PickFilesAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.T("ui_start_choose_files_to_process"),
            AllowMultiple = true
        });

        if (files.Count > 0)
        {
            var paths = files.Select(f => f.Path.LocalPath).ToList();
            GetViewModel()?.AddFiles(paths);
        }
    }

    // ===== 分割：放大預覽 (spec 4.6) =====

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S2325:Methods and properties that don't access instance data should be static", Justification = "XAML event handler")]
    private void OnSplitPageDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SplitPageViewModel page) page.OpenPreview();
    }

    private void OnSplitPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var zoom = GetViewModel()?.ActiveZoomPreview;
        if (zoom is null || !zoom.IsOpen) return;
        switch (e.Key)
        {
            case Key.Left: zoom.Previous(); e.Handled = true; break;
            case Key.Right: zoom.Next(); e.Handled = true; break;
            case Key.Escape: zoom.Close(); e.Handled = true; break;
            case Key.OemPlus or Key.Add when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                zoom.ZoomIn(); e.Handled = true; break;
            case Key.OemMinus or Key.Subtract when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                zoom.ZoomOut(); e.Handled = true; break;
            default:
                // Unhandled keys pass through to standard navigation
                break;
        }
    }

    /// <summary>Ctrl+wheel zooms the preview; a plain wheel still scrolls the page.</summary>
    private void OnSplitPreviewWheel(object? sender, PointerWheelEventArgs e)
    {
        var zoom = GetViewModel()?.ActiveZoomPreview;
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || zoom is null || !zoom.IsOpen) return;
        if (e.Delta.Y > 0) zoom.ZoomIn();
        else if (e.Delta.Y < 0) zoom.ZoomOut();
        e.Handled = true;
    }

    /// <summary>The page is sized from the preview area (minus the 16 px margin on each side) and drawn sharp for
    /// the screen's scaling.</summary>
    private void UpdateSplitPreviewViewport()
    {
        var zoom = GetViewModel()?.ActiveZoomPreview;
        if (zoom is null) return;
        Size size = SplitPreviewScroll.Bounds.Size;
        zoom.SetViewport(size.Width - 32, size.Height - 32, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
    }

    // ===== State D: files dragged over the window (spec 4.1) =====

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (GetViewModel() is not { IsExecuting: false } vm) return;
        List<string> paths = e.DataTransfer.TryGetFiles()?.Select(f => f.Path.LocalPath).ToList() ?? [];
        if (paths.Count == 0) return;
        ShowDragOverlay(vm, paths);
        if (!DragOverlay.IsVisible)
        {
            DragOverlay.IsVisible = true;
            Motion.Enter(DragOverlay, 0, 0, Motion.Fast);
        }
    }

    /// <summary>
    /// What will happen on release (spec 4.1 state D): a folder adds the files directly inside it, and files
    /// Clickra cannot convert can only be renamed. Said before the user lets go, so nothing silently disappears.
    /// </summary>
    private void ShowDragOverlay(ConvertViewModel vm, List<string> paths)
    {
        List<string> folders = paths.Where(System.IO.Directory.Exists).ToList();
        List<string> files = ConvertViewModel.ExpandFolders(paths).ToList();
        (string text, string sub) = BuildDragOverlayTexts(vm.IsDropZone, paths.Count, folders, files);
        bool nothing = files.Count == 0;

        DragOverlayText.Text = text;
        DragOverlaySub.Text = sub;
        DragOverlaySub.IsVisible = sub.Length > 0;
        DragOverlayFrame.Stroke = nothing
            ? (Avalonia.Media.IBrush?)this.FindResource("Clk.ControlBorder")
            : (Avalonia.Media.IBrush?)this.FindResource("Clk.Accent");
    }

    private static (string text, string sub) BuildDragOverlayTexts(bool isDropZone, int rawPathsCount, List<string> folders, List<string> files)
    {
        if (files.Count == 0)
        {
            string emptyTitle = folders.Count == 1
                ? Strings.F("ui_drop_folder_empty", ("name", System.IO.Path.GetFileName(folders[0].TrimEnd('\\', '/'))))
                : Strings.T("ui_drop_these_folders_have_no_files");
            return (emptyTitle, Strings.T("ui_drop_only_files_in_this_folder"));
        }

        string title;
        if (folders.Count == 1 && rawPathsCount == 1)
        {
            title = Strings.F("ui_drop_add_from_folder", ("name", System.IO.Path.GetFileName(folders[0].TrimEnd('\\', '/'))), ("count", files.Count));
        }
        else
        {
            title = isDropZone && folders.Count == 0 ? Strings.T("ui_drop_drop_to_start") : Strings.Count("ui_drop_add_files", files.Count);
        }

        List<string> extensions = files.Select(f => System.IO.Path.GetExtension(f).ToLowerInvariant()).Distinct().ToList();
        List<string> unknown = extensions.Where(x => !ConvertViewModel.HasConversions(x)).ToList();
        string subText = "";
        if (unknown.Count > 0 && unknown.Count == extensions.Count)
        {
            subText = Strings.F("ui_drop_unsupported", ("kind", ConvertViewModel.KindName(unknown)));
        }
        else if (folders.Count > 0)
        {
            subText = Strings.T("ui_drop_only_files_in_this_folder");
        }

        return (title, subText);
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        // Moving between child elements also raises DragLeave; only hide when the pointer left the page.
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) return;
        DragOverlay.IsVisible = false;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = GetViewModel() is { IsExecuting: false } ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        List<string> paths = e.DataTransfer.TryGetFiles()?.Select(f => f.Path.LocalPath).ToList() ?? [];
        if (paths.Count > 0 && !ConvertViewModel.ExpandFolders(paths).Any())
        {
            // Nothing to add (empty folders): keep the explanation up for a moment instead of silently doing nothing.
            DispatcherTimer.RunOnce(() => DragOverlay.IsVisible = false, TimeSpan.FromSeconds(2.5));
            return;
        }
        DragOverlay.IsVisible = false;
        if (paths.Count > 0)
        {
            GetViewModel()?.AddFiles(paths);
        }
    }
}

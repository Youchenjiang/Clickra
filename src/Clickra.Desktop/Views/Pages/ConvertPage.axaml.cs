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

namespace Clickra.Desktop.Views.Pages;

public partial class ConvertPage : UserControl
{
    public ConvertPage()
    {
        InitializeComponent();

        SizeChanged += (_, e) => LimitBottom(e.NewSize.Height, _startRows);
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
        OptionsScroller.SizeChanged += (_, _) => QueueFitOptionGap();
        OptionsCard.SizeChanged += (_, _) => QueueFitOptionGap();

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        PickFilesButton.Click += async (_, _) => await PickFilesAsync();
        ManageFilesButton.Click += (_, _) => GetViewModel()?.ToggleManageFiles();
        DoneManageFilesButton.Click += (_, _) => GetViewModel()?.ToggleManageFiles();
        RePickFilesButton.Click += (_, _) => GetViewModel()?.ResetToStart();
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
        if (!compact && CatalogItems.GetRealizedContainers().FirstOrDefault() is Control first && first.DesiredSize.Height > 0)
            _normalCardHeight = first.DesiredSize.Height; // includes the card margin
        if (rows is 0 or > 2 || _normalCardHeight <= 0)
        {
            if (compact) CatalogItems.Classes.Remove("compact");
            return;
        }
        bool needCompact = rows * _normalCardHeight > CatalogScroll.Bounds.Height + 0.5;
        if (needCompact != compact) CatalogItems.Classes.Set("compact", needCompact);
    }

    private void LimitBottom(double pageHeight, bool start)
    {
        BottomSection.MaxHeight = start
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
        LimitBottom(Bounds.Height, start);
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
            Title = "選擇要處理的檔案",
            AllowMultiple = true
        });

        if (files.Count > 0)
        {
            var paths = files.Select(f => f.Path.LocalPath).ToList();
            GetViewModel()?.AddFiles(paths);
        }
    }

}

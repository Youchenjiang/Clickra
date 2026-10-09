using Clickra.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Clickra_Fluent;

public sealed partial class MainPage
{
    private const int CompletedHistoryLimit = 50;
    private HistoryFeed _historyFeed = HistoryFeed.Empty;
    private List<HistoryItem> _activeTasks = new();
    private List<HistoryItem> _parkedTasks = new();
    private List<HistoryItem> _historyEntries = new();
    private bool _parkedRefreshHooked;
    private int _selectedHistoryIndex = -1;
    private void RefreshHistory()
    {
        // 讀一次清單：進行中、待繼續、已完成都在裡面，統計卡也是同一份數字。
        _historyFeed = HistoryFeed.Load(CompletedHistoryLimit);
        _activeTasks = _historyFeed.OfKind(HistoryItemKind.Active).ToList();
        _parkedTasks = _historyFeed.OfKind(HistoryItemKind.Parked).ToList();
        _historyEntries = _historyFeed.OfKind(HistoryItemKind.Completed).ToList();

        StatTotal.Text = _historyFeed.CompletedCount.ToString();
        StatSuccess.Text = _historyFeed.SuccessCount.ToString();
        StatFailed.Text = _historyFeed.FailedCount.ToString();
        HistoryTotalText.Text = _historyFeed.CompletedCount.ToString();
        HistorySuccessText.Text = _historyFeed.SuccessCount.ToString();
        HistoryFailedText.Text = _historyFeed.FailedCount.ToString();
        RenderOverviewHistory(_historyEntries);

        RenderActiveTasks();
        RenderParkedTasks();

        // 「尚無紀錄」只在整份清單都空的時候出現：有人在跑或有待繼續任務時，這頁有東西可看。
        bool nothingAtAll = _historyEntries.Count == 0 && _activeTasks.Count == 0 && _parkedTasks.Count == 0;
        EmptyHistoryState.Visibility = nothingAtAll ? Visibility.Visible : Visibility.Collapsed;
        HistoryListContainer.Visibility = _historyEntries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        HistoryListContainer.Children.Clear();

        if (_historyEntries.Count == 0)
        {
            _selectedHistoryIndex = -1;
            RenderEmptyHistoryDetail();
            return;
        }

        if (_selectedHistoryIndex < 0 || _selectedHistoryIndex >= _historyEntries.Count)
        {
            _selectedHistoryIndex = 0;
        }

        for (int i = 0; i < _historyEntries.Count; i++)
        {
            HistoryListContainer.Children.Add(CreateHistoryListItem(_historyEntries[i], i));
        }

        RenderHistoryDetail(_historyEntries[_selectedHistoryIndex]);
    }

    /// <summary>回到 dashboard 時同步「待繼續」清單：暫存是在任務視窗完成的，主視窗要能立刻反映。</summary>
    private void HookMainWindowActivatedForParkedRefresh()
    {
        if (_parkedRefreshHooked || App.MainWindow is not { } mainWindow) return;
        _parkedRefreshHooked = true;
        mainWindow.Activated += (_, _) => RefreshParkedTasks();
    }

    /// <summary>待繼續清單只是共用清單的一個切片，所以「重新整理待繼續」就是重新讀整份清單。</summary>
    private void RefreshParkedTasks() => RefreshHistory();

    /// <summary>Lists the parked (paused) conversions in the History page. The park toast promises
    /// this page is where they can be resumed, cancelled, or given their own retention, so the card
    /// stays hidden while none exist.</summary>
    private void RenderParkedTasks()
    {
        ParkedTasksContainer.Children.Clear();
        ParkedTasksSection.Visibility = _parkedTasks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_parkedTasks.Count == 0) return;

        int expiringSoonCount = _historyFeed.ExpiringSoonCount;

        if (expiringSoonCount > 0)
        {
            ParkedTasksDesc.Text = string.Format(L("task_parked_expiring_warning"), expiringSoonCount);
            ParkedTasksDesc.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 160, 40));
        }
        else
        {
            ParkedTasksDesc.Text = L("task_parked_desc");
            ParkedTasksDesc.Foreground = (Brush)Application.Current.Resources[SecondaryTextBrushResource];
        }

        foreach (var item in _parkedTasks)
        {
            ParkedTasksContainer.Children.Add(CreateParkedTaskRow(item));
        }
    }

    /// <summary>Conversions running in another window (or from the command line): the Win32 dashboard
    /// already lists these rows, so both pages render the same items from the same feed.</summary>
    private void RenderActiveTasks()
    {
        ActiveTasksTitle.Text = L("task_active_title");
        ActiveTasksContainer.Children.Clear();
        ActiveTasksSection.Visibility = _activeTasks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_activeTasks.Count == 0) return;

        foreach (var item in _activeTasks)
        {
            ActiveTasksContainer.Children.Add(CreateActiveTaskRow(item));
        }
    }

    /// <summary>One running conversion: command, file, and the shared status word.</summary>
    private Grid CreateActiveTaskRow(HistoryItem item)
    {
        var row = CreateHistoryTaskRowShell(item, out var texts, out var titleRow);
        titleRow.Children.Add(new TextBlock
        {
            Text = L(item.StatusKey),
            FontSize = 12,
            Foreground = StatusBrushFor(item),
            VerticalAlignment = VerticalAlignment.Center
        });
        texts.Children.Add(titleRow);
        texts.Children.Add(new TextBlock
        {
            Text = $"{item.Time} · {item.FileCountText}",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources[SecondaryTextBrushResource],
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        Grid.SetColumn(texts, 0);
        row.Children.Add(texts);
        return row;
    }

    /// <summary>One parked conversion: command, where it stopped, its own remaining retention, and the
    /// actions that apply to it alone (resume, cancel, extend/shorten the deadline, or follow the
    /// global policy again).</summary>
    private Grid CreateParkedTaskRow(HistoryItem item)
    {
        var task = item.Entry;
        var row = CreateHistoryTaskRowShell(item, out var texts, out var titleRow);

        if (item.NeedsAttention)
        {
            row.BorderThickness = new Thickness(1);
            row.BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 140, 0));
        }

        if (item.NeedsAttention)
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 140, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 140, 0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "⚠️ " + L("task_parked_badge_expiring"),
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 160, 40))
                }
            };
            titleRow.Children.Add(badge);
        }

        texts.Children.Add(titleRow);

        texts.Children.Add(new TextBlock
        {
            // Subtitle 與期限都來自共用模型：另一個介面印的就是同一句話。
            Text = string.Join(" · ", new[] { item.Subtitle, item.RetentionText }.Where(part => !string.IsNullOrWhiteSpace(part))),
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources[SecondaryTextBrushResource],
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        // 單一任務的保留期限：不必為了某一件改動整個政策。天數的加減、下限與夾取都在
        // Core，所以這兩個介面移動的永遠是同一個天數。
        int step = ClickraSettings.ParkedRetentionStepDays;
        var shortenButton = new Button
        {
            Content = string.Format(L("task_parked_shorten_days"), step),
            Padding = new Thickness(14, 6, 14, 6)
        };
        shortenButton.Click += (_, _) => AdjustParkedRetention(item, -step);
        var extendButton = new Button
        {
            Content = string.Format(L("task_parked_extend_days"), step),
            Padding = new Thickness(14, 6, 14, 6),
            // 「永久保留」的任務沒有延長可言：它本來就不會過期。
            IsEnabled = item.Retention?.IsUnlimited != true
        };
        extendButton.Click += (_, _) => AdjustParkedRetention(item, +step);
        actions.Children.Add(shortenButton);
        actions.Children.Add(extendButton);
        if (item.HasRetentionOverride)
        {
            var resetButton = new Button
            {
                Content = L("task_parked_retention_reset"),
                Padding = new Thickness(14, 6, 14, 6)
            };
            resetButton.Click += (_, _) => ResetParkedRetention(item);
            actions.Children.Add(resetButton);
        }

        var resumeButton = new Button { Content = L("fluent_task_resume"), Padding = new Thickness(14, 6, 14, 6) };
        resumeButton.Click += (_, _) => ResumeParkedTask(task);
        var cancelButton = new Button { Content = L("dialog_cancel"), Padding = new Thickness(14, 6, 14, 6) };
        cancelButton.Click += async (_, _) => await CancelParkedTaskAsync(task);
        actions.Children.Add(resumeButton);
        actions.Children.Add(cancelButton);

        Grid.SetColumn(texts, 0);
        Grid.SetColumn(actions, 1);
        row.Children.Add(texts);
        row.Children.Add(actions);
        return row;
    }

    private Grid CreateHistoryTaskRowShell(HistoryItem item, out StackPanel texts, out StackPanel titleRow)
    {
        var row = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(12, 10, 12, 10),
            Background = (Brush)Application.Current.Resources[SecondaryCardBrushResource],
            CornerRadius = new CornerRadius(8)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        texts = new StackPanel { Spacing = 2 };
        titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = L(item.CommandLabelKey),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }

    /// <summary>Adjusts this one parked conversion's retention. The deadline arithmetic (including the
    /// floor that keeps a shortened task from being deleted outright) lives in Core, so both interfaces
    /// move the same number of days; a null result means there was nothing to adjust.</summary>
    private void AdjustParkedRetention(HistoryItem item, int deltaDays)
    {
        if (string.IsNullOrWhiteSpace(item.Entry.Id)) return;
        if (ClickraStorage.AdjustParkedRetention(item.Entry.Id, deltaDays) is null) return;
        RefreshHistory();
    }

    /// <summary>Clears this task's own retention so it follows the global policy again.</summary>
    private void ResetParkedRetention(HistoryItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Entry.Id)) return;
        ClickraStorage.SetParkedRetentionOverride(item.Entry.Id, null);
        RefreshHistory();
    }

    /// <summary>Resumes a parked conversion through the shared "resume" entry point, so the persisted
    /// task keeps its identity and next-file index. The status is left untouched here: TaskProgressPage
    /// only accepts a Parked task, and it flips the task to InProgress itself once it starts.</summary>
    private void ResumeParkedTask(ClickraStorage.HistoryEntry task)
    {
        if (string.IsNullOrWhiteSpace(task.Id)) return;
        if (App.FindTaskPage(task.Id) is { } alreadyRunning)
        {
            alreadyRunning.ShowWindow();
            return;
        }

        App.OpenTaskProgressWindow($"resume {task.Id}");
        RefreshParkedTasks();
    }

    /// <summary>Cancels a parked conversion after confirmation; the Canceled history line is written by Core.</summary>
    private async Task CancelParkedTaskAsync(ClickraStorage.HistoryEntry task)
    {
        if (string.IsNullOrWhiteSpace(task.Id)) return;
        if (!await ConfirmAsync(L("task_parked_cancel_confirm"))) return;
        ClickraStorage.CancelParkedTask(task.Id);
        RefreshHistory();
    }

    /// <summary>Status colour: green success, gray canceled, red failure; the shape of the row comes
    /// from the shared item, so both interfaces colour the same three outcomes the same way.</summary>
    private static SolidColorBrush StatusBrushFor(HistoryItem item)
    {
        if (item.Kind == HistoryItemKind.Active)
            return new(item.Entry.Status == ConversionStatus.Pending
                ? Color.FromArgb(255, 180, 180, 100)
                : Color.FromArgb(255, 80, 160, 240));
        if (item.IsSuccess) return new(Colors.LimeGreen);
        if (item.IsCanceled) return new(Colors.Gray);
        return new(Colors.IndianRed);
    }

    /// <summary>Status chip background matching <see cref="StatusBrushFor"/>.</summary>
    private static SolidColorBrush StatusBackgroundFor(HistoryItem item)
    {
        if (item.Kind == HistoryItemKind.Active) return new(Color.FromArgb(36, 80, 160, 240));
        if (item.IsSuccess) return new(Color.FromArgb(36, 57, 211, 83));
        if (item.IsCanceled) return new(Color.FromArgb(36, 128, 128, 128));
        return new(Color.FromArgb(40, 255, 107, 107));
    }

    /// <summary>Localized status label for a history item: the wording is the model's, so this page
    /// and the Win32 dashboard cannot describe the same entry differently.</summary>
    private static string StatusLabelFor(HistoryItem item) => L(item.StatusKey);

    private void RenderOverviewHistory(IReadOnlyList<HistoryItem> history)
    {
        OverviewRecentContainer.Children.Clear();
        OverviewNoHistoryText.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var item in history.Take(3))
        {
            var statusBrush = StatusBrushFor(item);
            var row = new Grid
            {
                ColumnSpacing = 10,
                Padding = new Thickness(12, 10, 12, 10),
                Background = (Brush)Application.Current.Resources[SecondaryCardBrushResource],
                CornerRadius = new CornerRadius(8)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = statusBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            var title = new StackPanel { Spacing = 2 };
            title.Children.Add(new TextBlock
            {
                Text = L(item.CommandLabelKey),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            title.Children.Add(new TextBlock
            {
                Text = item.Time,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources[SecondaryTextBrushResource],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var elapsed = new TextBlock
            {
                Text = FormatElapsed(item.ElapsedMs),
                FontSize = 12,
                Foreground = statusBrush,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(dot, 0);
            Grid.SetColumn(title, 1);
            Grid.SetColumn(elapsed, 2);
            row.Children.Add(dot);
            row.Children.Add(title);
            row.Children.Add(elapsed);
            OverviewRecentContainer.Children.Add(row);
        }
    }

    private Button CreateHistoryListItem(HistoryItem item, int index)
    {
        var selected = index == _selectedHistoryIndex;
        var statusBrush = StatusBrushFor(item);

        var row = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(12, 10, 12, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var status = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = statusBrush,
            VerticalAlignment = VerticalAlignment.Center
        };

        var title = new StackPanel { Spacing = 3 };
        title.Children.Add(new TextBlock
        {
            Text = L(item.CommandLabelKey),
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        title.Children.Add(new TextBlock
        {
            Text = $"{item.Time} · {item.FileCountText}",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources[SecondaryTextBrushResource],
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var result = new StackPanel
        {
            Spacing = 3,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        result.Children.Add(new TextBlock
        {
            Text = StatusLabelFor(item),
            FontSize = 13,
            Foreground = statusBrush,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        result.Children.Add(new TextBlock
        {
            Text = FormatElapsed(item.ElapsedMs),
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources[SecondaryTextBrushResource],
            HorizontalAlignment = HorizontalAlignment.Right
        });

        Grid.SetColumn(status, 0);
        Grid.SetColumn(title, 1);
        Grid.SetColumn(result, 2);
        row.Children.Add(status);
        row.Children.Add(title);
        row.Children.Add(result);

        var button = new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources[selected ? SecondaryCardBrushResource : "CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources[selected ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(selected ? 2 : 1),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8)
        };
        button.Click += (_, _) => SelectHistoryEntry(index);
        return button;
    }

    private void SelectHistoryEntry(int index)
    {
        if (index < 0 || index >= _historyEntries.Count) return;
        _selectedHistoryIndex = index;
        RefreshHistory();
    }

    private void RenderEmptyHistoryDetail()
    {
        HistoryDetailContainer.Children.Clear();
        HistoryDetailContainer.Children.Add(new FontIcon
        {
            Glyph = "\uE81C",
            FontSize = 42,
            Foreground = (Brush)Application.Current.Resources[SecondaryTextBrushResource],
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 120, 0, 0)
        });
        HistoryDetailContainer.Children.Add(new TextBlock
        {
            Text = L("fluent_select_history"),
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources[SecondaryTextBrushResource],
            HorizontalAlignment = HorizontalAlignment.Center
        });
    }

    private void RenderHistoryDetail(HistoryItem item)
    {
        HistoryDetailContainer.Children.Clear();
        var statusBrush = StatusBrushFor(item);

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel { Spacing = 4 };
        title.Children.Add(new TextBlock
        {
            Text = L(item.CommandLabelKey),
            FontSize = 26,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        title.Children.Add(new TextBlock
        {
            Text = item.Time,
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources[SecondaryTextBrushResource]
        });

        var status = new Border
        {
            Background = StatusBackgroundFor(item),
            BorderBrush = statusBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = StatusLabelFor(item),
                FontSize = 13,
                Foreground = statusBrush
            }
        };

        Grid.SetColumn(status, 1);
        header.Children.Add(title);
        header.Children.Add(status);
        HistoryDetailContainer.Children.Add(header);

        var facts = new Grid
        {
            ColumnSpacing = 12,
            Background = (Brush)Application.Current.Resources[SecondaryCardBrushResource],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14)
        };
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddFact(facts, 0, L("fluent_files"), item.FileCountText);
        AddFact(facts, 1, L("fluent_elapsed"), FormatElapsed(item.ElapsedMs));
        AddFact(facts, 2, L("fluent_result"), StatusLabelFor(item), statusBrush);
        HistoryDetailContainer.Children.Add(facts);

        AddDetailSection(L("history_detail_inputs"), SplitPaths(item.Entry.InputPaths));
        AddDetailSection(L("history_detail_outputs"), SplitPaths(item.Entry.OutputPath));
        if (!item.IsSuccess && !string.IsNullOrWhiteSpace(item.Entry.ErrorMessage))
        {
            AddDetailSection(L("history_detail_error"), item.Entry.ErrorMessage, statusBrush, true);
        }
    }

    private static void AddFact(Grid grid, int column, string label, string value, Brush? valueBrush = null)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources[SecondaryTextBrushResource]
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = valueBrush ?? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
            TextWrapping = TextWrapping.Wrap
        });

        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
    }

    private void AddDetailSection(string label, string value, Brush? valueBrush = null, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        HistoryDetailContainer.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources[SecondaryTextBrushResource],
            Margin = new Thickness(0, 4, 0, -6)
        });

        HistoryDetailContainer.Children.Add(new Border
        {
            Background = isError ? new SolidColorBrush(Color.FromArgb(32, 255, 107, 107)) : (Brush)Application.Current.Resources[SecondaryCardBrushResource],
            BorderBrush = isError ? valueBrush : (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = new TextBlock
            {
                Text = value,
                FontSize = 13,
                Foreground = valueBrush ?? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                TextWrapping = TextWrapping.Wrap
            }
        });
    }

    private async Task ClearHistoryAsync()
    {
        if (!await ConfirmAsync(L("history_clear_confirm"))) return;
        ClickraStorage.ClearHistory();
        _selectedHistoryIndex = -1;
        RefreshHistory();
    }
    private static string FormatElapsed(long elapsedMs) => elapsedMs >= 0 ? $"{elapsedMs / 1000.0:F2}s" : "-";

    private static string SplitPaths(string paths) => string.Join(Environment.NewLine, paths.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Clickra.Desktop.ViewModels;

namespace Clickra.Desktop.Views.Pages;

public partial class ConvertPage : UserControl
{
    public ConvertPage()
    {
        InitializeComponent();

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
            if (VisualRoot is Window { DataContext: MainWindowViewModel mainVm })
            {
                mainVm.OpenSettings(SettingsSection.ContextMenu);
            }
        };

        ViewAllHistoryButton.Click += (_, _) =>
        {
            if (VisualRoot is Window { DataContext: MainWindowViewModel mainVm })
            {
                mainVm.CurrentPage = MainPage.History;
            }
        };
    }

    private ConvertViewModel? GetViewModel() => DataContext as ConvertViewModel;

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

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is { } files)
        {
            var paths = files.Select(f => f.Path.LocalPath).ToList();
            if (paths.Count > 0)
            {
                GetViewModel()?.AddFiles(paths);
            }
        }
    }
}

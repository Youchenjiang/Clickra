using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Clickra.Core.Modules.Pdf.Split.Application;

namespace Clickra.Desktop.ViewModels;

/// <summary>One file in the order list: position, name and page count (PDF) or size (image).</summary>
public sealed class OrderItemViewModel(string path) : ViewModelBase
{
    private int _position;
    private string _detail = "";

    public string Path { get; } = path;
    public string Name { get; } = System.IO.Path.GetFileName(path);
    public int Position { get => _position; set { _position = value; OnPropertyChanged(); OnPropertyChanged(nameof(PositionText)); } }
    public string PositionText => $"{_position}";
    private bool _canUp, _canDown;
    public bool CanMoveUp { get => _canUp; set { _canUp = value; OnPropertyChanged(); } }
    public bool CanMoveDown { get => _canDown; set { _canDown = value; OnPropertyChanged(); } }
    public string Detail { get => _detail; set { _detail = value; OnPropertyChanged(); } }
    public DateTime Modified { get; } = File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.MinValue;
}

/// <summary>
/// 合併與拼接的工作區 (spec 4.6): the order of the files is the order of the result. Rows can be dragged
/// or moved with ↑ ↓, the list sorted by name or modified time or reversed, and the output named.
/// </summary>
public sealed class OrderWorkspaceViewModel : ViewModelBase
{
    private string _command = "";
    private string _outputName = "";
    private int _sortIndex = -1;

    public ObservableCollection<OrderItemViewModel> Items { get; } = [];

    public void SetFiles(string command, IEnumerable<string> files)
    {
        _command = command;
        Items.Clear();
        foreach (string file in files) Items.Add(new OrderItemViewModel(file));
        Renumber();
        _sortIndex = -1;
        OutputName = DefaultOutputName();
        OnPropertyChanged(nameof(SortIndex));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(OutputExtension));
        _ = LoadDetailsAsync(Items.ToList());
    }

    public string Title => _command == "img-stitch" ? "拼接順序（由上到下）" : "合併順序";
    public string OutputExtension => _command == "img-stitch" ? ".png" : ".pdf";
    public string CountText => $"{Items.Count} 個檔案";

    public string OutputName
    {
        get => _outputName;
        set { _outputName = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(HasOutputName)); }
    }
    public bool HasOutputName => _outputName.Trim().Length > 0;

    /// <summary>0 = 依檔名, 1 = 依修改時間; -1 = 自己排的順序.</summary>
    public int SortIndex
    {
        get => _sortIndex;
        set
        {
            if (value == _sortIndex) return;
            _sortIndex = value;
            OnPropertyChanged();
            if (value == 0) Reorder(Items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
            else if (value == 1) Reorder(Items.OrderBy(i => i.Modified).ToList());
        }
    }

    public void Reverse()
    {
        Reorder(Items.Reverse().ToList());
        ClearSort();
    }

    public void MoveUp(OrderItemViewModel item) => Move(item, Items.IndexOf(item) - 1);
    public void MoveDown(OrderItemViewModel item) => Move(item, Items.IndexOf(item) + 1);

    /// <summary>Moves a row to another position (also used by dragging).</summary>
    public void Move(OrderItemViewModel item, int target)
    {
        int from = Items.IndexOf(item);
        target = Math.Clamp(target, 0, Items.Count - 1);
        if (from < 0 || from == target) return;
        Items.Move(from, target);
        Renumber();
        ClearSort();
    }

    public IReadOnlyList<string> OrderedFiles => Items.Select(i => i.Path).ToList();

    public Dictionary<string, object> BuildOptions() => new(StringComparer.Ordinal)
    {
        [Clickra.Core.Application.SingleOutputConversionUseCaseBase.OutputNameOptionKey] = _outputName.Trim(),
    };

    private void ClearSort()
    {
        _sortIndex = -1;
        OnPropertyChanged(nameof(SortIndex));
    }

    private void Reorder(List<OrderItemViewModel> ordered)
    {
        for (int i = 0; i < ordered.Count; i++)
        {
            int current = Items.IndexOf(ordered[i]);
            if (current != i) Items.Move(current, i);
        }
        Renumber();
    }

    private void Renumber()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].Position = i + 1;
            Items[i].CanMoveUp = i > 0;
            Items[i].CanMoveDown = i < Items.Count - 1;
        }
        OnPropertyChanged(nameof(CountText));
    }

    private string DefaultOutputName()
    {
        string first = Items.Count > 0 ? System.IO.Path.GetFileNameWithoutExtension(Items[0].Name) : "";
        string suffix = _command == "img-stitch" ? "_長圖" : "_合併";
        return first.Length == 0 ? suffix.TrimStart('_') : first + suffix;
    }

    /// <summary>Page counts are read in the background so a long list does not block the page.</summary>
    private static async Task LoadDetailsAsync(List<OrderItemViewModel> items)
    {
        foreach (OrderItemViewModel item in items)
        {
            string detail = await Task.Run(() => Describe(item.Path));
            Dispatcher.UIThread.Post(() => item.Detail = detail);
        }
    }

    private static string Describe(string path)
    {
        try
        {
            if (System.IO.Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                int pages = PdfPageCountQuery.Get(path);
                return pages > 0 ? $"{pages} 頁" : "";
            }
            long bytes = new FileInfo(path).Length;
            return bytes < 1024 * 1024 ? $"{bytes / 1024.0:0} KB" : $"{bytes / (1024.0 * 1024.0):0.0} MB";
        }
        catch
        {
            return ""; // e.g. a password-protected PDF: the count is just left out
        }
    }
}

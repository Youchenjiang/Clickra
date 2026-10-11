using Clickra.Core.Processors;

namespace Clickra.Core.Application;

/// <summary>
/// Mutable selection state for conversion workspaces. UI surfaces own presentation and file-system
/// picking; this type owns command compatibility, default routing, run state, and selection identity.
/// </summary>
public sealed class ConversionWorkspaceState
{
    private readonly List<string> _selectedFiles = new();
    private readonly IReadOnlyList<string> _selectedFilesView;

    public ConversionWorkspaceState()
    {
        _selectedFilesView = _selectedFiles.AsReadOnly();
    }

    public IReadOnlyList<string> SelectedFiles => _selectedFilesView;

    public string? SelectedCommand { get; private set; }

    public bool IsRunning { get; private set; }

    public bool CanStart =>
        !IsRunning &&
        _selectedFiles.Count > 0 &&
        SelectedCommand is not null &&
        IsCommandCompatible(SelectedCommand);

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                _selectedFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            _selectedFiles.Add(path);
        }

        if (SelectedCommand is null || !IsCommandCompatible(SelectedCommand))
        {
            SelectedCommand = ConvertCommandRegistry.GetDefaultCommandForFiles(_selectedFiles);
        }
    }

    public void ClearFiles() => _selectedFiles.Clear();

    public bool TrySelectCommand(string command)
    {
        if (!IsCommandCompatible(command)) return false;
        SelectedCommand = command;
        return true;
    }

    public bool IsCommandCompatible(string command)
    {
        if (!ConvertCommandRegistry.IsKnownCommand(command)) return false;
        if (_selectedFiles.Count == 0) return true;
        if (_selectedFiles.Count < ConvertCommandRegistry.GetMinFiles(command)) return false;

        string[] extensions = ConvertCommandRegistry.GetAllowedExtensions(command);
        return _selectedFiles.All(file =>
            extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
    }

    public void NormalizeSelectedCommand()
    {
        if (SelectedCommand is not null && !IsCommandCompatible(SelectedCommand))
        {
            SelectedCommand = null;
        }
    }

    public void BeginRun() => IsRunning = true;

    public void EndRun() => IsRunning = false;
}

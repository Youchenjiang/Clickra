using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Clickra.Core;
using Clickra.Core.Application;
using Clickra.Core.Presentation.Convert;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI;

public static partial class DashboardWindow
{
    /// <summary>
    /// Native-only visual metadata layered over the shared presentation catalog.
    /// Command identity, grouping, compatibility, selection and validation stay in Core Presentation.
    /// </summary>
    private sealed record NativeConvertCommand(ConvertCommandPresentation Presentation, Color TagColor)
    {
        public string Command => Presentation.Command;
        public string TextKey => Presentation.LabelKey;
        public int Group => Presentation.Group;
        public bool RequiresOffice => Presentation.RequiresOffice;
        public string[] Extensions => ConvertWorkspaceController.GetAllowedExtensions(Command);
        public string DisplayName => GetText(TextKey);

        public string GetOpenFilter()
        {
            if (ConversionExecutionController.AcceptsAllFileExtensions(Command))
                return "All Files (*.*)\0*.*\0\0";
            string patterns = string.Join(";", Extensions.Select(extension => $"*{extension}"));
            return $"Supported Files ({patterns})\0{patterns}\0All Files (*.*)\0*.*\0\0";
        }
    }

    private static readonly NativeConvertCommand[] ConvertCommands = ConvertWorkspaceController.CommandCatalog
        .Select(item => new NativeConvertCommand(item, GetCommandTagColor(item.Command)))
        .ToArray();

    private static readonly int[] ConvertCommandGroupSizes = ConvertWorkspaceController.CommandCatalog
        .GroupBy(item => item.Group)
        .OrderBy(group => group.Key)
        .Select(group => group.Count())
        .ToArray();

    private static readonly Dictionary<string, NativeConvertCommand> ConvertCommandByKey = ConvertCommands
        .ToDictionary(command => command.Command, StringComparer.OrdinalIgnoreCase);

    internal static readonly Dictionary<string, Bitmap> ConvertCommandIcons = new(StringComparer.OrdinalIgnoreCase);

    internal static Bitmap? GetConvertCommandIcon(string command)
    {
        if (ConvertCommandIcons.TryGetValue(command, out var cached)) return cached;
        string fileName = $"menu-{command}.ico";
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            string devPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "resources", fileName);
            if (File.Exists(devPath)) path = devPath;
        }
        if (File.Exists(path))
        {
            try
            {
                using var ico = new Icon(path, 64, 64);
                cached = ico.ToBitmap();
                ConvertCommandIcons[command] = cached;
                return cached;
            }
            catch
            {
                // Ignore icon loading or decoding exceptions; fallback to null for default rendering.
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the brand color for a command, governed 100% by its input source file type.
    /// In Clickra's Design System, tag colors strictly reflect the input source domain
    /// (Word=Blue, Excel=Green, PPT=BrightOrange, Markdown=Purple, Image=PhotoTeal, PDF=SlateBlueGrey).
    /// </summary>
    private static Color GetCommandTagColor(string command)
    {
        string fileType = Clickra.Shared.ConvertCommandMetadata.GetFileTypeForCommand(command);
        return fileType switch
        {
            "word" => Color.FromArgb(0, 120, 212),       // Word Blue (#0078D4)
            "excel" => Color.FromArgb(16, 124, 65),      // Excel Green (#107C41)
            "ppt" => Color.FromArgb(216, 59, 1),         // PPT Bright Orange (#D83B01)
            "markdown" => Color.FromArgb(96, 72, 164),   // Markdown Purple (#6048A4)
            "image" => Color.FromArgb(0, 130, 140),      // Photo Teal (#00828C)
            "pdf" => Color.FromArgb(71, 85, 105),        // PDF Slate Blue-Grey (#475569)
            _ => Color.FromArgb(100, 100, 100)
        };
    }

    private static int GetCommandIndex(string command) =>
        Array.FindIndex(ConvertCommands, item => item.Command.Equals(command, StringComparison.OrdinalIgnoreCase));

    private static string DescribeConvertValidation(ConvertWorkspaceValidation validation) => validation.Kind switch
    {
        ConvertWorkspaceValidationKind.TooFewFiles =>
            string.Format(GetText("convert_err_min_files"), validation.MinimumFiles),
        ConvertWorkspaceValidationKind.InvalidExtension => GetText("convert_err_invalid_ext"),
        _ => GetText("convert_err_invalid_ext")
    };

    private static bool HasAvailableEngine(NativeConvertCommand command) =>
        !command.RequiresOffice || SettingsPresentationController.Shared.IsOfficeCommandReady(command.Command);

    private static void SelectConvertCommand(NativeConvertCommand command)
    {
        _convertWorkspace.TrySelectCommand(command.Command);
    }

    private static void RunConvertCommand(NativeConvertCommand command, IntPtr hwnd)
    {
        ConvertWorkspaceValidation validation = _convertWorkspace.ValidateSelection();
        if (!validation.IsValid)
        {
            ShowAlert(hwnd, DescribeConvertValidation(validation));
            return;
        }

        if (!HasAvailableEngine(command))
        {
            string errorKey = SettingsPresentationController.Shared.GetOfficeUnavailableErrorKey();
            ShowAlert(hwnd, Localization.T(errorKey, SettingsPresentationController.Shared.Language));
            return;
        }

        var filesCopy = new List<string>(_convertWorkspace.SelectedFiles);
        bool requiresPreflightPrompt = ConversionExecutionController.RequiresOptions(command.Command)
            || ConversionExecutionController.HasCapability(command.Command, ConversionCapability.PrintOptions)
            || ConversionExecutionController.HasCapability(command.Command, ConversionCapability.BatchRename);
        bool generatesHistory = ConversionExecutionController.GeneratesHistoryTask(command.Command);

        _convertWorkspace.BeginRun();
        StartConvertProgress(command, hwnd, filesCopy, requiresPreflightPrompt);

        if (requiresPreflightPrompt) return;

        _convertWorkspace.ClearFiles();
        if (generatesHistory)
        {
            _activeTab = 2;
            RefreshHistoryData();
        }
        InvalidateRect(hwnd, IntPtr.Zero, false);
    }

    private static void StartConvertProgress(
        NativeConvertCommand command,
        IntPtr hwnd,
        List<string> filesCopy,
        bool hasPreflightPrompt)
    {
        bool generatesHistory = ConversionExecutionController.GeneratesHistoryTask(command.Command);
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                ProgressWindow.Show(
                    command.Command,
                    filesCopy,
                    markdownDecision: hasPreflightPrompt
                        ? started =>
                        {
                            if (!started || !IsWindow(hwnd)) return;
                            PostDashboardAction(hwnd, () =>
                            {
                                _convertWorkspace.ClearFiles();
                                if (generatesHistory)
                                {
                                    _activeTab = 2;
                                    RefreshHistoryData();
                                }
                            });
                        }
                        : null);
            }
            catch (Exception ex)
            {
                ShowAlert(hwnd, $"Execution failed: {ex.Message}");
            }
            finally
            {
                if (IsWindow(hwnd))
                {
                    PostDashboardAction(hwnd, () =>
                    {
                        _convertWorkspace.EndRun();
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    });
                }
                else
                {
                    _convertWorkspace.EndRun();
                }
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
    }
}

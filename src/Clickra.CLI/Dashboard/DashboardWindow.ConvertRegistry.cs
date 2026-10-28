using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Clickra.Core;
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

    private static Color GetCommandTagColor(string command) => command switch
    {
        "word2pdf" => Color.FromArgb(0, 120, 212),
        "excel2pdf" => Color.FromArgb(16, 124, 65),
        "ppt2pdf" => Color.FromArgb(180, 50, 30),
        "md2pdf" => Color.FromArgb(96, 72, 164),
        "md2word" => Color.FromArgb(43, 87, 154),
        "merge-pdf" => Color.FromArgb(16, 124, 65),
        "compress-pdf" => Color.FromArgb(0, 120, 120),
        "translate-pdf" => Color.FromArgb(138, 43, 226),
        "decrypt-pdf" => Color.FromArgb(0, 150, 136),
        "split-pdf" => Color.FromArgb(0, 188, 212),
        "img2pdf" => Color.FromArgb(100, 60, 180),
        "img-merge" => Color.FromArgb(0, 130, 135),
        "img-stitch" => Color.FromArgb(216, 59, 1),
        "img-to-png" => Color.FromArgb(0, 120, 212),
        "img-to-jpg" => Color.FromArgb(255, 140, 0),
        "img-to-webp" => Color.FromArgb(0, 153, 188),
        "img-to-gif" => Color.FromArgb(163, 73, 164),
        "img-to-heic" => Color.FromArgb(16, 124, 65),
        _ => Color.FromArgb(100, 100, 100)
    };

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
        bool markdownCommand = command.Command.Equals("md2pdf", StringComparison.OrdinalIgnoreCase)
            || command.Command.Equals("md2word", StringComparison.OrdinalIgnoreCase);

        _convertWorkspace.BeginRun();
        StartConvertProgress(command, hwnd, filesCopy, markdownCommand);

        if (markdownCommand) return;

        _convertWorkspace.ClearFiles();
        _activeTab = 2;
        RefreshHistoryData();
        InvalidateRect(hwnd, IntPtr.Zero, false);
    }

    private static void StartConvertProgress(
        NativeConvertCommand command,
        IntPtr hwnd,
        List<string> filesCopy,
        bool markdownCommand)
    {
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                ProgressWindow.Show(
                    command.Command,
                    filesCopy,
                    markdownDecision: markdownCommand
                        ? started =>
                        {
                            if (!started || !IsWindow(hwnd)) return;
                            PostDashboardAction(hwnd, () =>
                            {
                                _convertWorkspace.ClearFiles();
                                _activeTab = 2;
                                RefreshHistoryData();
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

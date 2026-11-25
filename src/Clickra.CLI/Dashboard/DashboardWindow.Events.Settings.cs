using Clickra.Core.Modules.Sys.Settings.Application;
using System;
using Clickra.Core;
using Clickra.Core.Modules.Sys.Settings.Model;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI;

    public static partial class DashboardWindow
    {
        /// <summary>Handles settings-tab element clicks: toggles, output dirs and office engine selection.</summary>
        static void HandleSettingsClick(IntPtr hwnd, int element)
        {
            if (element >= 1000 && element < 2000)
            {
                HandleDynamicSettingClick(hwnd, element);
                return;
            }

            switch (element)
            {
                case 5: SettingsPresentationController.Shared.QuietMode = !SettingsPresentationController.Shared.QuietMode; InvalidateRect(hwnd, IntPtr.Zero, false); break;
                case 6: SettingsPresentationController.Shared.NotificationsEnabled = !SettingsPresentationController.Shared.NotificationsEnabled; InvalidateRect(hwnd, IntPtr.Zero, false); break;
                case 7: SettingsPresentationController.Shared.OutputDirectory = new(OutputDirectoryKind.Source); InvalidateRect(hwnd, IntPtr.Zero, false); break;
                case 8: SettingsPresentationController.Shared.OutputDirectory = new(OutputDirectoryKind.Desktop); InvalidateRect(hwnd, IntPtr.Zero, false); break;
                case 9: SettingsPresentationController.Shared.OutputDirectory = new(OutputDirectoryKind.Downloads); InvalidateRect(hwnd, IntPtr.Zero, false); break;
                case 20: BrowseOutputDir(hwnd); break;
                case 32: SettingsPresentationController.Shared.OfficeEngine = OfficeEnginePreference.Auto; InvalidateRect(hwnd, IntPtr.Zero, false); break;
                case 33: SettingsPresentationController.Shared.OfficeEngine = OfficeEnginePreference.Microsoft; InvalidateRect(hwnd, IntPtr.Zero, false); break;
                case 34: SettingsPresentationController.Shared.OfficeEngine = OfficeEnginePreference.LibreOffice;
                         SettingsPresentationController.Shared.LibreOfficePath = string.Empty;
                         InvalidateRect(hwnd, IntPtr.Zero, false); break;
                default: break; // Unhandled settings element — ignore.
            }
        }

        static void HandleDynamicSettingClick(IntPtr hwnd, int element)
        {
            int descriptorIndex = (element - 1000) / 10;
            int subId = (element - 1000) % 10;

            IReadOnlyList<SettingDescriptor> descriptors = SettingsPresentationController.Shared.Descriptors;
            if (descriptorIndex < 0 || descriptorIndex >= descriptors.Count) return;
            var descriptor = descriptors[descriptorIndex];

            switch (descriptor.EditorKind)
            {
                case SettingEditorKind.Toggle:
                    SettingsPresentationController.Shared.WriteBool(descriptor, !SettingsPresentationController.Shared.ReadBool(descriptor));
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                    break;

                case SettingEditorKind.Slider:
                {
                    var range = descriptor.GetEffectiveNumericRange();
                    if (!range.HasValue) break;
                    _dynamicSliderDescriptorIndex = descriptorIndex;
                    _isDraggingDynamicSlider = true;
                    SetCapture(hwnd);

                    if (GetCursorPos(out var pt))
                    {
                        ScreenToClient(hwnd, ref pt);
                        float mouseX = (pt.X / _dpiScale) + _contentScrollX;
                        float relX = mouseX - _dynamicSliderTrackX;
                        float fraction = Math.Max(0f, Math.Min(1f, relX / _dynamicSliderTrackW));
                        int span = range.Value.Max - range.Value.Min;
                        int newLevel = Math.Clamp(
                            (int)Math.Round(fraction * span, MidpointRounding.AwayFromZero) + range.Value.Min,
                            range.Value.Min,
                            range.Value.Max);
                        SettingsPresentationController.Shared.WriteInt(descriptor, newLevel);
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    break;
                }

                case SettingEditorKind.Number:
                {
                    var range = descriptor.GetEffectiveNumericRange() ?? new NumericSettingRange(0, 100, 0);
                    int current = SettingsPresentationController.Shared.ReadInt(descriptor);
                    int delta = subId switch
                    {
                        1 => -1,
                        2 => 1,
                        _ => 0,
                    };
                    int updated = (int)Math.Clamp((long)current + delta, range.Min, range.Max);
                    SettingsPresentationController.Shared.WriteInt(descriptor, updated);
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                    break;
                }

                case SettingEditorKind.Choice:
                {
                    if (descriptor.Options != null && subId >= 0 && subId < descriptor.Options.Count)
                    {
                        SettingsPresentationController.Shared.WriteChoice(descriptor, descriptor.Options[subId].Value);
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    break;
                }

                case SettingEditorKind.Text:
                {
                    HandleDynamicTextEdit(hwnd, descriptor);
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                    break;
                }

                default:
                    // Ignore unsupported future editor kinds until they have an interaction handler.
                    break;
            }
        }

        private static void BrowseOutputDir(IntPtr hwnd)
        {
            string title = GetText("setting_output_browse_title");
            string folder = BrowseForFolder(hwnd, title);
            if (!string.IsNullOrEmpty(folder))
            {
                SettingsPresentationController.Shared.OutputDirectory = new(OutputDirectoryKind.Custom, folder);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
        }

        /// <summary>Handles the PDF compression level slider (fonts are never removed, so there are no other toggles).</summary>
        static void HandleCompressSettingsClick(IntPtr hwnd, int element, int adjMouseX)
        {
            SettingDescriptor levelDescriptor = SettingsPresentationController.Shared.PdfCompressionLevelDescriptor;
            if (element == 83)
            {
                // PDF compress slider clicked — snap to nearest stop via equal-width segments + enable drag
                float relX = adjMouseX - _pdfSliderTrackX;
                float fraction = Math.Max(0f, Math.Min(1f, relX / _pdfSliderTrackW));
                NumericSettingRange range = GetRequiredNumericRange(levelDescriptor);
                int span = range.Max - range.Min;
                int newLevel = Math.Clamp(
                    (int)Math.Round(fraction * span, MidpointRounding.AwayFromZero) + range.Min,
                    range.Min,
                    range.Max);
                ApplyPdfCompressLevel(hwnd, newLevel);
                _isDraggingPdfSlider = true;
                SetCapture(hwnd);
            }
        }

        /// <summary>Applies a PDF compression level selection and refreshes the settings tab.</summary>
        static void ApplyPdfCompressLevel(IntPtr hwnd, int level)
        {
            SettingDescriptor descriptor = SettingsPresentationController.Shared.PdfCompressionLevelDescriptor;
            NumericSettingRange range = GetRequiredNumericRange(descriptor);
            int clamped = Math.Clamp(level, range.Min, range.Max);
            SettingsPresentationController.Shared.WriteInt(descriptor, clamped);
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }
    }

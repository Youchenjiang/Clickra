using System;
using Clickra.Core;
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
                case 5: SettingsController.QuietMode = !SettingsController.QuietMode; InvalidateRect(hwnd, IntPtr.Zero, false); break;
                case 6: SettingsController.NotificationsEnabled = !SettingsController.NotificationsEnabled; InvalidateRect(hwnd, IntPtr.Zero, false); break;
                case 7: SettingsController.OutputDirectory = new(OutputDirectoryKind.Source); InvalidateRect(hwnd, IntPtr.Zero, false); break;
                case 8: SettingsController.OutputDirectory = new(OutputDirectoryKind.Desktop); InvalidateRect(hwnd, IntPtr.Zero, false); break;
                case 9: SettingsController.OutputDirectory = new(OutputDirectoryKind.Downloads); InvalidateRect(hwnd, IntPtr.Zero, false); break;
                case 20: BrowseOutputDir(hwnd); break;
                case 32: SettingsController.OfficeEngine = OfficeEnginePreference.Auto; InvalidateRect(hwnd, IntPtr.Zero, false); break;
                case 33: SettingsController.OfficeEngine = OfficeEnginePreference.Microsoft; InvalidateRect(hwnd, IntPtr.Zero, false); break;
                case 34: SettingsController.OfficeEngine = OfficeEnginePreference.LibreOffice;
                         SettingsController.LibreOfficePath = string.Empty;
                         InvalidateRect(hwnd, IntPtr.Zero, false); break;
                case 90: AdjustParkedRetention(hwnd, -1); break;
                case 91: AdjustParkedRetention(hwnd, 1); break;
                case 92: SetParkedRetention(hwnd, 0); break;
                case 93: SetParkedRetention(hwnd, 3); break;
                case 94: SetParkedRetention(hwnd, GetDefaultParkedRetentionDays()); break;
                case 95: SetParkedRetention(hwnd, 14); break;
                case 96: SetParkedRetention(hwnd, 30); break;
                default: break; // Unhandled settings element — ignore.
            }
        }

        static void HandleDynamicSettingClick(IntPtr hwnd, int element)
        {
            int descriptorIndex = (element - 1000) / 10;
            int subId = (element - 1000) % 10;

            if (descriptorIndex < 0 || descriptorIndex >= SettingPageRegistry.AllDescriptors.Count) return;
            var descriptor = SettingPageRegistry.AllDescriptors[descriptorIndex];

            switch (descriptor.EditorKind)
            {
                case SettingEditorKind.Toggle:
                    SettingsController.WriteBool(descriptor, !SettingsController.ReadBool(descriptor));
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
                        SettingsController.WriteInt(descriptor, newLevel);
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    break;
                }

                case SettingEditorKind.Number:
                {
                    var range = descriptor.GetEffectiveNumericRange() ?? new NumericSettingRange(0, 100, 0);
                    int current = SettingsController.ReadInt(descriptor);
                    int delta = subId switch
                    {
                        1 => -1,
                        2 => 1,
                        _ => 0,
                    };
                    int updated = (int)Math.Clamp((long)current + delta, range.Min, range.Max);
                    SettingsController.WriteInt(descriptor, updated);
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                    break;
                }

                case SettingEditorKind.Choice:
                {
                    if (descriptor.Options != null && subId >= 0 && subId < descriptor.Options.Count)
                    {
                        SettingsController.WriteChoice(descriptor, descriptor.Options[subId].Value);
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    break;
                }

                default:
                    // Ignore unsupported future editor kinds until they have an interaction handler.
                    break;
            }
        }

        private static void AdjustParkedRetention(IntPtr hwnd, int delta)
        {
            int current = ParkedTaskController.GetRetentionDays();
            SetParkedRetention(hwnd, current + delta);
        }

        private static int GetDefaultParkedRetentionDays()
            => ClickraSettings.GetDefaultInt(ClickraSettings.ParkedTaskRetention);

        private static void SetParkedRetention(IntPtr hwnd, int days)
        {
            // Same range as the Fluent settings page, from one place: a second copy of the bound
            // would let the two settings pages drift apart.
            int clamped = Math.Clamp(days, ClickraSettings.MinParkedRetentionDays, ClickraSettings.MaxParkedRetentionDays);
            SettingsController.ParkedTaskRetentionDays = clamped;
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        private static void BrowseOutputDir(IntPtr hwnd)
        {
            string title = GetText("setting_output_browse_title");
            string folder = BrowseForFolder(hwnd, title);
            if (!string.IsNullOrEmpty(folder))
            {
                SettingsController.OutputDirectory = new(OutputDirectoryKind.Custom, folder);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
        }

        /// <summary>Handles PDF compression settings: slider drag start and option toggles.</summary>
        static void HandleCompressSettingsClick(IntPtr hwnd, int element, int adjMouseX)
        {
            SettingDescriptor levelDescriptor = GetRequiredSettingDescriptor(ClickraSettings.PdfCompressImageLevel);
            SettingDescriptor stripFontsDescriptor = GetRequiredSettingDescriptor(ClickraSettings.PdfCompressStripFonts);
            SettingDescriptor minifyContentDescriptor = GetRequiredSettingDescriptor(ClickraSettings.PdfCompressMinifyContent);
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
            else if (element == 81)
            {
                bool current = SettingsController.ReadBool(stripFontsDescriptor);
                SettingsController.WriteBool(stripFontsDescriptor, !current);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 82)
            {
                bool current = SettingsController.ReadBool(minifyContentDescriptor);
                SettingsController.WriteBool(minifyContentDescriptor, !current);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
        }

        /// <summary>Applies a PDF compression level selection and refreshes the settings tab.</summary>
        static void ApplyPdfCompressLevel(IntPtr hwnd, int level)
        {
            SettingDescriptor descriptor = GetRequiredSettingDescriptor(ClickraSettings.PdfCompressImageLevel);
            NumericSettingRange range = GetRequiredNumericRange(descriptor);
            int clamped = Math.Clamp(level, range.Min, range.Max);
            SettingsController.WriteInt(descriptor, clamped);
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }
    }

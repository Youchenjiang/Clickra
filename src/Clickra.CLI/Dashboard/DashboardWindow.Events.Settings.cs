using System;
using Clickra.Core;
using static Clickra.UI.Native.Win32;

namespace Clickra.UI
{
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
                case 5: ToggleBoolSetting(hwnd, ClickraSettings.QuietMode); break;
                case 6: ToggleBoolSetting(hwnd, ClickraSettings.Notification); break;
                case 7: ApplySetting(hwnd, ClickraSettings.OutputDir, ClickraSettings.DefaultOutputDirSource); break;
                case 8: ApplySetting(hwnd, ClickraSettings.OutputDir, ClickraSettings.OutputDirDesktop); break;
                case 9: ApplySetting(hwnd, ClickraSettings.OutputDir, ClickraSettings.OutputDirDownloads); break;
                case 20: BrowseOutputDir(hwnd); break;
                case 32: ApplySetting(hwnd, ClickraSettings.OfficeEngine, ClickraSettings.DefaultOfficeEngineAuto); break;
                case 33: ApplySetting(hwnd, ClickraSettings.OfficeEngine, ClickraSettings.OfficeEngineMicrosoft); break;
                case 34: ClickraStorage.SaveSetting(ClickraSettings.OfficeEngine, ClickraSettings.OfficeEngineLibreOffice);
                         ApplySetting(hwnd, ClickraSettings.LibreOfficePath, ClickraSettings.DefaultEmpty); break;
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
                    ToggleBoolSetting(hwnd, descriptor.Key);
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
                        ClickraStorage.SaveSetting(descriptor.Key, newLevel.ToString());
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    break;
                }

                case SettingEditorKind.Number:
                {
                    var range = descriptor.GetEffectiveNumericRange() ?? new NumericSettingRange(0, 100, 0);
                    int current = ClickraStorage.GetSettingInt(descriptor.Key);
                    int delta = subId switch
                    {
                        1 => -1,
                        2 => 1,
                        _ => 0,
                    };
                    int updated = (int)Math.Clamp((long)current + delta, range.Min, range.Max);
                    ClickraStorage.SaveSetting(descriptor.Key, updated.ToString());
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                    break;
                }

                case SettingEditorKind.Choice:
                {
                    if (descriptor.Options != null && subId >= 0 && subId < descriptor.Options.Count)
                    {
                        ApplySetting(hwnd, descriptor.Key, descriptor.Options[subId].Value);
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
            int current = ClickraStorage.GetParkedRetentionDays();
            SetParkedRetention(hwnd, current + delta);
        }

        private static int GetDefaultParkedRetentionDays()
            => ClickraSettings.GetDefaultInt(ClickraSettings.ParkedTaskRetention);

        private static void SetParkedRetention(IntPtr hwnd, int days)
        {
            // Same range as the Fluent settings page, from one place: a second copy of the bound
            // would let the two settings pages drift apart.
            int clamped = Math.Clamp(days, ClickraSettings.MinParkedRetentionDays, ClickraSettings.MaxParkedRetentionDays);
            ClickraStorage.SaveSetting(ClickraSettings.ParkedTaskRetention, clamped.ToString());
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        private static void ToggleBoolSetting(IntPtr hwnd, string key)
        {
            bool current = ClickraStorage.GetSetting(key).Equals(ClickraSettings.ValueTrue, StringComparison.OrdinalIgnoreCase);
            ClickraStorage.SaveSetting(key, current ? ClickraSettings.ValueFalse : ClickraSettings.ValueTrue);
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        private static void ApplySetting(IntPtr hwnd, string key, string value)
        {
            ClickraStorage.SaveSetting(key, value);
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        private static void BrowseOutputDir(IntPtr hwnd)
        {
            string title = GetText("setting_output_browse_title");
            string folder = BrowseForFolder(hwnd, title);
            if (!string.IsNullOrEmpty(folder))
                ApplySetting(hwnd, ClickraSettings.OutputDir, folder);
        }

        /// <summary>Handles PDF compression settings: slider drag start and option toggles.</summary>
        static void HandleCompressSettingsClick(IntPtr hwnd, int element, int adjMouseX)
        {
            if (element == 83)
            {
                // PDF compress slider clicked — snap to nearest stop via equal-width segments + enable drag
                float relX = adjMouseX - _pdfSliderTrackX;
                float fraction = Math.Max(0f, Math.Min(1f, relX / _pdfSliderTrackW));
                int span = ClickraSettings.MaxPdfCompressLevel - ClickraSettings.MinPdfCompressLevel;
                int newLevel = ClickraSettings.ClampNumericSetting(
                    ClickraSettings.PdfCompressImageLevel,
                    (int)Math.Round(fraction * span, MidpointRounding.AwayFromZero) + ClickraSettings.MinPdfCompressLevel);
                ApplyPdfCompressLevel(hwnd, newLevel);
                _isDraggingPdfSlider = true;
                SetCapture(hwnd);
            }
            else if (element == 81)
            {
                bool current = ClickraStorage.GetSettingBool(ClickraSettings.PdfCompressStripFonts);
                ClickraStorage.SaveSetting(ClickraSettings.PdfCompressStripFonts, current ? ClickraSettings.ValueFalse : ClickraSettings.ValueTrue);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            else if (element == 82)
            {
                bool current = ClickraStorage.GetSettingBool(ClickraSettings.PdfCompressMinifyContent);
                ClickraStorage.SaveSetting(ClickraSettings.PdfCompressMinifyContent, current ? ClickraSettings.ValueFalse : ClickraSettings.ValueTrue);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
        }

        /// <summary>Applies a PDF compression level selection and refreshes the settings tab.</summary>
        static void ApplyPdfCompressLevel(IntPtr hwnd, int level)
        {
            int clamped = ClickraSettings.ClampNumericSetting(ClickraSettings.PdfCompressImageLevel, level);
            ClickraStorage.SaveSetting(ClickraSettings.PdfCompressImageLevel, clamped.ToString());
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }
    }
}

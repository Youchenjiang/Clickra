using System;
using System.Drawing;
using Clickra.Core;
using Clickra.Core.Layout;

namespace Clickra.UI
{
    public static partial class DashboardWindow
    {

        static void DrawDynamicSettingDescriptor(
            Graphics g,
            SettingDescriptor descriptor,
            int descriptorIndex,
            float logW,
            float contentX,
            ref float y,
            float? sliderWidth = null)
        {
            int baseElemId = 1000 + descriptorIndex * 10;
            switch (descriptor.EditorKind)
            {
                case SettingEditorKind.Toggle:
                    DrawDynamicToggleSetting(g, descriptor, logW, contentX, baseElemId, ref y);
                    break;
                case SettingEditorKind.Slider:
                    DrawDynamicSliderSetting(g, descriptor, contentX, baseElemId, sliderWidth, ref y);
                    break;
                case SettingEditorKind.Number:
                    DrawDynamicNumberSetting(g, descriptor, contentX, SettingsLayout.InlineGap, baseElemId, ref y);
                    break;
                case SettingEditorKind.Choice:
                    DrawDynamicChoiceSetting(g, descriptor, contentX, SettingsLayout.InlineGap, baseElemId, ref y);
                    break;
                default:
                    // Ignore unsupported future editor kinds until a renderer is defined.
                    break;
            }
        }


        static void DrawDynamicSettingHeader(Graphics g, SettingDescriptor descriptor, float contentX, float y)
        {
            float s = _dpiScale;
            if (_tabFont != null)
                g.DrawString(GetText(descriptor.TitleKey), _tabFont, Brushes.White, contentX * s, y * s);
            if (string.IsNullOrEmpty(descriptor.DescriptionKey) || _subFont == null) return;

            using var subBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
            g.DrawString(GetText(descriptor.DescriptionKey), _subFont, subBrush, contentX * s, (y + SettingsLayout.HeaderDescriptionOffset) * s);
        }


        static void DrawDynamicToggleSetting(
            Graphics g, SettingDescriptor descriptor, float logW, float contentX, int baseElemId, ref float y)
        {
            DrawDynamicSettingHeader(g, descriptor, contentX, y);
            LayoutRect toggle = SettingsLayout.ToggleRect((int)logW, (int)y);
            bool state = ClickraStorage.GetSettingBool(descriptor.Key);
            DrawToggleSwitch(g, state, _hoveredElement == baseElemId, toggle.X, toggle.Y, toggle.Width, toggle.Height);
            _settingsHitRects[baseElemId] = new RectangleF(toggle.X, toggle.Y, toggle.Width, toggle.Height);
            y += SettingsLayout.ToggleSectionHeight;
        }


        static void DrawDynamicSliderSetting(
            Graphics g, SettingDescriptor descriptor, float contentX, int baseElemId, float? sliderWidth, ref float y)
        {
            DrawDynamicSettingHeader(g, descriptor, contentX, y);
            y += SettingsLayout.SliderHeaderGap;

            float sliderW = sliderWidth ?? SettingsLayout.SliderWidth;
            _dynamicSliderTrackX = contentX;
            _dynamicSliderTrackW = sliderW;
            var range = descriptor.GetEffectiveNumericRange() ?? new NumericSettingRange(0, 1, 0);
            int currentLevel = Math.Clamp(ClickraStorage.GetSettingInt(descriptor.Key), range.Min, range.Max);
            DrawDynamicSlider(g, contentX, y, sliderW, descriptor, currentLevel, range);
            LayoutRect sliderHit = SettingsLayout.SliderHitRect((int)contentX, (int)y, (int)sliderW);
            _settingsHitRects[baseElemId] = new RectangleF(sliderHit.X, sliderHit.Y, sliderHit.Width, sliderHit.Height);
            y += SettingsLayout.SliderSectionHeight;
        }


        static void DrawDynamicNumberSetting(
            Graphics g, SettingDescriptor descriptor, float contentX, float margin, int baseElemId, ref float y)
        {
            DrawDynamicSettingHeader(g, descriptor, contentX, y);
            y += SettingsLayout.DynamicNumberHeaderGap;

            var range = descriptor.GetEffectiveNumericRange() ?? new NumericSettingRange(0, 100, 0);
            int value = Math.Clamp(ClickraStorage.GetSettingInt(descriptor.Key), range.Min, range.Max);
            if (_subFont != null)
            {
                using var valBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
                g.DrawString(value.ToString(), _subFont, valBrush, contentX * _dpiScale, y * _dpiScale);
            }
            y += SettingsLayout.DynamicNumberValueHeight;

            float buttonWidth = SettingsLayout.DynamicNumberButtonWidth;
            float buttonY = y;
            DrawOutputDirButton(g, "-", false, baseElemId + 1, (int)contentX, (int)buttonY, (int)buttonWidth);
            LayoutRect minusRect = SettingsLayout.ButtonRect((int)contentX, (int)buttonY, (int)buttonWidth);
            _settingsHitRects[baseElemId + 1] = new RectangleF(minusRect.X, minusRect.Y, minusRect.Width, minusRect.Height);
            float plusX = contentX + buttonWidth + margin;
            DrawOutputDirButton(g, "+", false, baseElemId + 2, (int)plusX, (int)buttonY, (int)buttonWidth);
            LayoutRect plusRect = SettingsLayout.ButtonRect((int)plusX, (int)buttonY, (int)buttonWidth);
            _settingsHitRects[baseElemId + 2] = new RectangleF(plusRect.X, plusRect.Y, plusRect.Width, plusRect.Height);
            y += SettingsLayout.DynamicNumberSectionTail;
        }


        static void DrawDynamicChoiceSetting(
            Graphics g, SettingDescriptor descriptor, float contentX, float margin, int baseElemId, ref float y)
        {
            DrawDynamicSettingHeader(g, descriptor, contentX, y);
            y += SettingsLayout.ControlTopOffset;

            string currentValue = ClickraStorage.GetSetting(descriptor.Key);
            float buttonX = contentX;
            var options = descriptor.Options ?? Array.Empty<SettingOption>();
            for (int optionIndex = 0; optionIndex < options.Count; optionIndex++)
            {
                var option = options[optionIndex];
                bool selected = string.Equals(currentValue, option.Value, StringComparison.OrdinalIgnoreCase);
                string label = GetText(option.LabelKey);
                if (string.Equals(label, option.LabelKey, StringComparison.Ordinal) && !string.IsNullOrEmpty(option.FallbackText))
                    label = option.FallbackText;

                var measureFont = _subFont ?? SystemFonts.DefaultFont;
                float buttonWidth = Math.Max(60f, g.MeasureString(label, measureFont).Width / _dpiScale + 20f);
                int elementId = baseElemId + optionIndex;
                DrawOutputDirButton(g, label, selected, elementId, (int)buttonX, (int)y, (int)buttonWidth);
                LayoutRect choiceRect = SettingsLayout.ButtonRect((int)buttonX, (int)y, (int)buttonWidth);
                _settingsHitRects[elementId] = new RectangleF(choiceRect.X, choiceRect.Y, choiceRect.Width, choiceRect.Height);
                buttonX += buttonWidth + margin;
            }
            y += SettingsLayout.DynamicChoiceSectionHeight;
        }


        static void DrawDynamicSlider(Graphics g, float x, float y, float w, SettingDescriptor descriptor, int level, NumericSettingRange range)
        {
            float s = _dpiScale;
            int stops = Math.Max(2, range.Max - range.Min + 1);
            float trackY = y + 18f;
            float trackH = 5f;
            Color accent = UIHelper.GetSystemColorizationColor();

            // Track background
            using var bgPath = UIHelper.GetRoundedRectPath(
                new RectangleF(x * s, trackY * s, w * s, trackH * s), (trackH / 2f) * s);
            using var bgBrush = new SolidBrush(Color.FromArgb(55, 55, 55));
            g.FillPath(bgBrush, bgPath);

            // Filled portion
            float thumbX = x + (float)(level - range.Min) / (stops - 1) * w;
            float fillW = thumbX - x;
            if (fillW > 0.5f)
            {
                using var fillPath = UIHelper.GetRoundedRectPath(
                    new RectangleF(x * s, trackY * s, fillW * s, trackH * s), (trackH / 2f) * s);
                using var fillBrush = new SolidBrush(accent);
                g.FillPath(fillBrush, fillPath);
            }

            // Stop dots + labels
            var labels = descriptor.SliderLabels;
            for (int i = 0; i < stops; i++)
            {
                float sx = x + (float)i / (stops - 1) * w;
                bool active = (i + range.Min == level);

                float dotR = DrawSliderStopMarker(
                    g, sx, trackY, trackH, active, (i + range.Min) <= level, accent);

                if (_subFont != null && labels != null && i < labels.Count)
                {
                    string label = GetText(labels[i]);
                    using var lBrush = new SolidBrush(active ? Color.White : Color.FromArgb(95, 95, 95));
                    var lSize = g.MeasureString(label, _subFont);
                    g.DrawString(label, _subFont, lBrush,
                        (sx - lSize.Width / s / 2f) * s,
                        (trackY + trackH / 2f + dotR + 5f) * s);
                }
            }
        }
    }
}

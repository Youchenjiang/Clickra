using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace Clickra.Desktop.Controls;

/// <summary>
/// Short entrance motion (spec section 7, "動態效果"): only opacity and transforms are animated, never size,
/// so nothing is re-laid out per frame. Everything is skipped when the system asks for reduced motion.
/// </summary>
internal static class Motion
{
    /// <summary>False when the OS "animation effects" setting is off (or CLICKRA_REDUCE_MOTION=1).</summary>
    public static bool Enabled { get; } = DetectEnabled();

    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(180);

    /// <summary>Fade in while sliding <paramref name="offsetY"/> px into place.</summary>
    /// <remarks>
    /// Built on transitions toward the control's own final values (opacity 1, no offset), not on a keyframe
    /// animation: when a keyframe animation ends, its value is dropped and the local value shows for a frame,
    /// which made pages blink at the end of the effect.
    /// </remarks>
    public static void Enter(Visual target, double offsetY = 8, TimeSpan? duration = null)
    {
        if (!Enabled) return;
        TimeSpan time = duration ?? Normal;

        // Start state, applied without transitions so it takes effect at once.
        target.Transitions = null;
        target.Opacity = 0;
        target.RenderTransform = TransformOperations.Parse(FormattableString.Invariant($"translateY({offsetY}px)"));

        void Play(TimeSpan _)
        {
            target.Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = time, Easing = new CubicEaseOut() },
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = time, Easing = new CubicEaseOut() },
            };
            target.Opacity = 1;
            target.RenderTransform = TransformOperations.Parse("translateY(0px)");
        }

        // Next frame: the start state has been applied, so the transitions run from it.
        if (TopLevel.GetTopLevel(target) is { } top) top.RequestAnimationFrame(Play);
        else Play(default);
    }

    /// <summary>Transitions for a control whose RenderTransform is swapped between TransformOperations values.</summary>
    public static Transitions? TransformTransitions() => Enabled
        ? new Transitions { new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Fast, Easing = new CubicEaseOut() } }
        : null;

    private static bool DetectEnabled()
    {
        if (Environment.GetEnvironmentVariable("CLICKRA_REDUCE_MOTION") == "1") return false;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                // SPI_GETCLIENTAREAANIMATION: Settings > Accessibility > Visual effects > Animation effects.
                if (SystemParametersInfoW(0x1042, 0, out int on, 0) != 0) return on != 0;
            }
            catch (Exception)
            {
                // Fall through: keep motion on if the setting can't be read.
            }
        }
        // macOS (accessibilityDisplayShouldReduceMotion) and Linux (gtk-enable-animations) are read in a later branch.
        return true;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    private static extern int SystemParametersInfoW(uint action, uint param, out int value, uint winIni);
}

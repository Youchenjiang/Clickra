using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace Clickra.Desktop.Controls;

/// <summary>
/// Short motion (spec section 7, "動態效果"): opacity and transforms only, never size, so nothing is re-laid
/// out per frame. The one exception is the drop zone collapsing into the file row (spec 4.1), which uses
/// <see cref="Tween"/>. Everything is skipped when the system asks for reduced motion.
/// </summary>
internal static class Motion
{
    /// <summary>False when the OS "animation effects" setting is off (or CLICKRA_REDUCE_MOTION=1).</summary>
    public static bool Enabled => DetectEnabled();

    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(180);

    /// <summary>Fade in while sliding from (<paramref name="offsetX"/>, <paramref name="offsetY"/>) px into place.</summary>
    /// <remarks>
    /// Built on transitions toward the control's own final values (opacity 1, no offset), not on a keyframe
    /// animation: when a keyframe animation ends, its value is dropped and the local value shows for a frame,
    /// which made pages blink at the end of the effect.
    /// </remarks>
    public static void Enter(Visual target, double offsetX, double offsetY, TimeSpan? duration = null, TimeSpan? delay = null)
    {
        if (!Enabled) return;
        TimeSpan time = duration ?? Normal;

        // Start state, applied without transitions so it takes effect at once.
        target.Transitions = null;
        target.Opacity = 0;
        target.RenderTransform = TransformOperations.Parse(string.Create(CultureInfo.InvariantCulture, $"translate({offsetX}px, {offsetY}px)"));

        void Play(TimeSpan _)
        {
            target.Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = time, Easing = new CubicEaseOut() },
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = time, Easing = new CubicEaseOut() },
            };
            target.Opacity = 1;
            target.RenderTransform = TransformOperations.Parse("translate(0px, 0px)");
        }

        // Next frame (after the optional delay): the start state has been applied, so the transitions run from it.
        void Schedule()
        {
            if (TopLevel.GetTopLevel(target) is { } top) top.RequestAnimationFrame(Play);
            else Play(default);
        }
        if (delay is { } wait && wait > TimeSpan.Zero) DispatcherTimer.RunOnce(Schedule, wait);
        else Schedule();
    }

    /// <summary>
    /// Runs <paramref name="apply"/> once per frame with the eased progress 0 → 1, then calls
    /// <paramref name="done"/> in the same frame as the final value, so nothing can show in between.
    /// Used for the few effects that are not a plain fade/slide (spec 4.1: the drop zone collapsing).
    /// </summary>
    public static void Tween(Visual host, TimeSpan duration, IEasing easing, Action<double, double> apply, Action? done = null)
    {
        if (TopLevel.GetTopLevel(host) is not { } top)
        {
            apply(1, 1);
            done?.Invoke();
            return;
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Frame(TimeSpan _)
        {
            double linear = Math.Min(1, clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds);
            apply(easing.Ease(linear), linear);
            if (linear < 1) top.RequestAnimationFrame(Frame);
            else done?.Invoke();
        }
        apply(0, 0);
        top.RequestAnimationFrame(Frame);
    }

    /// <summary>
    /// Keeps a control that its binding just hid on screen while it fades out, then lets it hide.
    /// </summary>
    public static void FadeOut(Control target, TimeSpan duration)
    {
        if (!Enabled) return;
        IDisposable? keepVisible = target.SetValue(Visual.IsVisibleProperty, true, BindingPriority.Animation);
        target.Transitions = null; // set per frame below; a leftover Enter() transition would lag behind
        target.IsHitTestVisible = false;
        Tween(target, duration, new LinearEasing(), (_, p) => target.Opacity = 1 - p, () =>
        {
            keepVisible?.Dispose();
            target.ClearValue(Visual.OpacityProperty);
            target.ClearValue(InputElement.IsHitTestVisibleProperty);
        });
    }

    /// <summary>Grows a control from <paramref name="fromScale"/> to full size with a slight overshoot.</summary>
    public static void Pop(Visual target, double fromScale = 0.8, TimeSpan? duration = null)
    {
        if (!Enabled) return;
        Tween(target, duration ?? TimeSpan.FromMilliseconds(360), new BackEaseOut(), (e, _) =>
        {
            double scale = fromScale + (1 - fromScale) * e;
            target.RenderTransform = new ScaleTransform(scale, scale);
        }, () => target.ClearValue(Visual.RenderTransformProperty));
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
            IntPtr ptr = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                // SPI_GETCLIENTAREAANIMATION: Settings > Accessibility > Visual effects > Animation effects.
                if (SystemParametersInfoW(0x1042, 0, ptr, 0) != 0)
                {
                    return Marshal.ReadInt32(ptr) != 0;
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Fall through: keep motion on if the setting can't be read.
            }
            catch (DllNotFoundException)
            {
                // Fall through: keep motion on if user32.dll isn't available.
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        // macOS (accessibilityDisplayShouldReduceMotion) and Linux (gtk-enable-animations) are read in a later branch.
        return true;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    private static extern int SystemParametersInfoW(uint action, uint param, IntPtr value, uint winIni);
}

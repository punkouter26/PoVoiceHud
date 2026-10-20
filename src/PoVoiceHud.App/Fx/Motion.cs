using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using PoVoiceHud.Core.Config;

namespace PoVoiceHud.App.Fx;

/// <summary>
/// G3 (headline glitch), G5 (springs) and G8 (answer materialising), run by the compositor on its own thread:
/// none of it costs the UI thread a frame. Everything checks <see cref="Level"/> and does nothing at Off.
/// </summary>
internal static class Motion
{
    /// <summary>The effective level: the setting, lowered by Windows' animation switch and battery saver (see <see cref="HudEffects"/>).</summary>
    public static EffectsLevel Level { get; set; } = EffectsLevel.Full;

    private static Compositor Compositor => CompositionTarget.GetCompositorForCurrentThread();

    /// <summary>G5: the focus card springs up into place and fades in; at Full it overshoots a little.</summary>
    public static void Enter(UIElement element)
    {
        if (Level == EffectsLevel.Off) return;
        var compositor = Compositor;
        var rise = compositor.CreateSpringVector3Animation();
        rise.Target = "Translation";
        rise.InitialValue = new Vector3(0, 18, 0);
        rise.FinalValue = Vector3.Zero;
        rise.DampingRatio = Level == EffectsLevel.Full ? 0.48f : 0.85f;
        rise.Period = TimeSpan.FromMilliseconds(48);
        element.StartAnimation(rise);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(220);
        element.StartAnimation(fade);
    }

    /// <summary>G3: a contradicted claim makes the headline jolt sideways for a quarter of a second.</summary>
    public static void Glitch(UIElement element)
    {
        if (Level != EffectsLevel.Full) return;
        var jolt = Compositor.CreateVector3KeyFrameAnimation();
        jolt.Target = "Translation";
        float[] xs = [-4, 5, -3, 3, -1.5f, 0];
        for (var i = 0; i < xs.Length; i++)
            jolt.InsertKeyFrame((i + 1f) / xs.Length, new Vector3(xs[i], 0, 0), Compositor.CreateStepEasingFunction());
        jolt.Duration = TimeSpan.FromMilliseconds(240);
        element.StartAnimation(jolt);
    }

    /// <summary>
    /// G5: lets the history list animate its own layout. When a card is pushed to the top, the others slide down
    /// on a spring instead of jumping, and the new top line drops in.
    /// </summary>
    public static void Slide(UIElement element, bool isNewTop)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (Level == EffectsLevel.Off)
        {
            visual.ImplicitAnimations = null;
            return;
        }
        var compositor = visual.Compositor;
        if (visual.ImplicitAnimations is null)
        {
            // "this.FinalValue" is the new layout position; the move runs there from wherever the visual is now,
            // and at Full the curve's second control point above 1 makes it overshoot and settle like a spring.
            var follow = compositor.CreateVector3KeyFrameAnimation();
            follow.Target = "Offset";
            follow.InsertExpressionKeyFrame(1, "this.FinalValue", Level == EffectsLevel.Full
                ? compositor.CreateCubicBezierEasingFunction(new Vector2(0.3f, 0), new Vector2(0.25f, 1.35f))
                : compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0, 1)));
            follow.Duration = TimeSpan.FromMilliseconds(Level == EffectsLevel.Full ? 380 : 240);
            var implicitAnimations = compositor.CreateImplicitAnimationCollection();
            implicitAnimations["Offset"] = follow;
            visual.ImplicitAnimations = implicitAnimations;
        }
        if (isNewTop) Enter(element);
    }

    /// <summary>G5: a card thrown off sideways after a swipe, then <paramref name="done"/>.</summary>
    public static void FlyOut(UIElement element, float direction, double speed, Action done)
    {
        var compositor = Compositor;
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(500 / Math.Max(speed, 0.5), 120, 260));
        var fly = compositor.CreateVector3KeyFrameAnimation();
        fly.Target = "Translation";
        fly.InsertKeyFrame(1, new Vector3(direction * 640, 0, 0), compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.6f), new Vector2(0.4f, 1)));
        fly.Duration = duration;
        element.StartAnimation(fly);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.InsertKeyFrame(1, 0);
        fade.Duration = duration;
        element.StartAnimation(fade);
        batch.End();
        batch.Completed += (_, _) =>
        {
            element.StopAnimation(fly);
            element.StopAnimation(fade);
            element.Translation = Vector3.Zero;
            element.Opacity = 1;
            done();
        };
    }

    /// <summary>G5: a swipe that was not far or fast enough springs back.</summary>
    public static void SpringBack(UIElement element)
    {
        var compositor = Compositor;
        var back = compositor.CreateSpringVector3Animation();
        back.Target = "Translation";
        back.FinalValue = Vector3.Zero;
        back.DampingRatio = 0.55f;
        back.Period = TimeSpan.FromMilliseconds(45);
        element.StartAnimation(back);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(150);
        element.StartAnimation(fade);
    }

    /// <summary>
    /// G8: the answer comes into focus from left to right. A sprite on top of the text blurs whatever is behind it
    /// (the text) and is masked by a gradient whose edge sweeps across, leaving sharp text behind it. A confident
    /// answer then gets one glint of light.
    /// </summary>
    public static void Materialize(FrameworkElement text, bool glint)
    {
        if (Level == EffectsLevel.Off || text.ActualWidth < 1) return;
        var compositor = Compositor;
        var size = new Vector2((float)text.ActualWidth, (float)text.ActualHeight);
        var duration = TimeSpan.FromMilliseconds(Level == EffectsLevel.Full ? 560 : 360);
        var container = compositor.CreateContainerVisual();
        container.Size = size;
        try
        {
            var blur = new GaussianBlurEffect
            {
                Name = "Blur",
                BlurAmount = 7,
                BorderMode = EffectBorderMode.Hard,
                Source = new CompositionEffectSourceParameter("backdrop"),
            };
            var effect = compositor.CreateEffectFactory(blur, ["Blur.BlurAmount"]).CreateBrush();
            effect.SetSourceParameter("backdrop", compositor.CreateBackdropBrush());

            // mask alpha 1 = still blurred; the clear edge runs from left (−0.3) to past the right (1)
            var mask = compositor.CreateLinearGradientBrush();
            var clear = compositor.CreateColorGradientStop(0, Colors.Transparent);
            var blurred = compositor.CreateColorGradientStop(0.3f, Colors.White);
            mask.ColorStops.Add(clear);
            mask.ColorStops.Add(blurred);
            mask.StartPoint = Vector2.Zero;
            mask.EndPoint = new Vector2(1, 0);
            var masked = compositor.CreateMaskBrush();
            masked.Source = effect;
            masked.Mask = mask;
            var sprite = compositor.CreateSpriteVisual();
            sprite.Size = size;
            sprite.Brush = masked;
            container.Children.InsertAtTop(sprite);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            clear.StartAnimation("Offset", Sweep(compositor, -0.3f, 1, duration));
            blurred.StartAnimation("Offset", Sweep(compositor, 0, 1.3f, duration));
            var sharpen = compositor.CreateScalarKeyFrameAnimation();
            sharpen.InsertKeyFrame(1, 1.5f);
            sharpen.Duration = duration;
            effect.Properties.StartAnimation("Blur.BlurAmount", sharpen);
            if (glint && Level == EffectsLevel.Full) container.Children.InsertAtTop(Glint(compositor, size, duration));
            batch.End();
            batch.Completed += (_, _) => ElementCompositionPreview.SetElementChildVisual(text, null);
            ElementCompositionPreview.SetElementChildVisual(text, container);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // no backdrop sampling on this system: the card still springs in, it just does not sharpen
            ElementCompositionPreview.SetElementChildVisual(text, null);
        }
    }

    private static ScalarKeyFrameAnimation Sweep(Compositor compositor, float from, float to, TimeSpan duration)
    {
        var sweep = compositor.CreateScalarKeyFrameAnimation();
        sweep.InsertKeyFrame(0, from);
        sweep.InsertKeyFrame(1, to, compositor.CreateCubicBezierEasingFunction(new Vector2(0.3f, 0), new Vector2(0.2f, 1)));
        sweep.Duration = duration;
        return sweep;
    }

    /// <summary>A narrow slanted band of light that crosses the text once, after the blur has cleared.</summary>
    private static SpriteVisual Glint(Compositor compositor, Vector2 size, TimeSpan delay)
    {
        var band = compositor.CreateLinearGradientBrush();
        band.StartPoint = Vector2.Zero;
        band.EndPoint = new Vector2(1, 0.25f);
        var edgeIn = compositor.CreateColorGradientStop(0, Colors.Transparent);
        var peak = compositor.CreateColorGradientStop(0.08f, ColorHelper.FromArgb(110, 255, 255, 255));
        var edgeOut = compositor.CreateColorGradientStop(0.16f, Colors.Transparent);
        band.ColorStops.Add(edgeIn);
        band.ColorStops.Add(peak);
        band.ColorStops.Add(edgeOut);
        var duration = TimeSpan.FromMilliseconds(520);
        foreach (var (stop, at) in new[] { (edgeIn, 0f), (peak, 0.08f), (edgeOut, 0.16f) })
        {
            var move = compositor.CreateScalarKeyFrameAnimation();
            move.InsertKeyFrame(0, at - 0.2f);
            move.InsertKeyFrame(1, at + 1);
            move.Duration = duration;
            move.DelayTime = delay;
            move.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            stop.StartAnimation("Offset", move);
        }
        var sprite = compositor.CreateSpriteVisual();
        sprite.Size = size;
        sprite.Brush = band;
        return sprite;
    }
}

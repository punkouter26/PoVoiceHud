using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using ComputeSharp;
using ComputeSharp.D2D1.WinUI;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Fx;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.ViewModels;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.System.Power;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace PoVoiceHud.App.Fx;

/// <summary>The parts of the window the effects draw on, measure or move.</summary>
internal sealed record HudParts(
    FrameworkElement Root, CanvasSwapChainPanel Back, CanvasControl Front, FrameworkElement RibbonSlot, UIElement LevelBars,
    UIElement Busy, FrameworkElement Headline, FrameworkElement Tally, FrameworkElement MicButton, FrameworkElement Expanded);

/// <summary>
/// Runs G1–G4, G6, G7 and G9 on two Win2D surfaces: a swap chain behind the pill's content (glass, ribbon), presented
/// straight to the compositor because it changes every frame while you talk, and a canvas over the whole window (comet,
/// shockwave, frost, shards, sparks), which XAML redraws only during an effect. Both are redrawn from a timer only while something is moving: 60 frames a
/// second during an effect, 30 while only the voice moves. Between events it is asleep and the canvases keep their last
/// frame: an idle HUD draws nothing. (A timer rather than CompositionTarget.Rendering, which keeps all of XAML rendering
/// at 60 Hz for as long as anything listens to it.)
/// </summary>
internal sealed class HudEffects : IDisposable
{
    private const float PillHeight = 44;
    private const float PillRadius = 8;
    private const double ShockSeconds = 0.7;
    private static readonly TimeSpan EffectFrame = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan VoiceFrame = TimeSpan.FromMilliseconds(33);

    private static readonly Vector3 Green = new(0.30f, 0.86f, 0.46f);
    private static readonly Vector3 Red = new(1.0f, 0.26f, 0.30f);
    private static readonly Vector3 Amber = new(1.0f, 0.72f, 0.20f);

    private readonly HudViewModel _viewModel;
    private readonly HudParts _parts;
    private readonly PixelShaderEffect<GlassShader> _glass = new();
    private readonly PixelShaderEffect<OverlayShader> _overlay = new();
    private readonly PixelShaderEffect<FrostShader> _frostEffect = new();
    private CanvasSwapChain? _backChain;
    private readonly ParticleSystem _sparks = new();
    private readonly DispatcherQueueTimer _frames;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private EffectsLevel _requested = EffectsLevel.Full;
    private EffectsLevel _level = EffectsLevel.Off;
    private bool _sounds;
    private bool _started;
    private bool _awake;
    private bool _frozen;
    private bool _frontDirty;   // the overlay showed something last frame, so it needs one more draw to clear
    private double _lastFrame;

    private double _time;
    private double _auroraTime;
    private float _energy;
    private double _quietFor = double.MaxValue;
    private Vector3 _tint;
    private float _tintMix;

    private double _shockStart = double.NegativeInfinity;
    private float _shockKind;
    private Vector3 _shockColour;
    private Point _shockOrigin;

    private double _cometStart;
    private float _cometAlpha;
    private float _cometHead;

    private float _frost;
    private ShardField? _shards;
    private CanvasRenderTarget? _frostImage;

    private double _sparkTargetAt = double.PositiveInfinity;
    private Point _pointer;
    private double _pointerAt = double.NegativeInfinity;

    public HudEffects(HudViewModel viewModel, HudParts parts)
    {
        (_viewModel, _parts) = (viewModel, parts);
        _frames = parts.Root.DispatcherQueue.CreateTimer();
        _frames.Tick += (_, _) => OnFrame();
        viewModel.PropertyChanged += OnViewModelChanged;
        viewModel.Arrived += OnArrived;
        viewModel.Rated += OnRated;
        viewModel.Milestone += OnMilestone;
        parts.Back.SizeChanged += (_, _) => PresentBack();
        parts.Front.Draw += DrawFront;
        parts.Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), handledEventsToo: true);
        parts.Root.SizeChanged += (_, _) => FitFront();
        PowerManager.EnergySaverStatusChanged += OnEnergySaverChanged;
    }

    /// <summary>Takes the settings; until the first call nothing reacts, so a burst of cards at startup stays quiet.</summary>
    public void Apply(EffectsLevel requested, bool sounds)
    {
        (_requested, _sounds, _started) = (requested, sounds, true);
        Refresh();
    }

    /// <summary>The level actually shown: Windows' "Animation effects" off or high contrast mean Off; battery saver caps it at Subtle.</summary>
    public static EffectsLevel Effective(EffectsLevel requested)
    {
        if (requested == EffectsLevel.Off || !new UISettings().AnimationsEnabled || new AccessibilitySettings().HighContrast) return EffectsLevel.Off;
        return requested == EffectsLevel.Full && PowerManager.EnergySaverStatus == EnergySaverStatus.On ? EffectsLevel.Subtle : requested;
    }

    private void Refresh()
    {
        _level = Motion.Level = Effective(_requested);
        var on = _level != EffectsLevel.Off;
        _parts.Back.Visibility = _parts.Front.Visibility = _parts.RibbonSlot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _parts.LevelBars.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        _parts.Busy.Opacity = on ? 0 : 1;   // the comet takes over from the progress bar, which stays for screen readers
        _sparks.Clear();
        EndShards();
        _frost = on && _viewModel.IsMuted ? 1 : 0;
        _tintMix = Tint(_viewModel.Focus);
        FitFront();
        Redraw();
        Wake();
    }

    private void OnEnergySaverChanged(object? sender, object e) => _parts.Root.DispatcherQueue.TryEnqueue(() =>
    {
        if (_started) Refresh();
    });

    private void FitFront()
    {
        // The overlay sits in a zero-size Canvas so it never affects layout; it is as wide as the window and tall enough for the panel.
        _parts.Front.Width = Math.Max(1, _parts.Root.ActualWidth);
        _parts.Front.Height = 720;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pointer = e.GetCurrentPoint(_parts.Root).Position;
        _pointerAt = _time;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_started) return;
        // Sounds are their own switch: mute still chirps with the effects off.
        if (e.PropertyName == nameof(HudViewModel.IsMuted) && _sounds) SoundPalette.Play(_viewModel.IsMuted ? Cue.Muted : Cue.Unmuted);
        if (_level == EffectsLevel.Off) return;
        switch (e.PropertyName)
        {
            case nameof(HudViewModel.IsChecking):
                if (_viewModel.IsChecking) _cometStart = _time;
                Wake();
                break;
            case nameof(HudViewModel.IsMuted):
                OnMuted();
                break;
            case nameof(HudViewModel.MicLevel):
                if (_viewModel.MicLevel > 0.2) Wake();
                break;
            case nameof(HudViewModel.Focus):
                _tintMix = Tint(_viewModel.Focus);
                Redraw();
                break;
        }
    }

    private void OnMuted()
    {
        if (!_viewModel.IsMuted && _level == EffectsLevel.Full && _frost > 0.3f) Shatter();
        Wake();
    }

    /// <summary>G9: the frost as it is now becomes a texture, broken into shards thrown from where the click was (or the mic).</summary>
    private void Shatter()
    {
        var canvas = _parts.Front;
        if (!canvas.ReadyToDraw) return;
        var width = (float)_parts.Root.ActualWidth;
        _frostImage?.Dispose();
        _frostImage = new CanvasRenderTarget(canvas, width, PillHeight);
        using (var ds = _frostImage.CreateDrawingSession())
        {
            ds.Clear(Color.FromArgb(0, 0, 0, 0));
            DrawFrost(ds, width, canvas.Dpi / 96, _frost);
        }
        var impact = _time - _pointerAt < 0.6 ? _pointer : Centre(_parts.MicButton);
        _shards = new ShardField(width, PillHeight, new Vector2((float)impact.X, (float)impact.Y), seed: Environment.TickCount);
        _frost = 0;
    }

    private void EndShards()
    {
        _shards = null;
        _frostImage?.Dispose();
        _frostImage = null;
    }

    private void OnArrived(InsightCardViewModel card)
    {
        if (!_started) return;
        if (_sounds && card.Verdict is { } verdict)
            SoundPalette.Play(verdict switch { Verdict.Supported => Cue.Supported, Verdict.Contradicted => Cue.Contradicted, _ => Cue.Unverified },
                pan: !card.ShowSpeaker ? 0 : card.Source == SourceHint.Mic ? -0.35f : 0.35f);
        if (_level == EffectsLevel.Off) return;

        _tintMix = Tint(card);
        (_shockStart, _shockOrigin) = (_time, At(_parts.Headline, 12, _parts.Headline.ActualHeight / 2));
        (_shockKind, _shockColour) = card.Verdict switch
        {
            Verdict.Supported => (0f, Green),
            Verdict.Contradicted => (1f, Red),
            Verdict.Unverified => (2f, Amber),
            _ => (0f, Accent()),
        };
        if (card.Verdict == Verdict.Contradicted) Motion.Glitch(_parts.Headline);
        if (_level == EffectsLevel.Full && card.Verdict == Verdict.Supported && card.HasLink)
        {
            // G6: evidence dust, falling out of the pill and gathering on the source link once the panel has opened
            var from = Vector(_shockOrigin) + new Vector2(0, 14);
            _sparks.Target = null;
            _sparks.Burst(from, 80, 460, MathF.PI / 2, 1.3f, 1.35f, [new(Green, 1), new(0.75f, 1, 0.8f, 1), new(Accent(), 1), new(1, 1, 1, 1)]);
            _sparkTargetAt = _time + 0.3;
        }
        Wake();
    }

    private void OnRated(InsightCardViewModel card)
    {
        if (card.Rating == Rating.None) return;
        if (_sounds) SoundPalette.Play(Cue.Rated);
        if (_level != EffectsLevel.Full || _time - _pointerAt > 0.6) return;
        var colour = card.Rating == Rating.Up ? Green : Red;
        _sparks.Burst(Vector(_pointer), 26, 260, -MathF.PI / 2, MathF.PI, 0.7f, [new(colour, 1), new(1, 1, 1, 1)]);
        Wake();
    }

    private void OnMilestone(int ups)
    {
        if (_sounds) SoundPalette.Play(Cue.Milestone);
        if (_level != EffectsLevel.Full) return;
        _sparks.Target = null;
        _sparks.Burst(Vector(Centre(_parts.Tally)), 160, 560, MathF.PI / 2, MathF.PI * 0.95f, 1.5f,
            [new(1, 0.84f, 0.3f, 1), new(Green, 1), new(Accent(), 1), new(1, 1, 1, 1)]);
        Wake();
    }

    // ---------------------------------------------------------------- the frame loop

    private bool AuroraLive => _level == EffectsLevel.Full && _quietFor < 2;
    private bool ShockLive => _time - _shockStart < ShockSeconds;
    private bool FrostMoving => _frost != (_viewModel.IsMuted ? 1 : 0);
    private bool Transient => ShockLive || _cometAlpha > 0 || _viewModel.IsChecking || FrostMoving || _shards is not null || _sparks.IsAlive;
    private bool Busy => Transient || AuroraLive || _energy > 0.01f;

    // Hidden from the tray: nothing to draw for, however much the voice moves.
    private bool Hidden => _parts.Root.XamlRoot is { IsHostVisible: false };

    private void Wake()
    {
        if (_awake || _frozen || _level == EffectsLevel.Off || Hidden) return;
        _awake = true;
        _lastFrame = _clock.Elapsed.TotalSeconds;
        _frames.Interval = EffectFrame;
        _frames.Start();
    }

    private void Sleep()
    {
        _frames.Stop();
        _awake = false;
    }

    private void OnFrame()
    {
        var now = _clock.Elapsed.TotalSeconds;
        Advance(Math.Clamp(now - _lastFrame, 0, 0.05));
        _lastFrame = now;
        var transient = Transient;
        PresentBack();
        if (transient || _frontDirty) _parts.Front.Invalidate();
        _frontDirty = transient;
        // Only the voice moving: 30 frames a second is plenty. Anything else (a shockwave, the comet, debris) gets 60.
        _frames.Interval = transient ? EffectFrame : VoiceFrame;
        if (!Busy || _level == EffectsLevel.Off || Hidden) Sleep();
    }

    private void Advance(double dt)
    {
        _time += dt;
        if (_level == EffectsLevel.Full) _auroraTime += dt;

        var heard = (float)Math.Clamp((_viewModel.MicLevel - 0.2) / 0.45, 0, 1);
        _energy += (heard - _energy) * (1 - MathF.Exp(-(float)dt * (heard > _energy ? 18 : 5)));
        if (_energy < 0.01f && heard == 0) _energy = 0;
        _quietFor = heard > 0.05f ? 0 : _quietFor + dt;

        _cometAlpha = Toward(_cometAlpha, _viewModel.IsChecking ? 1 : 0, dt / 0.25);
        if (_cometAlpha > 0) _cometHead = (float)((_cometHead + dt * 0.45) % 1);

        if (_shards is null) _frost = Toward(_frost, _viewModel.IsMuted ? 1 : 0, dt / (_level == EffectsLevel.Full ? 0.7 : 0.35));
        _shards?.Step((float)dt);
        if (_shards?.IsDone == true) EndShards();

        if (_time >= _sparkTargetAt)
        {
            _sparkTargetAt = double.PositiveInfinity;
            _sparks.Target = LinkTarget();
        }
        _sparks.Step((float)dt);
    }

    private void Redraw()
    {
        PresentBack();
        _parts.Front.Invalidate();
    }

    // ---------------------------------------------------------------- drawing

    /// <summary>Draws the glass and the ribbon into the back swap chain (made, or resized, to fit the pill first) and presents it.</summary>
    private void PresentBack()
    {
        var panel = _parts.Back;
        if (_level == EffectsLevel.Off || panel.XamlRoot is null || panel.ActualWidth < 1 || panel.ActualHeight < 1) return;
        var dpi = (float)(96 * panel.XamlRoot.RasterizationScale);
        var (w, h) = ((float)panel.ActualWidth, (float)panel.ActualHeight);
        try
        {
            if (_backChain is null)
            {
                _backChain = new CanvasSwapChain(CanvasDevice.GetSharedDevice(), w, h, dpi, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, CanvasAlphaMode.Premultiplied);
                panel.SwapChain = _backChain;
            }
            else if (_backChain.Size.Width != w || _backChain.Size.Height != h || _backChain.Dpi != dpi)
            {
                _backChain.ResizeBuffers(w, h, dpi);
            }
            using (var ds = _backChain.CreateDrawingSession(Color.FromArgb(0, 0, 0, 0))) DrawBack(ds, panel, dpi / 96);
            _backChain.Present();
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // the GPU was reset (driver update, sleep): drop the chain and make a new one next frame
            _backChain?.Dispose();
            _backChain = null;
        }
    }

    private void DrawBack(CanvasDrawingSession ds, FrameworkElement sender, float scale)
    {
        var (width, height) = ((float)(sender.ActualWidth * scale), (float)(sender.ActualHeight * scale));

        var slot = _parts.RibbonSlot.TransformToVisual(sender).TransformBounds(new Rect(0, 0, _parts.RibbonSlot.ActualWidth, _parts.RibbonSlot.ActualHeight));
        var ripple = _level == EffectsLevel.Full && ShockLive ? (float)(_time - _shockStart) : -1;
        var origin = _parts.Root.TransformToVisual(sender).TransformPoint(_shockOrigin);
        var speaker = _viewModel.LevelSource == SourceHint.Teams ? Amber : Accent();
        _glass.ConstantBuffer = new GlassShader((float)_auroraTime, new float2(width, height), (float)scale,
            _level == EffectsLevel.Full ? 1 : 0.6f, _energy, F3(_tint), _tintMix, ripple, new float2((float)(origin.X * scale), (float)(origin.Y * scale)),
            new float4((float)(slot.X * scale), (float)(slot.Y * scale), (float)(slot.Width * scale), (float)(slot.Height * scale)), F3(speaker));
        ds.Units = CanvasUnits.Pixels;
        ds.DrawImage(_glass, 0, 0, new Rect(0, 0, width, height));
    }

    private void DrawFront(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_level == EffectsLevel.Off) return;
        var ds = args.DrawingSession;
        var scale = sender.Dpi / 96;
        var width = (float)_parts.Root.ActualWidth;
        if (width < 1) return;

        if (_cometAlpha > 0 || ShockLive)
        {
            var comet = Vector3.Lerp(Accent(), Amber, (float)Math.Clamp((_time - _cometStart - 2) / 1.5, 0, 1));
            _overlay.ConstantBuffer = new OverlayShader((float)_time, new float2(width * scale, PillHeight * scale), scale, PillRadius * scale,
                _cometAlpha, _cometHead, F3(comet),
                ShockLive ? (float)(_time - _shockStart) : -1, new float2((float)(_shockOrigin.X * scale), (float)(_shockOrigin.Y * scale)),
                F3(_shockColour), _shockKind);
            ds.Units = CanvasUnits.Pixels;
            ds.DrawImage(_overlay, 0, 0, new Rect(0, 0, width * scale, PillHeight * scale));
            ds.Units = CanvasUnits.Dips;
        }
        if (_frost > 0 && _shards is null) DrawFrost(ds, width, scale, _frost);
        if (_shards is not null && _frostImage is not null) DrawShards(ds);
        DrawSparks(ds);
    }

    private void DrawFrost(CanvasDrawingSession ds, float width, float scale, float progress)
    {
        _frostEffect.ConstantBuffer = new FrostShader((float)_time, new float2(width * scale, PillHeight * scale), scale, PillRadius * scale,
            1 - (1 - progress) * (1 - progress));   // ease out: the frost races in, then creeps
        ds.Units = CanvasUnits.Pixels;
        ds.DrawImage(_frostEffect, 0, 0, new Rect(0, 0, width * scale, PillHeight * scale));
        ds.Units = CanvasUnits.Dips;
    }

    private void DrawShards(CanvasDrawingSession ds)
    {
        var fade = 1 - Math.Clamp(_shards!.Age / ShardField.Life, 0, 1);
        foreach (var s in _shards.Shards)
        {
            ds.Transform = Matrix3x2.CreateTranslation(-s.Centre) * Matrix3x2.CreateRotation(s.Angle) * Matrix3x2.CreateTranslation(s.Centre + s.Offset);
            using var piece = CanvasGeometry.CreatePolygon(ds, [s.A, s.B, s.C]);
            using (ds.CreateLayer(fade, piece)) ds.DrawImage(_frostImage);
            ds.DrawGeometry(piece, Color.FromArgb((byte)(160 * fade), 255, 255, 255), 0.75f);
        }
        ds.Transform = Matrix3x2.Identity;
    }

    private void DrawSparks(CanvasDrawingSession ds)
    {
        if (!_sparks.IsAlive) return;
        var blend = ds.Blend;
        ds.Blend = CanvasBlend.Add;   // overlapping sparks get brighter, like light
        foreach (var p in _sparks.Particles)
        {
            var life = 1 - p.Age / p.Life;
            var at = p.Position;
            ds.FillCircle(at, p.Size * 3.2f, Colour(p.Color, 0.16f * life));
            ds.FillCircle(at, p.Size, Colour(p.Color, 0.9f * life));
            // a short streak along the velocity: motion blur for free
            ds.DrawLine(at, at - p.Velocity * 0.018f, Colour(p.Color, 0.5f * life), p.Size * 0.9f);
        }
        ds.Blend = blend;
    }

    // ---------------------------------------------------------------- helpers

    private static float Toward(float value, float target, double step) =>
        value < target ? MathF.Min(target, value + (float)step) : MathF.Max(target, value - (float)step);

    private float Tint(InsightCardViewModel? card)
    {
        _tint = card?.Verdict switch { Verdict.Supported => Green, Verdict.Contradicted => Red, Verdict.Unverified => Amber, _ => _tint };
        return card?.Verdict is null ? 0 : _level == EffectsLevel.Full ? 0.38f : 0.25f;
    }

    private static Vector3 Accent() =>
        Application.Current.Resources.TryGetValue("SystemAccentColorLight1", out var value) && value is Color c
            ? new Vector3(c.R / 255f, c.G / 255f, c.B / 255f)
            : new Vector3(0.3f, 0.6f, 1);

    private static float3 F3(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Color Colour(Vector4 c, float alpha) =>
        Color.FromArgb((byte)(Math.Clamp(alpha * c.W, 0, 1) * 255), (byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255));

    private static Vector2 Vector(Point p) => new((float)p.X, (float)p.Y);

    private Point At(FrameworkElement element, double x, double y) => element.TransformToVisual(_parts.Root).TransformPoint(new Point(x, y));

    private Point Centre(FrameworkElement element) => At(element, element.ActualWidth / 2, element.ActualHeight / 2);

    /// <summary>The focus card's source link, if the panel shows it: where the evidence dust settles.</summary>
    private Vector2? LinkTarget()
    {
        if (!_viewModel.IsExpanded) return null;
        var link = Find(_parts.Expanded, "LinkButton");
        return link is { Visibility: Visibility.Visible, ActualWidth: > 0 } ? Vector(Centre(link)) : null;
    }

    private static FrameworkElement? Find(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: var n } element && n == name) return element;
            if (Find(child, name) is { } found) return found;
        }
        return null;
    }

#if DEBUG
    /// <summary>Runs the effects forward by <paramref name="seconds"/> in small fixed steps, then holds that frame if asked: for screenshots.</summary>
    public void FastForward(double seconds, bool hold)
    {
        Sleep();
        for (var t = 0d; t < seconds; t += 1 / 120d) Advance(1 / 120d);
        _frozen = hold;
        Redraw();
        if (!hold) Wake();
    }
#endif

    public void Dispose()
    {
        Sleep();
        _clock.Stop();
        PowerManager.EnergySaverStatusChanged -= OnEnergySaverChanged;
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel.Arrived -= OnArrived;
        _viewModel.Rated -= OnRated;
        _viewModel.Milestone -= OnMilestone;
        EndShards();
        _backChain?.Dispose();
        _glass.Dispose();
        _overlay.Dispose();
        _frostEffect.Dispose();
    }
}

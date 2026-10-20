using System.Diagnostics;
using System.Numerics;
using ComputeSharp;
using ComputeSharp.D2D1.WinUI;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PoVoiceHud.App.Fx;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Fx;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.ViewModels;
using Windows.UI;

namespace PoVoiceHud.App.Views;

public sealed partial class ConstellationView : UserControl, IDisposable
{
    private const float Inset = 22;
    private const float HoverReach = 14;
    // After opening (or a hover) the sky keeps twinkling this long, then holds still and stops drawing.
    private static readonly TimeSpan Lively = TimeSpan.FromSeconds(12);

    private readonly PixelShaderEffect<StarfieldShader> _sky = new();
    private readonly DispatcherQueueTimer _frames;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private List<InsightCardViewModel> _cards = [];
    private Constellation? _layout;
    private int _hover = -1;
    private bool _awake;
    private double _lastFrame;
    private double _time;
    private double _sleepAt;

    public ConstellationView()
    {
        InitializeComponent();
        // 30 frames a second: stars settling and twinkling need no more
        _frames = DispatcherQueue.CreateTimer();
        _frames.Interval = TimeSpan.FromMilliseconds(33);
        _frames.Tick += (_, _) => OnFrame();
        Unloaded += (_, _) => Sleep();
    }

    /// <summary>A star was clicked.</summary>
    public event Action<InsightCardViewModel>? Picked;

    /// <summary>Lays out <paramref name="cards"/> (oldest first), keeping the stars that were already there where they are.</summary>
    public void Show(IReadOnlyList<InsightCardViewModel> cards)
    {
        ReadOnlySpan<Vector2> kept = _layout is null ? default : _layout.Positions[..Math.Min(_layout.Positions.Length, cards.Count)];
        _cards = [.. cards];
        _layout = new Constellation(_cards.Select(c => c.Title).ToList(), kept);
        if (Motion.Level == EffectsLevel.Off) _layout.Settle();
        Empty.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Sky.Invalidate();
        Wake();
    }

    public void Sleep()
    {
        _frames.Stop();
        _awake = false;
    }

    private void Wake()
    {
        _sleepAt = _time + Lively.TotalSeconds;
        if (_awake || Motion.Level == EffectsLevel.Off) return;
        _awake = true;
        _lastFrame = _clock.Elapsed.TotalSeconds;
        _frames.Start();
    }

    private void OnFrame()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - _lastFrame, 0, 0.05);
        _lastFrame = now;
        _time += dt;
        _layout?.Step((float)dt);
        Sky.Invalidate();
        if (_time > _sleepAt && (_layout is null || _layout.Energy < 1e-4f)) Sleep();
    }

    private static Vector2 Place(Vector2 p, Vector2 size) =>
        new(Inset + (p.X + 1) / 2 * (size.X - 2 * Inset), Inset + (p.Y + 1) / 2 * (size.Y - 2 * Inset));

    private static Vector3 Hue(InsightCardViewModel card) => card.Verdict switch
    {
        Verdict.Supported => new(0.36f, 0.92f, 0.52f),
        Verdict.Contradicted => new(1.0f, 0.34f, 0.36f),
        Verdict.Unverified => new(1.0f, 0.76f, 0.28f),
        _ => new(0.45f, 0.68f, 1.0f),
    };

    private void Sky_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var scale = sender.Dpi / 96;
        var size = new Vector2((float)sender.ActualWidth, (float)sender.ActualHeight);
        if (size.X < 1 || size.Y < 1) return;

        // the nebula takes the session's mix of verdict colours
        var tint = _cards.Count == 0 ? new Vector3(0.5f, 0.3f, 0.9f) : _cards.Aggregate(Vector3.Zero, (sum, c) => sum + Hue(c)) / _cards.Count;
        _sky.ConstantBuffer = new StarfieldShader((float)_time, new float2(size.X * scale, size.Y * scale), scale, 6 * scale, new float3(tint.X, tint.Y, tint.Z));
        ds.Units = CanvasUnits.Pixels;
        ds.DrawImage(_sky, 0, 0, new Windows.Foundation.Rect(0, 0, size.X * scale, size.Y * scale));
        ds.Units = CanvasUnits.Dips;
        if (_layout is null) return;

        var at = _layout.Positions;
        foreach (var (a, b) in _layout.Links)
            ds.DrawLine(Place(at[a], size), Place(at[b], size), Color.FromArgb(70, 170, 200, 255), 1);

        ds.Blend = CanvasBlend.Add;
        for (var i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i];
            var centre = Place(at[i], size);
            var twinkle = 1 + 0.12f * MathF.Sin((float)_time * 2.1f + i * 1.7f);
            var radius = (2.2f + 2.6f * (float)card.Insight.Confidence) * twinkle * (i == _hover ? 1.4f : 1);
            var hue = Hue(card);
            ds.FillCircle(centre, radius * 4.5f, Rgba(hue, 0.10f));
            ds.FillCircle(centre, radius * 2.2f, Rgba(hue, 0.28f));
            ds.FillCircle(centre, radius, Rgba(hue, 0.95f));
            ds.FillCircle(centre, radius * 0.45f, Color.FromArgb(230, 255, 255, 255));
            // a four-point diffraction spike on the brightest (most confident) stars
            if (card.Insight.Confidence >= 0.9)
            {
                var spike = radius * 3.6f;
                ds.DrawLine(centre - new Vector2(spike, 0), centre + new Vector2(spike, 0), Rgba(hue, 0.45f), 0.8f);
                ds.DrawLine(centre - new Vector2(0, spike), centre + new Vector2(0, spike), Rgba(hue, 0.45f), 0.8f);
            }
        }
        ds.Blend = CanvasBlend.SourceOver;
        if (_hover >= 0 && _hover < _cards.Count)
            ds.DrawCircle(Place(at[_hover], size), 9, Color.FromArgb(200, 255, 255, 255), 1.2f);
    }

    private static Color Rgba(Vector3 c, float alpha) => Color.FromArgb((byte)(alpha * 255), (byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255));

    private void Sky_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_layout is null) return;
        var point = e.GetCurrentPoint(Sky).Position;
        var pointer = new Vector2((float)point.X, (float)point.Y);
        var size = new Vector2((float)Sky.ActualWidth, (float)Sky.ActualHeight);
        var (nearest, best) = (-1, HoverReach);
        for (var i = 0; i < _cards.Count; i++)
        {
            var d = Vector2.Distance(Place(_layout.Positions[i], size), pointer);
            if (d < best) (nearest, best) = (i, d);
        }
        if (nearest == _hover) return;
        _hover = nearest;
        if (nearest >= 0)
        {
            var star = Place(_layout.Positions[nearest], size);
            LabelText.Text = _cards[nearest].Summary;
            Label.Visibility = Visibility.Visible;
            Label.Measure(new Windows.Foundation.Size(Label.MaxWidth, double.PositiveInfinity));
            var x = Math.Clamp(star.X + 12, 4, size.X - Label.DesiredSize.Width - 4);
            var y = star.Y + 12 + Label.DesiredSize.Height > size.Y ? star.Y - 12 - Label.DesiredSize.Height : star.Y + 12;
            Label.Margin = new Thickness(x, y, 0, 0);
        }
        else Label.Visibility = Visibility.Collapsed;
        Sky.Invalidate();
        Wake();
    }

    private void Sky_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hover = -1;
        Label.Visibility = Visibility.Collapsed;
        Sky.Invalidate();
    }

    private void Sky_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_hover >= 0 && _hover < _cards.Count) Picked?.Invoke(_cards[_hover]);
    }

    public void Dispose()
    {
        Sleep();
        _clock.Stop();
        _sky.Dispose();
    }
}

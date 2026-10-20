using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PoVoiceHud.App.Fx;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace PoVoiceHud.App.Views;

public sealed partial class InsightCard : UserControl
{
    public static readonly DependencyProperty CardProperty =
        DependencyProperty.Register(nameof(Card), typeof(InsightCardViewModel), typeof(InsightCard),
            new PropertyMetadata(null, (d, _) => ((InsightCard)d).Arrive()));

    private static readonly Brush None = new SolidColorBrush(Colors.Transparent);
    // A swipe this far (DIPs), or faster than SwipeSpeed (DIPs per ms), throws the card away.
    private const double SwipeDistance = 120;
    private const double SwipeSpeed = 1;

    private double _drag;

    public InsightCard()
    {
        InitializeComponent();
    }

    public InsightCardViewModel? Card
    {
        get => (InsightCardViewModel?)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    /// <summary>The pill's bottom edge: the verdict's colour (green, red, amber), nothing for cards without a verdict.</summary>
    public static Brush EdgeBrush(InsightCardViewModel? card) => card?.Verdict switch
    {
        Verdict.Supported => Theme("SystemFillColorSuccessBrush"),
        Verdict.Contradicted => Theme("SystemFillColorCriticalBrush"),
        Verdict.Unverified => Theme("SystemFillColorCautionBrush"),
        _ => None,
    };

    /// <summary>A 👍 shows green and a 👎 red once chosen; unchosen they keep the ordinary icon colour.</summary>
    public static Brush Lit(bool chosen, bool up) => !chosen ? Theme("TextFillColorSecondaryBrush")
        : Theme(up ? "SystemFillColorSuccessBrush" : "SystemFillColorCriticalBrush");

    public static string SaidBy(string speaker) => speaker switch
    {
        "You" => "You said this (microphone)",
        "Them" => "Someone on the call said this",
        _ => "Not sure who said this",
    };

    private static Brush Theme(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush ? brush : None;

    /// <summary>A new card in the frame: it springs in (G5) and its answer comes into focus (G8) once laid out.</summary>
    private void Arrive()
    {
        _drag = 0;
        Body.Translation = Vector3.Zero;
        Body.Opacity = 1;
        if (Card is not { } card) return;
        Motion.Enter(Body);
        var glint = card.Insight.Confidence >= 0.9;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => Motion.Materialize(AnswerText, glint));
    }

    private void Body_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        _drag += e.Delta.Translation.X;
        Body.Translation = new Vector3((float)_drag, 0, 0);
        Body.Opacity = Math.Clamp(1 - Math.Abs(_drag) / 420, 0.25, 1);
        if (e.IsInertial && Math.Abs(_drag) > SwipeDistance) e.Complete();
    }

    private void Body_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        var (drag, speed) = (_drag, e.Velocities.Linear.X);
        _drag = 0;
        if (Card is not { } card) return;
        var thrown = Math.Abs(drag) > SwipeDistance || Math.Abs(speed) > SwipeSpeed;
        if (Motion.Level == EffectsLevel.Off)
        {
            Body.Translation = Vector3.Zero;
            Body.Opacity = 1;
            if (thrown) card.DismissCommand.Execute(null);
        }
        else if (thrown) Motion.FlyOut(Body, Math.Abs(drag) > 1 ? Math.Sign(drag) : Math.Sign(speed), Math.Abs(speed), () => card.DismissCommand.Execute(null));
        else Motion.SpringBack(Body);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Card is null) return;
        var data = new DataPackage();
        data.SetText(Card.CopyText);
        Clipboard.SetContent(data);
    }
}

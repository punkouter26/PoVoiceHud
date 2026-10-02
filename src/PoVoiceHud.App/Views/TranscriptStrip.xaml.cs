using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PoVoiceHud.Core;
using PoVoiceHud.Core.ViewModels;

namespace PoVoiceHud.App.Views;

public sealed partial class TranscriptStrip : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(HudViewModel), typeof(TranscriptStrip), new PropertyMetadata(null));

    public TranscriptStrip()
    {
        InitializeComponent();
    }

    public HudViewModel? ViewModel
    {
        get => (HudViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>"[Teams 04:12]" — minutes:seconds since capture started.</summary>
    public static string Stamp(Core.Audio.SourceHint source, TimeSpan at) => $"[{source} {at:mm\\:ss}]";
}

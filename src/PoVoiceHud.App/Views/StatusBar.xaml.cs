using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PoVoiceHud.Core.ViewModels;

namespace PoVoiceHud.App.Views;

public sealed partial class StatusBar : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(HudViewModel), typeof(StatusBar), new PropertyMetadata(null));

    public StatusBar()
    {
        InitializeComponent();
    }

    public HudViewModel? ViewModel
    {
        get => (HudViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}

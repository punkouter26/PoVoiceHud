using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PoVoiceHud.Core.ViewModels;

namespace PoVoiceHud.App.Views;

public sealed partial class FocusCard : UserControl
{
    public static readonly DependencyProperty CardProperty =
        DependencyProperty.Register(nameof(Card), typeof(InsightCardViewModel), typeof(FocusCard), new PropertyMetadata(null));

    public FocusCard()
    {
        InitializeComponent();
    }

    public InsightCardViewModel? Card
    {
        get => (InsightCardViewModel?)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }
}

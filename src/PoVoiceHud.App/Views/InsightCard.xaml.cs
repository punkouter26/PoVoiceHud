using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace PoVoiceHud.App.Views;

public sealed partial class InsightCard : UserControl
{
    public static readonly DependencyProperty CardProperty =
        DependencyProperty.Register(nameof(Card), typeof(InsightCardViewModel), typeof(InsightCard), new PropertyMetadata(null));

    private static readonly Brush None = new SolidColorBrush(Colors.Transparent);

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

    private static Brush Theme(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush ? brush : None;

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Card is null) return;
        var data = new DataPackage();
        data.SetText(Card.CopyText);
        Clipboard.SetContent(data);
    }
}

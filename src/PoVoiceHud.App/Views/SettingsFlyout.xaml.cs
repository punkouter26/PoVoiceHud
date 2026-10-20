using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PoVoiceHud.Core.ViewModels;

namespace PoVoiceHud.App.Views;

public sealed partial class SettingsFlyout : UserControl
{
    public SettingsFlyout(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }

    /// <summary>Closes the app; set by the window.</summary>
    public Action? Quit { get; set; }

    /// <summary>Scroll rather than run off the screen: <paramref name="height"/> is what the screen has room for.</summary>
    public void FitTo(double height) => Scroller.MaxHeight = Math.Max(240, height);

    public static string KeyPlaceholder(bool hasKey) => hasKey ? "•••••••• (stored — type to replace)" : "not set";

    /// <summary>An error line takes no room until there is an error.</summary>
    public static Visibility Shown(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The connection's dot: green once the check passed, red when it failed, grey before any check.</summary>
    public static Brush Dot(string? result) => Theme(result switch
    {
        ['✓', ..] => "SystemFillColorSuccessBrush",
        ['✗', ..] => "SystemFillColorCriticalBrush",
        _ => "ControlStrongFillColorDisabledBrush",
    });

    private static Brush Theme(string key) => (Brush)Application.Current.Resources[key];

    private void Quit_Click(object sender, RoutedEventArgs e) => Quit?.Invoke();
}

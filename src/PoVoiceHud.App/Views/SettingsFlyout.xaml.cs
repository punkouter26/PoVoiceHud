using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    public static string KeyPlaceholder(bool hasKey) => hasKey ? "•••••••• (stored — type to replace)" : "not set";

    /// <summary>An error line takes no room until there is an error.</summary>
    public static Visibility Shown(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
}

using System.Diagnostics;
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

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
        Application.Current.Exit();
    }
}

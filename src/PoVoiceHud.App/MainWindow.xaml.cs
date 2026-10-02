using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.ViewModels;
using WinUIEx;

namespace PoVoiceHud.App;

public sealed partial class MainWindow : WindowEx
{
    private const int PillHeight = 44;
    private const int ExpandedHeight = 540;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow(HudViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HudViewModel.IsExpanded)) ApplyHeight();
        };
        // WinUIEx restores the persisted size on activation, so re-apply the state-driven height afterwards.
        Activated += (_, _) => ApplyHeight();
        _tick.Tick += (_, _) => ViewModel.Tick(DateTimeOffset.Now);
        _tick.Start();
        Closed += (_, _) => _tick.Stop();
    }

    public HudViewModel ViewModel { get; }

    private void ApplyHeight() => Height = ViewModel.IsExpanded ? ExpandedHeight : PillHeight;

    public void AttachSettings(SettingsViewModel settings) =>
        SettingsButton.Flyout = new Flyout { Content = new Views.SettingsFlyout(settings), Placement = FlyoutPlacementMode.Bottom };

    public void ShowSettings()
    {
        if (Root.IsLoaded) SettingsButton.Flyout?.ShowAt(SettingsButton);
        else Root.Loaded += (_, _) => SettingsButton.Flyout?.ShowAt(SettingsButton);
    }

    public static Brush RecBrush(bool muted) => new SolidColorBrush(muted ? Colors.OrangeRed : Colors.LimeGreen);

    public static string ExpandGlyph(bool expanded) => expanded ? "▴" : "▾";

    private void Pill_Tapped(object sender, TappedRoutedEventArgs e) => ViewModel.ToggleExpandCommand.Execute(null);

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e) => ViewModel.IsPointerOver = true;

    private void Root_PointerExited(object sender, PointerRoutedEventArgs e) => ViewModel.IsPointerOver = false;

    /// <summary>Sample cards for layout checks: run with --demo.</summary>
    public void SeedDemo()
    {
        var now = DateTimeOffset.Now;
        var root = @"C:\src\EnterprisePMIS";
        ViewModel.SetStatus("● REC · AzDO: Connected");
        ViewModel.AddTranscript(new Core.TranscriptLine(SourceHint.Teams, TimeSpan.FromSeconds(252), "Does the inspection table have a tenant ID foreign key?"));
        ViewModel.AddTranscript(new Core.TranscriptLine(SourceHint.Mic, TimeSpan.FromSeconds(255), "Let me confirm the exact schema definition."));
        ViewModel.Push([new Insight(Intent.Pipeline, "[AZDO] PR Validation", "feature/pmis-sync: ✅ Succeeded (8m) · run #20261001.3", [], 0.9,
            Link: new Uri("https://dev.azure.com/acme/PMIS/_build/results?buildId=42"))], now.AddSeconds(-30));
        ViewModel.Push([new Insight(Intent.Docs, "[DOCS] ADR-0004: Auth Lifecycle", "Accepted: Use a ClaimsPrincipal header populated by the gateway (src/Auth).",
            [new Jump($@"{root}\docs\adr\0004-auth-lifecycle.md", 10)], 0.7, "⚠️ STALE DOCUMENTATION: Underlying code was modified on 2026-09-26 (5 days after this document)")], now.AddSeconds(-20));
        ViewModel.Push([new Insight(Intent.Schema, "[SCHEMA] Inspection.TenantId", "Yes. Inspection.TenantId is a required uniqueidentifier foreign key to Tenants.Id, indexed.",
            [new Jump($@"{root}\src\Domain\Entities\Inspection.cs", 28), new Jump($@"{root}\src\Infrastructure\Data\Migrations\AppDbContextModelSnapshot.cs", 142)], 1.0)], now);
    }
}

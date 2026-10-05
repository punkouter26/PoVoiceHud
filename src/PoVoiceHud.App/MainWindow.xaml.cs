using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PoVoiceHud.Core;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.ViewModels;
using WinUIEx;

namespace PoVoiceHud.App;

public sealed partial class MainWindow : WindowEx
{
    private const int PillHeight = 44;
    private const int MaxExpandedHeight = 640;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    // Eases the window towards _targetHeight a frame at a time: AppWindow has no animated resize of its own.
    private readonly DispatcherTimer _resize = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private double _targetHeight = PillHeight;
    private bool _settingsOpen;

    public MainWindow(HudViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HudViewModel.MicLevel)) ShowLevel(ViewModel.MicLevel);
            // The panel has to be laid out before its height is known, hence the low-priority hop.
            if (e.PropertyName == nameof(HudViewModel.IsExpanded)) DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ApplyHeight);
        };
        // WinUIEx restores the persisted size on first activation, so re-apply the state-driven height once after that.
        // (Activated also fires on deactivation, including while closing, when Resize is no longer valid.)
        void OnFirstActivation(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) return;
            Activated -= OnFirstActivation;
            ApplyHeight();
        }
        Activated += OnFirstActivation;
        _tick.Tick += (_, _) => ViewModel.Tick(DateTimeOffset.Now);
        _tick.Start();
        _resize.Tick += (_, _) => StepHeight();
        Closed += (_, _) =>
        {
            _tick.Stop();
            _resize.Stop();
        };
    }

    public HudViewModel ViewModel { get; }

    /// <summary>The window is exactly as tall as the pill plus whatever the expanded panel currently holds.</summary>
    private void ApplyHeight()
    {
        if (_settingsOpen) return;
        // Height is the outer window; the frame Windows draws around the content has to be added on top of the content's height.
        var scale = Content.XamlRoot?.RasterizationScale ?? 1;
        var frame = (AppWindow.Size.Height - AppWindow.ClientSize.Height) / scale;
        _targetHeight = PillHeight + frame + (ViewModel.IsExpanded ? Math.Min(Expanded.ActualHeight, MaxExpandedHeight) : 0);
        _resize.Start();
    }

    private void StepHeight()
    {
        var remaining = _targetHeight - Height;
        if (Math.Abs(remaining) < 2)
        {
            Height = _targetHeight;
            _resize.Stop();
        }
        else Height += remaining * 0.35;
    }

    private void Expanded_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyHeight();

    // Level (0 = −60 dBFS, 1 = full scale) at which each of the five bars lights. Room noise sits near 0.15–0.2 and
    // ordinary speech around 0.4–0.6, so silence shows nothing, talking shows three or four bars, and five means loud.
    private static readonly double[] BarThresholds = [0.25, 0.34, 0.43, 0.52, 0.62];

    private void ShowLevel(double level)
    {
        for (var i = 0; i < BarThresholds.Length; i++)
            LevelBars.Children[i].Opacity = level >= BarThresholds[i] ? 1 : 0.2;
    }

    public void AttachSettings(SettingsViewModel settings)
    {
        var flyout = new Flyout
        {
            Content = new Views.SettingsFlyout(settings),
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            ShouldConstrainToRootBounds = false,   // the pill is 44 px tall; the flyout must be allowed to spill outside it
        };
        // Resizing the window dismisses an open flyout, so while the settings are up the window keeps its size:
        // the panel is pinned (no auto-collapse) and height changes wait until the flyout closes.
        var wasPinned = false;
        flyout.Opening += (_, _) =>
        {
            wasPinned = ViewModel.IsPinned;
            ViewModel.IsPinned = true;
            settings.IsOpen = _settingsOpen = true;
            _resize.Stop();
        };
        flyout.Closed += (_, _) =>
        {
            settings.IsOpen = _settingsOpen = false;
            ViewModel.IsPinned = wasPinned;
            ApplyHeight();
            // There is no Save button: closing the settings is what applies them.
            settings.Apply();
            if (settings.Problem is { } problem) ViewModel.SetStatus($"Settings not applied — {problem}");
        };
        SettingsButton.Flyout = flyout;
    }

    public static Visibility Not(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static string MicName(bool muted) => muted ? "Microphone is muted — unmute" : "Microphone is live — mute";

    public static string MicTip(string status, string muteHint) => $"{status}\n{muteHint}";

    /// <summary>"[Teams 04:12] text" — minutes:seconds since capture started.</summary>
    public static string Stamped(SourceHint source, TimeSpan at, string text) => $"[{source} {at:mm\\:ss}] {text}";

    public static string Line(TranscriptLine? line) => line is null ? "Nothing heard yet." : Stamped(line.Source, line.At, line.Text);

    private void Pill_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // A tap on one of the pill's buttons also bubbles up to here. It belongs to that button: only a tap on
        // the bare pill toggles the panel. (Otherwise the chevron toggles twice and the gear collapses the panel.)
        for (var at = e.OriginalSource as DependencyObject; at is not null && !ReferenceEquals(at, sender); at = VisualTreeHelper.GetParent(at))
            if (at is ButtonBase) return;
        ViewModel.ToggleExpandCommand.Execute(null);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void History_Click(object sender, RoutedEventArgs e) => ViewModel.ShowCommand.Execute(((FrameworkElement)sender).Tag);

    private void Toggle_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.ToggleExpandCommand.Execute(null);
        args.Handled = true;
    }

    private void Escape_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.CollapseCommand.Execute(null);
        args.Handled = true;
    }

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e) => ViewModel.IsPointerOver = true;

    private void Root_PointerExited(object sender, PointerRoutedEventArgs e) => ViewModel.IsPointerOver = false;

#if DEBUG
    /// <summary>
    /// Puts the settings into the panel itself: run a Debug build with --demo --settings (add --all to unfold the model
    /// fields and fill in a repository). A flyout only opens for the foreground window, so this is the way to look at
    /// the settings layout without clicking anything.
    /// </summary>
    public void ShowSettings(bool everything)
    {
        if (SettingsButton.Flyout is not Flyout { Content: Views.SettingsFlyout settings } flyout) return;
        if (everything)
        {
            settings.ViewModel.ChangeModelCommand.Execute(null);
            settings.ViewModel.RepoPath = "https://dev.azure.com/org/project/_git/repo";
        }
        flyout.Content = null;
        Expanded.Children.Clear();
        Expanded.Children.Add(settings);
        ViewModel.IsPinned = ViewModel.IsExpanded = true;
    }

    /// <summary>Sample cards for layout checks: run a Debug build with --demo.</summary>
    public void SeedDemo()
    {
        var now = DateTimeOffset.Now;
        var root = @"C:\src\EnterprisePMIS";
        ViewModel.Apply(new Core.Config.Settings { RepoPath = root });
        ViewModel.AddTranscript(new TranscriptLine(SourceHint.Teams, TimeSpan.FromSeconds(252), "Does the inspection table have a tenant ID foreign key?"));
        ViewModel.AddTranscript(new TranscriptLine(SourceHint.Mic, TimeSpan.FromSeconds(255), "Python was created by Dennis Ritchie."));
        ViewModel.Push([new Core.Providers.Insight(Core.Routing.Intent.Pipeline, "[AZDO] PR Validation", "feature/pmis-sync: ✅ Succeeded (8m) · run #20261001.3", [], 0.9,
            Link: new Uri("https://dev.azure.com/acme/PMIS/_build/results?buildId=42"))], now.AddSeconds(-40));
        ViewModel.Push([new Core.Providers.Insight(Core.Routing.Intent.Docs, "[DOCS] ADR-0004: Auth Lifecycle", "Accepted: Use a ClaimsPrincipal header populated by the gateway (src/Auth).",
            [new Core.Providers.Jump($@"{root}\docs\adr\0004-auth-lifecycle.md", 10)], 0.7, "⚠️ STALE DOCUMENTATION: Underlying code was modified on 2026-09-26 (5 days after this document)")], now.AddSeconds(-30));
        ViewModel.Push([new Core.Providers.Insight(Core.Routing.Intent.Schema, "[SCHEMA] Inspection.TenantId", "Yes. Inspection.TenantId is a required uniqueidentifier foreign key to Tenants.Id, indexed.",
            [new Core.Providers.Jump($@"{root}\src\Domain\Entities\Inspection.cs", 28), new Core.Providers.Jump($@"{root}\src\Infrastructure\Data\Migrations\AppDbContextModelSnapshot.cs", 142)], 1.0)], now.AddSeconds(-20), SourceHint.Teams);
        ViewModel.Push([new Core.Providers.Insight(Core.Routing.Intent.FactCheck, "[FACT] The Eiffel Tower was completed in 1889", "✅ Supported — Completed in March 1889 for the Exposition Universelle.",
            [], 0.9, Link: new Uri("https://en.wikipedia.org/wiki/Eiffel_Tower"), Verdict: Core.Providers.Verdict.Supported)], now.AddSeconds(-10), SourceHint.Teams);
        ViewModel.Push([new Core.Providers.Insight(Core.Routing.Intent.FactCheck, "[FACT] The Eiffel Tower was completed in 1889", "✅ Supported — Completed in March 1889 for the Exposition Universelle.",
            [], 0.9, Link: new Uri("https://en.wikipedia.org/wiki/Eiffel_Tower"), Verdict: Core.Providers.Verdict.Supported)], now.AddSeconds(-5), SourceHint.Teams);   // said twice: one line, counted
        ViewModel.Push([new Core.Providers.Insight(Core.Routing.Intent.FactCheck, "[FACT] Python was created by Dennis Ritchie", "❌ Contradicted — Python was created by Guido van Rossum; Dennis Ritchie created C.",
            [], 0.9, Link: new Uri("https://en.wikipedia.org/wiki/Python_(programming_language)"), Verdict: Core.Providers.Verdict.Contradicted)], now);
        ViewModel.MicLevel = 0.5;   // an ordinary speaking voice, so the meter is visible in the sample
        ViewModel.IsPinned = true;
    }
#endif
}

using System.Collections.Specialized;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PoVoiceHud.App.Fx;
using PoVoiceHud.Core;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Fx;
using PoVoiceHud.Core.ViewModels;
using WinUIEx;

namespace PoVoiceHud.App;

public sealed partial class MainWindow : WindowEx
{
    private const int PillHeight = 44;
    private const int MaxExpandedHeight = 640;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    // Springs the window towards its target height a tick at a time: AppWindow has no animated resize of its own.
    private readonly DispatcherTimer _resize = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly Spring _height = new(stiffness: 520);
    private readonly Stopwatch _resizeClock = new();
    private bool _settingsOpen;
    private bool _newTop;   // a card was just pushed onto the history: its line drops in rather than just appearing

    public MainWindow(HudViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        AppWindow.SetIcon(IconPath);
        Effects = new HudEffects(viewModel, new HudParts(Root, Back, Front, RibbonSlot, LevelBars, Busy, HeadlineText, TallyText, MicButton, Expanded));
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HudViewModel.MicLevel)) ShowLevel(ViewModel.MicLevel);
            // The panel has to be laid out before its height is known, hence the low-priority hop.
            if (e.PropertyName == nameof(HudViewModel.IsExpanded)) DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ApplyHeight);
            if (e.PropertyName == nameof(HudViewModel.IsConstellationOpen))
            {
                if (ViewModel.IsConstellationOpen) Stars.Show(ViewModel.Session);
                else Stars.Sleep();
            }
        };
        ViewModel.Shelf.CollectionChanged += (_, e) => _newTop |= e is { Action: NotifyCollectionChangedAction.Add, NewStartingIndex: 0 };
        ViewModel.Arrived += _ =>
        {
            if (ViewModel.IsConstellationOpen) Stars.Show(ViewModel.Session);
        };
        // Clicking a star brings its card back, if it is still in the history.
        Stars.Picked += card => ViewModel.ShowCommand.Execute(card);
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
            Effects.Dispose();
            Stars.Dispose();
        };
    }

    public HudViewModel ViewModel { get; }

    /// <summary>Shaders, particles and sounds (G1–G10); the app hands it the settings.</summary>
    internal HudEffects Effects { get; }

    /// <summary>The app icon, copied next to the exe: the window, the tray and the installer all use it.</summary>
    public static string IconPath { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "PoVoiceHud.ico");

    /// <summary>The window is exactly as tall as the pill plus whatever the expanded panel currently holds.</summary>
    private void ApplyHeight()
    {
        if (_settingsOpen) return;
        // Height is the outer window; the frame Windows draws around the content has to be added on top of the content's height.
        var scale = Content.XamlRoot?.RasterizationScale ?? 1;
        var frame = (AppWindow.Size.Height - AppWindow.ClientSize.Height) / scale;
        _height.Target = PillHeight + frame + (ViewModel.IsExpanded ? Math.Min(Expanded.ActualHeight, MaxExpandedHeight) : 0);
        if (!_resize.IsEnabled) (_height.Value, _height.Velocity) = (Height, 0);
        // G5: at Full the window overshoots a few pixels and settles; at Subtle it arrives without overshoot; at Off it jumps.
        _height.DampingRatio = Motion.Level == EffectsLevel.Full ? 0.78 : 1;
        _resizeClock.Restart();
        _resize.Start();
    }

    private void StepHeight()
    {
        var settled = Motion.Level == EffectsLevel.Off || _height.Step(_resizeClock.Elapsed.TotalSeconds);
        _resizeClock.Restart();
        Height = settled ? _height.Target : Math.Max(PillHeight, _height.Value);
        if (settled) _resize.Stop();
    }

    private void Shelf_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        Motion.Slide(args.Element, args.Index == 0 && _newTop);
        if (args.Index == 0) _newTop = false;
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

    /// <summary>Minutes:seconds since capture started.</summary>
    public static string Stamp(TimeSpan at) => $"{at:mm\\:ss}";

    /// <summary>"You:" for the microphone, "Them:" for the call audio.</summary>
    public static string Said(SourceHint source) => $"{source.Speaker()}:";

    public static string Heard(TranscriptLine? line) => line?.Text ?? "Nothing heard yet.";

    /// <summary>Your lines in the accent colour, the call's in the ordinary text colour, so a glance tells them apart.</summary>
    public static Brush SpeakerBrush(SourceHint source) =>
        (Brush)Application.Current.Resources[source == SourceHint.Mic ? "AccentTextFillColorPrimaryBrush" : "TextFillColorPrimaryBrush"];

    public static string RecapTip(string? hotkey) =>
        "Write a recap of this call: summary, decisions, action items, fact checks" + (hotkey is null ? "" : $" ({hotkey})");

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
        ViewModel.Apply(new Core.Config.Settings { RepoPath = root, AudioInput = Core.Config.AudioInput.Both });   // call audio on: cards say who spoke
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
            [], 0.9, Link: new Uri("https://en.wikipedia.org/wiki/Python_(programming_language)"), Verdict: Core.Providers.Verdict.Contradicted, LinkChecked: true)], now);
        ViewModel.Focus?.RateUpCommand.Execute(null);   // the demo has no session log: the rating only shows on the card
        ViewModel.SetRatings(24, 2);
        ViewModel.MicLevel = 0.5;   // an ordinary speaking voice, so the meter is visible in the sample
        ViewModel.IsPinned = true;
    }

    /// <summary>
    /// Plays one effect and holds a frame of it, for screenshots: --demo --fx contradicted|supported|unverified|checking|
    /// voice|muted|shatter|milestone|constellation|materialize. Composition motion (springs, the answer sharpening) runs
    /// to its end, except in "materialize", which slows it down.
    /// </summary>
    public void Preview(string moment)
    {
        static Core.Providers.Insight Claim(string claim, string answer, Core.Providers.Verdict verdict, double confidence = 0.9) =>
            new(Core.Routing.Intent.FactCheck, $"[FACT] {claim}", answer, [], confidence,
                Link: new Uri($"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(claim.Split(' ')[1])}"), Verdict: verdict, LinkChecked: true);
        var now = DateTimeOffset.Now;
        switch (moment)
        {
            case "contradicted":
                ViewModel.Push([Claim("The Great Wall is visible from space", "❌ Contradicted — It is far too narrow to see with the naked eye from orbit.",
                    Core.Providers.Verdict.Contradicted)], now);
                Effects.FastForward(0.13, hold: true);
                break;
            case "supported":
                ViewModel.Push([Claim("Water boils at 100 °C at sea level", "✅ Supported — At standard atmospheric pressure (101.325 kPa), water boils at 100 °C.",
                    Core.Providers.Verdict.Supported, 0.95)], now, SourceHint.Teams);
                Effects.FastForward(0.42, hold: true);
                break;
            case "materialize":
                // composition time slowed to 8 %, so a capture a few seconds later lands mid-way through the answer sharpening
                Microsoft.UI.Xaml.Media.CompositionTarget.GetCompositorForCurrentThread().GlobalPlaybackRate = 0.08f;
                ViewModel.Push([Claim("Light from the Sun takes about eight minutes to reach Earth",
                    "✅ Supported — Sunlight takes about 8 minutes 20 seconds to cover the 150 million km.", Core.Providers.Verdict.Supported, 0.95)], now);
                Effects.FastForward(0.8, hold: true);
                break;
            case "unverified":
                ViewModel.Push([Claim("Octopuses dream in colour", "⚠️ Unverified — Octopuses change colour during sleep, but whether they dream is unknown.",
                    Core.Providers.Verdict.Unverified, 0.5)], now);
                Effects.FastForward(0.3, hold: true);
                break;
            case "checking":
                ViewModel.IsChecking = true;
                Effects.FastForward(1.15, hold: true);
                break;
            case "voice":
                ViewModel.MicLevel = 0.62;
                Effects.FastForward(0.8, hold: true);
                break;
            case "muted":
                ViewModel.ToggleMuteCommand.Execute(null);
                Effects.FastForward(1.0, hold: true);
                break;
            case "shatter":
                ViewModel.ToggleMuteCommand.Execute(null);
                Effects.FastForward(1.0, hold: false);
                ViewModel.ToggleMuteCommand.Execute(null);
                Effects.FastForward(0.2, hold: true);
                break;
            case "milestone":
                ViewModel.SetRatings(30, 2);
                Effects.FastForward(0.3, hold: true);
                break;
            case "constellation":
                (string, string, Core.Providers.Verdict)[] session =
                [
                    ("Python was released in 1991", "✅ Supported — Python 0.9.0 was released in February 1991.", Core.Providers.Verdict.Supported),
                    ("Python is named after a snake", "❌ Contradicted — It is named after Monty Python's Flying Circus.", Core.Providers.Verdict.Contradicted),
                    ("Guido van Rossum created Python", "✅ Supported — He began work on it in 1989.", Core.Providers.Verdict.Supported),
                    ("The Eiffel Tower is 330 metres tall", "✅ Supported — 330 m including antennas.", Core.Providers.Verdict.Supported),
                    ("The Eiffel Tower was built for the 1900 fair", "❌ Contradicted — It was built for the 1889 Exposition Universelle.", Core.Providers.Verdict.Contradicted),
                    ("Gustave Eiffel designed the Statue of Liberty frame", "✅ Supported — His company built its internal structure.", Core.Providers.Verdict.Supported),
                    ("Rust guarantees memory safety without garbage collection", "✅ Supported — Through ownership and borrowing.", Core.Providers.Verdict.Supported),
                    ("Rust was created at Google", "❌ Contradicted — It began as a Mozilla Research project.", Core.Providers.Verdict.Contradicted),
                    ("The tenant table has a soft delete column", "⚠️ Unverified — No such column in the schema snapshot.", Core.Providers.Verdict.Unverified),
                    ("Mozilla funded Rust development", "✅ Supported — Mozilla sponsored it from 2009.", Core.Providers.Verdict.Supported),
                ];
                foreach (var (claim, answer, verdict) in session) ViewModel.Push([Claim(claim, answer, verdict, verdict == Core.Providers.Verdict.Unverified ? 0.5 : 0.92)], now);
                ViewModel.IsConstellationOpen = true;
                break;
        }
    }
#endif
}

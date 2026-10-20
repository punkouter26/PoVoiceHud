using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PoVoiceHud.Core;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using WinUIEx;

namespace PoVoiceHud.App;

public sealed partial class MainWindow : WindowEx
{
    private static readonly Brush None = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    private bool _follow = true;  // the Live page is scrolled to the end, so new lines keep it there

    public MainWindow(HudViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        AppWindow.SetIcon(IconPath);
        // The header is the title bar: one 48 px row, dragged by the headline, with the caption buttons drawn over its right end.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragArea);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        Root.SizeChanged += (_, _) => KeepClearOfCaptionButtons();
        PageSwitch.SelectedItem = ViewModel.IsFactsOpen ? FactsTab : LiveTab;   // from the view model, not the markup, so a page chosen before load sticks
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HudViewModel.MicLevel)) ShowLevel(ViewModel.MicLevel);
            if (e.PropertyName == nameof(HudViewModel.IsFactsOpen)) PageSwitch.SelectedItem = ViewModel.IsFactsOpen ? FactsTab : LiveTab;
        };
        // New lines and verdicts make the list taller; while it is scrolled to the end it stays there. Scrolling up stops that.
        LiveScroll.ViewChanged += (_, e) =>
        {
            if (!e.IsIntermediate) _follow = LiveScroll.VerticalOffset >= LiveScroll.ScrollableHeight - 40;
        };
        LiveList.SizeChanged += (_, _) =>
        {
            if (_follow) LiveScroll.ChangeView(null, LiveScroll.ScrollableHeight, null, disableAnimation: true);
        };
    }

    public HudViewModel ViewModel { get; }

    /// <summary>The highlight fades in rather than appearing; off with "Sounds and animations" or Windows' animation switch.</summary>
    public bool Animations { get; set; } = true;

    /// <summary>The app icon, copied next to the exe: the window, the tray and the installer all use it.</summary>
    public static string IconPath { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "PoVoiceHud.ico");

    /// <summary>The caption buttons' width is in physical pixels and changes with the display's scale.</summary>
    private void KeepClearOfCaptionButtons()
    {
        if (Content.XamlRoot is { RasterizationScale: > 0 } root)
            CaptionSpace.Width = new GridLength(AppWindow.TitleBar.RightInset / root.RasterizationScale);
    }

    private void PageSwitch_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.IsFactsOpen = ReferenceEquals(sender.SelectedItem, FactsTab);

    /// <summary>The highlight colour of a line fades in when its verdict lands.</summary>
    private void Highlight_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is Border border) border.BackgroundTransition = Animations ? new BrushTransition { Duration = TimeSpan.FromMilliseconds(350) } : null;
    }

    private void CopyFacts_Click(object sender, RoutedEventArgs e)
    {
        var data = new DataPackage();
        data.SetText(ViewModel.FactsText);
        Clipboard.SetContent(data);
        CopyFactsText.Text = "Copied";
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => CopyFactsText.Text = "Copy facts";
        timer.Start();
    }

    // Level (0 = −60 dBFS, 1 = full scale) at which each of the five bars lights. Room noise sits near 0.15–0.2 and
    // ordinary speech around 0.4–0.6, so silence shows nothing, talking shows three or four bars, and five means loud.
    private static readonly double[] BarThresholds = [0.25, 0.34, 0.43, 0.52, 0.62];

    private void ShowLevel(double level)
    {
        for (var i = 0; i < BarThresholds.Length; i++)
            LevelBars.Children[i].Opacity = level >= BarThresholds[i] ? 1 : 0.2;
    }

    /// <summary>The Settings page, shown in place of Live and Facts while it is open or while setup is needed.</summary>
    public void AttachSettings(SettingsViewModel settings, Action quit) => SettingsHost.Content = new Views.SettingsView(settings) { Quit = quit };

    public static Visibility Not(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static string MicName(bool muted) => muted ? "Microphone is muted — unmute" : "Microphone is live — mute";

    public static string MicTip(string status, bool muted) => $"{status}\n{(muted ? "Click to unmute" : "Click to mute")}";

    /// <summary>One verdict's share of the bar along the header's bottom edge; with no verdicts yet the bar is empty.</summary>
    public static GridLength Share(int count) => new(count, GridUnitType.Star);

    public static Brush LineBack(LineState state) => state switch
    {
        LineState.Correct => Theme("SystemFillColorSuccessBackgroundBrush"),
        LineState.Incorrect => Theme("SystemFillColorCriticalBackgroundBrush"),
        LineState.Checking => Theme("SubtleFillColorSecondaryBrush"),
        _ => None,   // "can't tell" gets a note, not a block of colour: it is not a finding
    };

    /// <summary>Claims in the ordinary text colour; everything that is not one, dimmed, so the claims stand out.</summary>
    public static Brush LineText(LineState state) => Theme(state == LineState.Heard ? "TextFillColorTertiaryBrush" : "TextFillColorPrimaryBrush");

    public static Brush LineFore(LineState state) => state switch
    {
        LineState.Correct => Theme("SystemFillColorSuccessBrush"),
        LineState.Incorrect => Theme("SystemFillColorCriticalBrush"),
        LineState.CantTell => Theme("SystemFillColorCautionBrush"),
        _ => Theme("TextFillColorSecondaryBrush"),
    };

    public static string LineGlyph(LineState state) => state switch
    {
        LineState.Correct => "\uE73E",     // check mark
        LineState.Incorrect => "\uE711",   // cross
        LineState.CantTell => "\uE7BA",    // warning
        _ => "\uE895",                     // the sync arrows: being checked
    };

    public static Brush FactBack(bool correct) => LineBack(correct ? LineState.Correct : LineState.Incorrect);

    public static Brush FactFore(bool correct) => LineFore(correct ? LineState.Correct : LineState.Incorrect);

    public static string FactGlyph(bool correct) => LineGlyph(correct ? LineState.Correct : LineState.Incorrect);

    private static Brush Theme(string key) => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush ? brush : None;

#if DEBUG
    /// <summary>A conversation with every kind of line, for layout checks: run a Debug build with --demo (--facts for the Facts page).</summary>
    public void SeedDemo()
    {
        var now = DateTimeOffset.Now;
        ViewModel.Apply(new Core.Config.Settings { ListenToCall = true });   // call audio on: lines say who spoke
        ViewModel.SetStatus("Listening to you and the call");
        (SourceHint Source, int At, string Text, Core.Providers.Verdict? Verdict, string Reason)[] said =
        [
            (SourceHint.Teams, 241, "Okay, let's get started with the roadmap review.", null, ""),
            (SourceHint.Mic, 246, "The Eiffel Tower was completed in 1889.", Core.Providers.Verdict.Supported, "It was finished in March 1889 for the Exposition Universelle."),
            (SourceHint.Teams, 250, "Right, and I think we should keep that example in the deck.", null, ""),
            (SourceHint.Mic, 255, "Python was created by Dennis Ritchie.", Core.Providers.Verdict.Contradicted, "Guido van Rossum created Python; Dennis Ritchie created C."),
            (SourceHint.Teams, 262, "Octopuses dream in colour, apparently.", Core.Providers.Verdict.Unverified, "They change colour in their sleep, but whether they dream is unknown."),
            (SourceHint.Mic, 268, "Light from the Sun takes about eight minutes to reach Earth.", null, "still with the checker"),
        ];
        foreach (var (source, at, text, verdict, reason) in said)
        {
            var line = new TranscriptLine(source, now.AddSeconds(at - 270), text);
            ViewModel.AddTranscript(line);
            if (reason.Length == 0) continue;
            ViewModel.MarkChecking(line, busy: true);
            if (verdict is not { } v) continue;
            var link = v == Core.Providers.Verdict.Unverified ? null
                : new Uri($"https://en.wikipedia.org/wiki/{(v == Core.Providers.Verdict.Supported ? "Eiffel_Tower" : "Python_(programming_language)")}");
            ViewModel.Push(new Checked(line, new Core.Providers.Insight(text.TrimEnd('.'), v, reason, 0.9, link, LinkChecked: true)));
            ViewModel.MarkChecking(line, busy: false);
        }
        ViewModel.MicLevel = 0.5;   // an ordinary speaking voice, so the meter is visible in the sample
    }
#endif
}

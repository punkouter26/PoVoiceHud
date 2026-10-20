using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers;

namespace PoVoiceHud.Core.ViewModels;

/// <summary>
/// The window: a header and one of three pages, Live (every line heard, highlighted by its verdict), Facts (only the
/// claims found correct or incorrect) or Settings. Until a working endpoint and key are set (<see cref="NeedsSetup"/>)
/// only Settings is shown. The header's headline shows whichever happened last: a verdict or a status change.
/// </summary>
public sealed partial class HudViewModel(Func<Uri, Task> openUri) : ObservableObject
{
    public const int LiveSize = 60;
    public const int FactsSize = 200;

    private readonly HashSet<TranscriptLine> _inFlight = [];
    private string _lastStatus = "Starting…";
    private bool _showSpeaker;

    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private string _headline = "Starting…";
    [ObservableProperty] private string _statusText = "Starting…";
    [ObservableProperty] private double _micLevel;
    /// <summary>Who the meter is hearing: the microphone (you) or the call.</summary>
    [ObservableProperty] private SourceHint _levelSource = SourceHint.Mic;
    /// <summary>Facts rather than Live is the chosen page.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowsLive), nameof(ShowsFacts))] private bool _isFactsOpen;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowsSettings), nameof(ShowsPages), nameof(ShowsLive), nameof(ShowsFacts))]
    private bool _isSettingsOpen;
    /// <summary>No working endpoint and key yet: the app shows Settings and listens to nothing until there is.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowsSettings), nameof(ShowsPages), nameof(ShowsLive), nameof(ShowsFacts))]
    private bool _needsSetup;
    // This session's verdicts, for the share bar along the bottom of the header.
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TallySummary))] private int _supported;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TallySummary))] private int _contradicted;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TallySummary))] private int _unverified;

    public bool ShowsSettings => IsSettingsOpen || NeedsSetup;
    /// <summary>The Live and Facts pages and the switch between them.</summary>
    public bool ShowsPages => !ShowsSettings;
    public bool ShowsLive => ShowsPages && !IsFactsOpen;
    public bool ShowsFacts => ShowsPages && IsFactsOpen;
    public bool HasLive => Live.Count > 0;
    public bool NoLive => Live.Count == 0;
    public bool HasFacts => Facts.Count > 0;
    public bool NoFacts => Facts.Count == 0;

    /// <summary>"3 correct · 1 incorrect"; the Facts page heading and the headline's tooltip.</summary>
    public string FactsSummary => $"{Facts.Count(f => f.IsCorrect)} correct · {Facts.Count(f => !f.IsCorrect)} incorrect";

    /// <summary>What the share bar along the header's bottom edge shows: "3 correct · 1 incorrect · 2 can't tell" this session.</summary>
    public string TallySummary => Supported + Contradicted + Unverified == 0
        ? "No verdicts yet"
        : $"{Supported} {Verdict.Supported.Label().ToLowerInvariant()} · {Contradicted} {Verdict.Contradicted.Label().ToLowerInvariant()} · " +
          $"{Unverified} {Verdict.Unverified.Label().ToLowerInvariant()}";

    /// <summary>Every line heard, oldest first.</summary>
    public ObservableCollection<LiveLineViewModel> Live { get; } = [];

    /// <summary>Claims found correct or incorrect, newest first. "Can't tell" stays on the Live page only.</summary>
    public ObservableCollection<FactViewModel> Facts { get; } = [];

    /// <summary>A verdict came in (for its sound).</summary>
    public event Action<Insight>? Arrived;

    /// <summary>Picks up the settings that can change while the HUD is running.</summary>
    public void Apply(Settings settings) => _showSpeaker = settings.ListenToCall;   // with the microphone alone, everything is yours

    public void AddTranscript(TranscriptLine line)
    {
        Live.Add(new LiveLineViewModel(line, _showSpeaker));
        while (Live.Count > LiveSize) Live.RemoveAt(0);
        OnPropertyChanged(nameof(HasLive));
        OnPropertyChanged(nameof(NoLive));
    }

    /// <summary>A line went to the fact checker (busy) or came back from it.</summary>
    public void MarkChecking(TranscriptLine line, bool busy)
    {
        if (busy) _inFlight.Add(line);
        else _inFlight.Remove(line);
        IsChecking = _inFlight.Count > 0;
        if (Find(line) is not { } live) return;
        if (busy && live.State == LineState.Heard) live.State = LineState.Checking;
        else if (!busy && live.State == LineState.Checking) live.State = LineState.Heard;   // not a claim after all
    }

    /// <summary>The pipeline was replaced (settings changed): nothing is with the old one's fact checker any more.</summary>
    public void ResetChecking()
    {
        _inFlight.Clear();
        IsChecking = false;
        foreach (var line in Live.Where(l => l.State == LineState.Checking)) line.State = LineState.Heard;
    }

    public void Push(Checked result)
    {
        var (line, verdict) = result;
        Find(line)?.Judge(verdict);
        Tally(verdict.Verdict);
        if (verdict.Verdict != Verdict.Unverified)
        {
            AddFact(verdict, line.At, line.Source);
            Headline = $"{verdict.Verdict.Emoji()} {verdict.Claim}";   // "can't tell" stays a note on its line
        }
        Arrived?.Invoke(verdict);
    }

    /// <summary>"Copy facts": one line per fact, newest first.</summary>
    public string FactsText => string.Join(Environment.NewLine, Facts.Select(f => f.CopyLine));

    private void AddFact(Insight verdict, DateTimeOffset at, SourceHint source)
    {
        // The same claim said again is one row with a count, moved to the top.
        if (Facts.FirstOrDefault(f => f.IsSameAs(verdict) && f.Verdict == verdict.Verdict) is { } earlier)
        {
            Facts.Remove(earlier);
            earlier.Repeat(at);
            Facts.Insert(0, earlier);
        }
        else
        {
            Facts.Insert(0, new FactViewModel(verdict, at, source, _showSpeaker, openUri));
            while (Facts.Count > FactsSize) Facts.RemoveAt(Facts.Count - 1);
        }
        OnPropertyChanged(nameof(HasFacts));
        OnPropertyChanged(nameof(NoFacts));
        OnPropertyChanged(nameof(FactsSummary));
    }

    private void Tally(Verdict verdict)
    {
        if (verdict == Verdict.Supported) Supported++;
        else if (verdict == Verdict.Contradicted) Contradicted++;
        else Unverified++;
    }

    // Lines are records, so a verdict finds its line even if it was copied on the way; the newest match wins.
    private LiveLineViewModel? Find(TranscriptLine line) => Live.LastOrDefault(l => l.Line == line);

    public void SetStatus(string status)
    {
        _lastStatus = status;
        ShowStatus();
    }

    private void ShowStatus() => Headline = StatusText = IsMuted ? "Muted" : _lastStatus;

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        ShowStatus();
    }

    /// <summary>The ⚙ button: opens Settings, or leaves them (discarding what was not applied). Not while setup is needed.</summary>
    [RelayCommand]
    private void ToggleSettings()
    {
        if (!NeedsSetup) IsSettingsOpen = !IsSettingsOpen;
    }
}

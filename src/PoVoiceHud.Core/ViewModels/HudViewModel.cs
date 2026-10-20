using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers;

namespace PoVoiceHud.Core.ViewModels;

/// <summary>
/// Pill-and-expand state (SPEC §5a). No timers here: the window calls <see cref="Tick"/> once a second.
/// The pill's headline shows whichever happened last: a card arriving or a status change.
/// </summary>
public sealed partial class HudViewModel(TimeSpan autoExpand, string? repoRoot, Func<Uri, Task> openUri) : ObservableObject
{
    public const int ShelfSize = 6;
    public const int TranscriptSize = 8;
    private const double ExpandConfidence = 0.6;
    // Muting after at least this many sentences suggests a recap; fewer is not a conversation worth one.
    private const int RecapWorthy = 6;
    // Cards kept for the constellation; an all-day session would otherwise grow without bound.
    private const int SessionSize = 120;
    // Every this many 👍 in a week is a milestone.
    private const int MilestoneEvery = 10;

    // Fact-check verdicts this session, kept apart for what you said (mic) and what the call said.
    private readonly Dictionary<(bool Mine, Verdict Verdict), int> _counts = [];
    private DateTimeOffset _expandedUntil;
    private string _lastStatus = "Starting…";
    private bool _showSpeaker;
    private int _heardSinceRecap;
    private string _verdictDetail = "";
    private string _ratingDetail = "";
    private int _weeklyUps = -1;   // unknown until the first SetRatings, which must not count as a milestone
    private readonly List<InsightCardViewModel> _session = [];

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private bool _isPointerOver;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private string _headline = "Starting…";
    [ObservableProperty] private string _statusText = "Starting…";
    [ObservableProperty] private string _muteHint = "Mute: Win+Shift+M";
    [ObservableProperty] private string _tally = "";
    [ObservableProperty] private string _tallyDetail = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasFocus), nameof(NoFocus))] private InsightCardViewModel? _focus;
    [ObservableProperty] private double _micLevel;
    /// <summary>Who the meter is hearing: the microphone (you) or the call.</summary>
    [ObservableProperty] private SourceHint _levelSource = SourceHint.Mic;
    [ObservableProperty] private bool _isConstellationOpen;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsTranscriptClosed))] private bool _isTranscriptOpen;
    [ObservableProperty] private TranscriptLine? _lastLine;
    /// <summary>The live recap hotkey, which the muted pill suggests after a conversation; null when none could be registered.</summary>
    [ObservableProperty] private string? _recapHotkey;

    public bool HasFocus => Focus is not null;
    public bool NoFocus => Focus is null;
    public bool HasShelf => Shelf.Count > 0;
    public bool IsTranscriptClosed => !IsTranscriptOpen;

    /// <summary>Earlier cards, newest first: the history list under the focus card.</summary>
    public ObservableCollection<InsightCardViewModel> Shelf { get; } = [];
    public ObservableCollection<TranscriptLine> Transcript { get; } = [];

    /// <summary>A card was given 👍 or 👎 (or had it taken back).</summary>
    public event Action<InsightCardViewModel>? Rated;

    /// <summary>The recap button or hotkey: the app writes the recap and calls <see cref="RecapDone"/>.</summary>
    public event Action? RecapRequested;

    /// <summary>A new card became the focus card (not one brought back from the history).</summary>
    public event Action<InsightCardViewModel>? Arrived;

    /// <summary>The week's 👍 count reached another multiple of ten; carries the count.</summary>
    public event Action<int>? Milestone;

    /// <summary>This session's cards, oldest first (recaps left out): the stars of the constellation.</summary>
    public IReadOnlyList<InsightCardViewModel> Session => _session;

    /// <summary>Picks up the settings that can change while the HUD is running.</summary>
    public void Apply(Settings settings)
    {
        autoExpand = TimeSpan.FromSeconds(settings.AutoExpandSeconds);
        repoRoot = settings.RepoPath;
        _showSpeaker = settings.AudioInput != AudioInput.Mic;   // with the microphone alone, everything is yours
    }

    public void Push(IReadOnlyList<Insight> insights, DateTimeOffset now, SourceHint source = SourceHint.Mic)
    {
        if (insights.Count == 0) return;
        Record(insights, source == SourceHint.Mic);

        // The same thing said again is one card with a count, not a second card.
        var previous = Focus;
        var primary = Card(insights[0], source, ref previous);
        if (previous is not null) Shelf.Insert(0, previous);
        foreach (var extra in insights.Skip(1).Reverse())
        {
            InsightCardViewModel? none = null;
            Shelf.Insert(0, Card(extra, source, ref none));
        }
        while (Shelf.Count > ShelfSize) Shelf.RemoveAt(Shelf.Count - 1);
        OnPropertyChanged(nameof(HasShelf));

        foreach (var card in Shelf.Take(insights.Count - 1).Reverse().Append(primary))
            Remember(card);

        Focus = primary;
        Headline = primary.Summary;
        Arrived?.Invoke(primary);
        if (insights[0].Confidence >= ExpandConfidence)
        {
            _expandedUntil = now + autoExpand;
            IsExpanded = true;
        }
    }

    /// <summary>
    /// A card for <paramref name="insight"/>, counting the earlier one it repeats: that one is taken out of the
    /// history, or — when it is the current focus card — <paramref name="focus"/> is cleared so it is replaced in place.
    /// </summary>
    private InsightCardViewModel Card(Insight insight, SourceHint source, ref InsightCardViewModel? focus)
    {
        var count = 1;
        if (focus is not null && focus.IsSameAs(insight))
        {
            count += focus.Count;
            focus = null;
        }
        else if (Shelf.FirstOrDefault(c => c.IsSameAs(insight)) is { } earlier)
        {
            count += earlier.Count;
            Shelf.Remove(earlier);
        }
        return new InsightCardViewModel(insight, repoRoot, openUri, count, source, _showSpeaker && source != SourceHint.Unknown,
            card => Rated?.Invoke(card), Dismiss);
    }

    /// <summary>Keeps <paramref name="card"/> for the constellation, in place of an earlier card saying the same thing.</summary>
    private void Remember(InsightCardViewModel card)
    {
        if (card.Insight.Intent == Routing.Intent.None) return;
        _session.RemoveAll(c => c.IsSameAs(card.Insight));
        _session.Add(card);
        if (_session.Count > SessionSize) _session.RemoveAt(0);
    }

    private void Record(IReadOnlyList<Insight> insights, bool mine)
    {
        foreach (var verdict in insights.Select(i => i.Verdict).OfType<Verdict>())
            _counts[(mine, verdict)] = _counts.GetValueOrDefault((mine, verdict)) + 1;
        if (_counts.Count == 0) return;

        string Line(Func<bool, bool> who) => string.Join("  ", Enum.GetValues<Verdict>()
            .Select(v => (Verdict: v, Count: _counts.Where(t => t.Key.Verdict == v && who(t.Key.Mine)).Sum(t => t.Value)))
            .Where(v => v.Count > 0)
            .Select(v => $"{v.Verdict.Emoji()} {v.Count}"));

        Tally = Line(_ => true);
        _verdictDetail = $"This session — you: {Or(Line(m => m))} · the call: {Or(Line(m => !m))}";
        ShowTallyDetail();
    }

    private static string Or(string line) => line.Length == 0 ? "none" : line;

    /// <summary>Your 👍/👎 over the last week, under the session tally: how often the HUD has been right, by your own account.</summary>
    public void SetRatings(int up, int down)
    {
        _ratingDetail = up + down == 0 ? "" : $"Your ratings, last 7 days: 👍 {up}  👎 {down} ({100 * up / (up + down)}% right)";
        ShowTallyDetail();
        if (_weeklyUps >= 0 && up / MilestoneEvery > _weeklyUps / MilestoneEvery) Milestone?.Invoke(up);
        _weeklyUps = up;
    }

    private void ShowTallyDetail() => TallyDetail = string.Join("\n", new[] { _verdictDetail, _ratingDetail }.Where(s => s.Length > 0));

    public void AddTranscript(TranscriptLine line)
    {
        Transcript.Add(line);
        while (Transcript.Count > TranscriptSize) Transcript.RemoveAt(0);
        LastLine = line;
        _heardSinceRecap++;
    }

    /// <summary>A recap was written: the muted pill stops suggesting one until there is a new conversation.</summary>
    public void RecapDone()
    {
        _heardSinceRecap = 0;
        ShowStatus();
        if (_session.Count >= 3) IsConstellationOpen = true;   // the end of a conversation: show the whole of it at a glance
    }

    public void SetStatus(string status)
    {
        _lastStatus = status;
        ShowStatus();
    }

    public void Tick(DateTimeOffset now)
    {
        if (IsExpanded && !IsPinned && !IsPointerOver && now >= _expandedUntil) IsExpanded = false;
    }

    private void ShowStatus() => Headline = StatusText = !IsMuted ? _lastStatus
        : _heardSinceRecap >= RecapWorthy && RecapHotkey is { } key ? $"Muted · recap this call? {key}"
        : "Muted";

    /// <summary>Puts the focus card away; the newest card in the history takes its place.</summary>
    private void Dismiss(InsightCardViewModel card)
    {
        if (!ReferenceEquals(card, Focus)) return;
        Focus = Shelf.FirstOrDefault();
        if (Focus is not null) Shelf.RemoveAt(0);
        OnPropertyChanged(nameof(HasShelf));
        Headline = Focus?.Summary ?? StatusText;
    }

    [RelayCommand]
    private void Recap() => RecapRequested?.Invoke();

    /// <summary>Brings a card from the history back as the focus card; the one it replaces takes its place at the top of the history.</summary>
    [RelayCommand]
    private void Show(InsightCardViewModel card)
    {
        if (!Shelf.Remove(card)) return;
        if (Focus is not null) Shelf.Insert(0, Focus);
        Focus = card;
        Headline = card.Summary;
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        IsPinned = !IsPinned;
        IsExpanded = IsPinned;
    }

    [RelayCommand]
    private void Collapse() => IsPinned = IsExpanded = false;

    [RelayCommand]
    private void ToggleTranscript() => IsTranscriptOpen = !IsTranscriptOpen;

    [RelayCommand]
    private void ToggleConstellation() => IsConstellationOpen = !IsConstellationOpen;

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        ShowStatus();
    }
}

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

    // Fact-check verdicts this session, kept apart for what you said (mic) and what the call said.
    private readonly Dictionary<(bool Mine, Verdict Verdict), int> _counts = [];
    private DateTimeOffset _expandedUntil;
    private string _lastStatus = "Starting…";

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
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsTranscriptClosed))] private bool _isTranscriptOpen;
    [ObservableProperty] private TranscriptLine? _lastLine;

    public bool HasFocus => Focus is not null;
    public bool NoFocus => Focus is null;
    public bool HasShelf => Shelf.Count > 0;
    public bool IsTranscriptClosed => !IsTranscriptOpen;

    /// <summary>Earlier cards, newest first: the history list under the focus card.</summary>
    public ObservableCollection<InsightCardViewModel> Shelf { get; } = [];
    public ObservableCollection<TranscriptLine> Transcript { get; } = [];

    /// <summary>Picks up the settings that can change while the HUD is running.</summary>
    public void Apply(Settings settings)
    {
        autoExpand = TimeSpan.FromSeconds(settings.AutoExpandSeconds);
        repoRoot = settings.RepoPath;
    }

    public void Push(IReadOnlyList<Insight> insights, DateTimeOffset now, SourceHint source = SourceHint.Mic)
    {
        if (insights.Count == 0) return;
        Record(insights, source == SourceHint.Mic);

        // The same thing said again is one card with a count, not a second card.
        var previous = Focus;
        var primary = Card(insights[0], ref previous);
        if (previous is not null) Shelf.Insert(0, previous);
        foreach (var extra in insights.Skip(1).Reverse())
        {
            InsightCardViewModel? none = null;
            Shelf.Insert(0, Card(extra, ref none));
        }
        while (Shelf.Count > ShelfSize) Shelf.RemoveAt(Shelf.Count - 1);
        OnPropertyChanged(nameof(HasShelf));

        Focus = primary;
        Headline = primary.Summary;
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
    private InsightCardViewModel Card(Insight insight, ref InsightCardViewModel? focus)
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
        return new InsightCardViewModel(insight, repoRoot, openUri, count);
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
        TallyDetail = $"This session — you: {Or(Line(m => m))} · the call: {Or(Line(m => !m))}";
    }

    private static string Or(string line) => line.Length == 0 ? "none" : line;

    public void AddTranscript(TranscriptLine line)
    {
        Transcript.Add(line);
        while (Transcript.Count > TranscriptSize) Transcript.RemoveAt(0);
        LastLine = line;
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

    private void ShowStatus() => Headline = StatusText = IsMuted ? "Muted" : _lastStatus;

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
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        ShowStatus();
    }
}

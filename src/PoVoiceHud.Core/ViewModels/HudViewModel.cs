using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoVoiceHud.Core.Providers;

namespace PoVoiceHud.Core.ViewModels;

/// <summary>
/// Pill-and-expand state (SPEC §5a). No timers here: the window calls <see cref="Tick"/> once a second.
/// </summary>
public sealed partial class HudViewModel(TimeSpan autoExpand, string? repoRoot, Func<Uri, Task> openUri) : ObservableObject
{
    public const int ShelfSize = 6;
    public const int TranscriptSize = 8;
    private const double ExpandConfidence = 0.6;

    private DateTimeOffset _expandedUntil;
    private string _lastStatus = "Starting…";

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private bool _isPointerOver;
    [ObservableProperty] private string _headline = "PoVoiceHud — listening";
    [ObservableProperty] private string _statusText = "Starting…";
    [ObservableProperty] private string _muteHint = "Mute: Win+Shift+M";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasFocus), nameof(NoFocus))] private InsightCardViewModel? _focus;
    [ObservableProperty] private double _micLevel;

    public bool HasFocus => Focus is not null;
    public bool NoFocus => Focus is null;

    public ObservableCollection<InsightCardViewModel> Shelf { get; } = [];
    public ObservableCollection<TranscriptLine> Transcript { get; } = [];
    public string RepoLabel => repoRoot ?? "No repo selected";

    public void Push(IReadOnlyList<Insight> insights, DateTimeOffset now)
    {
        if (insights.Count == 0) return;
        if (Focus is not null) Shelf.Insert(0, Focus);
        foreach (var extra in insights.Skip(1).Reverse())
            Shelf.Insert(0, new InsightCardViewModel(extra, repoRoot, openUri));
        while (Shelf.Count > ShelfSize) Shelf.RemoveAt(Shelf.Count - 1);

        Focus = new InsightCardViewModel(insights[0], repoRoot, openUri);
        Headline = insights[0].Headline;
        if (insights[0].Confidence >= ExpandConfidence)
        {
            _expandedUntil = now + autoExpand;
            IsExpanded = true;
        }
    }

    public void AddTranscript(TranscriptLine line)
    {
        Transcript.Add(line);
        while (Transcript.Count > TranscriptSize) Transcript.RemoveAt(0);
    }

    public void SetStatus(string status)
    {
        _lastStatus = status;
        StatusText = IsMuted ? "Muted" : status;
    }

    public void Tick(DateTimeOffset now)
    {
        if (IsExpanded && !IsPinned && !IsPointerOver && now >= _expandedUntil) IsExpanded = false;
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        IsPinned = !IsPinned;
        IsExpanded = IsPinned;
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        StatusText = IsMuted ? "Muted" : _lastStatus;
    }
}

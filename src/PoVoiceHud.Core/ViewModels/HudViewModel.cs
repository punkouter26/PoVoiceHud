using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers;

namespace PoVoiceHud.Core.ViewModels;

/// <summary>The page the switch at the top has chosen.</summary>
public enum HudPage { Live, Facts, Code }

/// <summary>
/// The window: a header and one of four pages, Live (every line heard, highlighted by its verdict or answer), Facts (the
/// claims found correct or incorrect), Code facts (questions about the code repository answered, and statements about it
/// judged) or Settings. Until a working endpoint and key are set (<see cref="NeedsSetup"/>) only Settings is shown. The
/// header's headline shows whichever happened last: a verdict, an answer or a status change.
/// </summary>
public sealed partial class HudViewModel(Func<Uri, Task> openUri) : ObservableObject
{
    public const int LiveSize = 60;
    public const int FactsSize = 200;

    private readonly HashSet<TranscriptLine> _inFlight = [];
    private string _lastStatus = "Starting…";

    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private string _headline = "Starting…";
    [ObservableProperty] private string _statusText = "Starting…";
    [ObservableProperty] private double _micLevel;
    /// <summary>Who the meter is hearing: the microphone (you) or the call.</summary>
    [ObservableProperty] private SourceHint _levelSource = SourceHint.Mic;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowsLive), nameof(ShowsFacts), nameof(ShowsCode), nameof(ShowsList), nameof(CanCopy))]
    private HudPage _page;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowsSettings), nameof(ShowsPages), nameof(ShowsLive), nameof(ShowsFacts), nameof(ShowsCode), nameof(ShowsList))]
    private bool _isSettingsOpen;
    /// <summary>No working endpoint and key yet: the app shows Settings and listens to nothing until there is.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowsSettings), nameof(ShowsPages), nameof(ShowsLive), nameof(ShowsFacts), nameof(ShowsCode), nameof(ShowsList))]
    private bool _needsSetup;
    /// <summary>A code repository is connected, so sentences about it are looked up (the Code facts page says how, otherwise).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(NoCodeHint))] private bool _hasRepository;
    // This session's verdicts, for the share bar along the bottom of the header.
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TallySummary))] private int _supported;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TallySummary))] private int _contradicted;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TallySummary))] private int _unverified;

    public bool ShowsSettings => IsSettingsOpen || NeedsSetup;
    /// <summary>The Live and Facts pages and the switch between them.</summary>
    public bool ShowsPages => !ShowsSettings;
    public bool ShowsLive => ShowsPages && Page == HudPage.Live;
    public bool ShowsFacts => ShowsPages && Page == HudPage.Facts;
    public bool ShowsCode => ShowsPages && Page == HudPage.Code;
    /// <summary>Facts or Code facts: a list with a count and Copy.</summary>
    public bool ShowsList => ShowsFacts || ShowsCode;
    public bool HasLive => Live.Count > 0;
    public bool NoLive => Live.Count == 0;
    public bool HasFacts => Facts.Count > 0;
    public bool NoFacts => Facts.Count == 0;
    public bool HasCodeFacts => CodeFacts.Count > 0;
    public bool NoCodeFacts => CodeFacts.Count == 0;

    /// <summary>What the empty Code facts page says: how to ask, or that a repository has to be connected first.</summary>
    public string NoCodeHint => HasRepository
        ? "No code facts yet. Say the repository's name, a file or class in it, or words like \"our code\", \"the repo\" or \"our website\": questions are answered from the code and statements checked against it."
        : "Connect a code repository in Settings (a GitHub or Azure DevOps URL, or a folder), and questions and statements about it are answered and checked here.";

    public bool CanCopy => Page == HudPage.Code ? HasCodeFacts : HasFacts;

    /// <summary>"2 answers · 1 checked": the Code facts page heading.</summary>
    public string CodeSummary => $"{CodeFacts.Count(f => f.IsAnswer)} {(CodeFacts.Count(f => f.IsAnswer) == 1 ? "answer" : "answers")} · {CodeFacts.Count(f => !f.IsAnswer)} checked";

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

    /// <summary>Questions about the code answered and statements about it judged, newest first.</summary>
    public ObservableCollection<CodeFactViewModel> CodeFacts { get; } = [];

    /// <summary>A verdict came in (for its sound); an answer from the code comes as "can't tell", the quiet cue.</summary>
    public event Action<Insight>? Arrived;

    public void AddTranscript(TranscriptLine line)
    {
        Live.Add(new LiveLineViewModel(line));
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

    /// <summary>
    /// What the code says about a line: the line takes the answer or verdict, and the Code facts page gets a row, except for
    /// "can't tell", which stays a quiet note on its line as on Facts.
    /// </summary>
    public void PushCode(CodeChecked result)
    {
        var (line, fact) = result;
        Find(line)?.Judge(fact);
        if (fact.Verdict is { } verdict) Tally(verdict);
        if (fact.Verdict == Verdict.Unverified)
        {
            Arrived?.Invoke(new Insight(fact.Topic, Verdict.Unverified, fact.Text, 0.6));
            return;
        }
        CodeFacts.Insert(0, new CodeFactViewModel(fact, line.At, line.Source, openUri));
        while (CodeFacts.Count > FactsSize) CodeFacts.RemoveAt(CodeFacts.Count - 1);
        Headline = $"{(fact.Verdict is { } v ? v.Emoji() : "💬")} {fact.Topic}";
        OnPropertyChanged(nameof(HasCodeFacts));
        OnPropertyChanged(nameof(NoCodeFacts));
        OnPropertyChanged(nameof(CodeSummary));
        OnPropertyChanged(nameof(CanCopy));
        Arrived?.Invoke(new Insight(fact.Topic, fact.Verdict ?? Verdict.Unverified, fact.Text, 0.9));
    }

    /// <summary>Copy: one line per fact on the page on show, newest first.</summary>
    public string CopyText => Page == HudPage.Code
        ? string.Join(Environment.NewLine, CodeFacts.Select(f => f.CopyLine))
        : string.Join(Environment.NewLine, Facts.Select(f => f.CopyLine));

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
            Facts.Insert(0, new FactViewModel(verdict, at, source, openUri));
            while (Facts.Count > FactsSize) Facts.RemoveAt(Facts.Count - 1);
        }
        OnPropertyChanged(nameof(HasFacts));
        OnPropertyChanged(nameof(NoFacts));
        OnPropertyChanged(nameof(FactsSummary));
        OnPropertyChanged(nameof(CanCopy));
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

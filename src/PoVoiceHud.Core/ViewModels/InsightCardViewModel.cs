using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.ViewModels;

public sealed partial class JumpItem(Jump jump, string label, Func<Uri, Task> openUri)
{
    public string Label => label;

    [RelayCommand]
    private Task Open() => openUri(jump.Uri);
}

/// <summary>Your verdict on a card: was it right?</summary>
public enum Rating { None, Up, Down }

/// <summary>
/// One card. <see cref="Summary"/> is its single line (the pill and the history list show it);
/// the card itself then only needs the answer and the links. Jumps are labelled relative to the repo root.
/// </summary>
public sealed partial class InsightCardViewModel : ObservableObject
{
    private readonly Insight _insight;
    private readonly Action<InsightCardViewModel>? _rated;
    private readonly Action<InsightCardViewModel>? _dismissed;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsUp), nameof(IsDown))] private Rating _rating;

    /// <param name="count">How many times this same thing has come up, when it repeats an earlier card.</param>
    /// <param name="showSpeaker">Label the card with who said it; pointless when only the microphone is heard.</param>
    /// <param name="rated">Called after 👍 or 👎 changes <see cref="Rating"/>.</param>
    /// <param name="dismissed">Called when the card is put away.</param>
    public InsightCardViewModel(Insight insight, string? repoRoot, Func<Uri, Task> openUri, int count = 1,
        SourceHint source = SourceHint.Mic, bool showSpeaker = false, Action<InsightCardViewModel>? rated = null, Action<InsightCardViewModel>? dismissed = null)
    {
        _insight = insight;
        _rated = rated;
        _dismissed = dismissed;
        Count = count;
        Source = source;
        ShowSpeaker = showSpeaker;
        var close = insight.Headline.IndexOf(']');
        (Tag, Title) = insight.Headline.StartsWith('[') && close > 0
            ? (insight.Headline[1..close], insight.Headline[(close + 1)..].Trim())
            : ("", insight.Headline);
        Jumps = insight.Jumps.Select(j => new JumpItem(j, $"{Relative(j.AbsolutePath, repoRoot)}:{j.Line}", openUri)).ToList();
        OpenLinkCommand = new AsyncRelayCommand(() => insight.Link is null ? Task.CompletedTask : openUri(insight.Link));
    }

    public Insight Insight => _insight;
    public string Tag { get; }
    public string Title { get; }
    public int Count { get; }
    public SourceHint Source { get; }
    public bool ShowSpeaker { get; }
    public string Speaker => Source.Speaker();
    public string Answer => _insight.Answer;
    public Verdict? Verdict => _insight.Verdict;
    public string? Stale => _insight.Stale;
    public bool IsStale => _insight.Stale is not null;
    public bool HasLink => _insight.Link is not null;
    public bool HasJumps => Jumps.Count > 0;
    public bool IsUp => Rating == Rating.Up;
    public bool IsDown => Rating == Rating.Down;

    /// <summary>"❌ Python was created by Dennis Ritchie ×2" for a verdict, "[SCHEMA] Inspection.TenantId" for the other sources.</summary>
    public string Summary => (Verdict is { } verdict ? $"{verdict.Emoji()} {Title}" : _insight.Headline) + (Count > 1 ? $" ×{Count}" : "");

    /// <summary>The same sentence or subject coming up again (headlines are compared, ignoring case).</summary>
    public bool IsSameAs(Insight other) => string.Equals(_insight.Headline, other.Headline, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Shows where the link goes. A page the app looked up itself is a checked source; a link the model wrote can be
    /// invented, so it is labelled as the model's citation. A local file (a recap) shows its name.
    /// </summary>
    public string LinkLabel => _insight.Link is not { } link ? ""
        : link.IsFile ? $"{Path.GetFileName(link.LocalPath)} ↗"
        : _insight.LinkChecked ? $"{link.Host} ↗ (looked up)"
        : _insight.Intent == Intent.FactCheck ? $"{link.Host} ↗ (cited by the model)"
        : $"{link.Host} ↗";

    public string CopyText => $"{_insight.Headline}\n{_insight.Answer}{(_insight.Link is { } link ? $"\n{link}" : "")}";
    public IReadOnlyList<JumpItem> Jumps { get; }
    public IAsyncRelayCommand OpenLinkCommand { get; }

    /// <summary>👍: pressing it again takes the rating back.</summary>
    [RelayCommand]
    private void RateUp() => Rate(Rating == Rating.Up ? Rating.None : Rating.Up);

    [RelayCommand]
    private void RateDown() => Rate(Rating == Rating.Down ? Rating.None : Rating.Down);

    private void Rate(Rating rating)
    {
        Rating = rating;
        _rated?.Invoke(this);
    }

    [RelayCommand]
    private void Dismiss() => _dismissed?.Invoke(this);

    private static string Relative(string path, string? root) =>
        (root is not null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(root, path) : path)
        .Replace('\\', '/');
}

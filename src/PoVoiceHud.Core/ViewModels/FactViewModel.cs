using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Providers;

namespace PoVoiceHud.Core.ViewModels;

/// <summary>One row of the Facts page: a claim the model found correct or incorrect, its reason and its source.</summary>
public sealed partial class FactViewModel : ObservableObject
{
    private readonly Insight _insight;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title))] private int _count = 1;

    public FactViewModel(Insight insight, DateTimeOffset at, SourceHint source, bool showSpeaker, Func<Uri, Task> openUri)
    {
        _insight = insight;
        Time = $"{at:HH:mm}";
        Speaker = showSpeaker ? source.Speaker() : "";
        OpenLinkCommand = new AsyncRelayCommand(() => insight.Link is null ? Task.CompletedTask : openUri(insight.Link));
    }

    public string Claim => _insight.Claim;
    public Verdict Verdict => _insight.Verdict;
    public bool IsCorrect => Verdict == Verdict.Supported;
    public string Label => Verdict.Label();
    public string Reason => _insight.Reason;
    /// <summary>The claim, and how many times it was said when more than once.</summary>
    public string Title => Claim + (Count > 1 ? $" ×{Count}" : "");
    /// <summary>When it was said (the first time), as a clock time.</summary>
    public string Time { get; }
    /// <summary>"You" or "Them" when the call is heard too; empty otherwise.</summary>
    public string Speaker { get; }
    public bool HasLink => _insight.Link is not null;

    /// <summary>
    /// Shows where the link goes. A page the app looked up itself is a checked source; a link the model wrote can be
    /// invented, so it is labelled as the model's citation.
    /// </summary>
    public string LinkLabel => _insight.Link is not { } link ? "" : $"{link.Host} ↗ ({(_insight.LinkChecked ? "looked up" : "cited by the model")})";

    public IAsyncRelayCommand OpenLinkCommand { get; }

    /// <summary>One line for "Copy facts": "✅ Correct: claim — reason (link)".</summary>
    public string CopyLine => $"{Verdict.Emoji()} {Label}: {Title} — {Reason}{(_insight.Link is { } link ? $" ({link})" : "")}";

    /// <summary>The same claim, said again (compared ignoring case).</summary>
    public bool IsSameAs(Insight other) => string.Equals(Claim, other.Claim, StringComparison.OrdinalIgnoreCase);
}

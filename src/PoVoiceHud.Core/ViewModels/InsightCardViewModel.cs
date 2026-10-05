using CommunityToolkit.Mvvm.Input;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.ViewModels;

public sealed partial class JumpItem(Jump jump, string label, Func<Uri, Task> openUri)
{
    public string Label => label;

    [RelayCommand]
    private Task Open() => openUri(jump.Uri);
}

/// <summary>
/// One card. <see cref="Summary"/> is its single line (the pill and the history list show it);
/// the card itself then only needs the answer and the links. Jumps are labelled relative to the repo root.
/// </summary>
public sealed class InsightCardViewModel
{
    private readonly Insight _insight;

    /// <param name="count">How many times this same thing has come up, when it repeats an earlier card.</param>
    public InsightCardViewModel(Insight insight, string? repoRoot, Func<Uri, Task> openUri, int count = 1)
    {
        _insight = insight;
        Count = count;
        var close = insight.Headline.IndexOf(']');
        (Tag, Title) = insight.Headline.StartsWith('[') && close > 0
            ? (insight.Headline[1..close], insight.Headline[(close + 1)..].Trim())
            : ("", insight.Headline);
        Jumps = insight.Jumps.Select(j => new JumpItem(j, $"{Relative(j.AbsolutePath, repoRoot)}:{j.Line}", openUri)).ToList();
        OpenLinkCommand = new AsyncRelayCommand(() => insight.Link is null ? Task.CompletedTask : openUri(insight.Link));
    }

    public string Tag { get; }
    public string Title { get; }
    public int Count { get; }
    public string Answer => _insight.Answer;
    public Verdict? Verdict => _insight.Verdict;
    public string? Stale => _insight.Stale;
    public bool IsStale => _insight.Stale is not null;
    public bool HasLink => _insight.Link is not null;
    public bool HasJumps => Jumps.Count > 0;

    /// <summary>"❌ Python was created by Dennis Ritchie ×2" for a verdict, "[SCHEMA] Inspection.TenantId" for the other sources.</summary>
    public string Summary => (Verdict switch
    {
        Providers.Verdict.Supported => $"✅ {Title}",
        Providers.Verdict.Contradicted => $"❌ {Title}",
        Providers.Verdict.Unverified => $"⚠️ {Title}",
        _ => _insight.Headline,
    }) + (Count > 1 ? $" ×{Count}" : "");

    /// <summary>The same sentence or subject coming up again (headlines are compared, ignoring case).</summary>
    public bool IsSameAs(Insight other) => string.Equals(_insight.Headline, other.Headline, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Shows where the link goes. A fact-check link was written by the model, which can invent URLs,
    /// so it is labelled as the model's citation rather than as a checked source.
    /// </summary>
    public string LinkLabel => _insight.Link is not { } link ? ""
        : _insight.Intent == Intent.FactCheck ? $"{link.Host} ↗ (cited by the model)"
        : $"{link.Host} ↗";

    public string CopyText => $"{_insight.Headline}\n{_insight.Answer}{(_insight.Link is { } link ? $"\n{link}" : "")}";
    public IReadOnlyList<JumpItem> Jumps { get; }
    public IAsyncRelayCommand OpenLinkCommand { get; }

    private static string Relative(string path, string? root) =>
        (root is not null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(root, path) : path)
        .Replace('\\', '/');
}

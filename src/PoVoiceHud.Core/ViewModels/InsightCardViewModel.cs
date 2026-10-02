using CommunityToolkit.Mvvm.Input;
using PoVoiceHud.Core.Providers;

namespace PoVoiceHud.Core.ViewModels;

public sealed partial class JumpItem(Jump jump, string label, Func<Uri, Task> openUri)
{
    public string Label => label;

    [RelayCommand]
    private Task Open() => openUri(jump.Uri);
}

/// <summary>Headline "[TAG] Title" → Tag/Title; jumps labelled relative to the repo root.</summary>
public sealed class InsightCardViewModel
{
    private readonly Insight _insight;

    public InsightCardViewModel(Insight insight, string? repoRoot, Func<Uri, Task> openUri)
    {
        _insight = insight;
        var close = insight.Headline.IndexOf(']');
        (Tag, Title) = insight.Headline.StartsWith('[') && close > 0
            ? (insight.Headline[1..close], insight.Headline[(close + 1)..].Trim())
            : ("", insight.Headline);
        Jumps = insight.Jumps.Select(j => new JumpItem(j, $"{Relative(j.AbsolutePath, repoRoot)}:{j.Line}", openUri)).ToList();
        OpenLinkCommand = new AsyncRelayCommand(() => insight.Link is null ? Task.CompletedTask : openUri(insight.Link));
    }

    public string Tag { get; }
    public string Title { get; }
    public string Answer => _insight.Answer;
    public string? Stale => _insight.Stale;
    public bool IsStale => _insight.Stale is not null;
    public bool HasLink => _insight.Link is not null;
    public IReadOnlyList<JumpItem> Jumps { get; }
    public IAsyncRelayCommand OpenLinkCommand { get; }

    private static string Relative(string path, string? root) =>
        (root is not null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(root, path) : path)
        .Replace('\\', '/');
}

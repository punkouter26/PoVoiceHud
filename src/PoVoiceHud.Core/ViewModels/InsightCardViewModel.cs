using CommunityToolkit.Mvvm.Input;
using PoVoiceHud.Core.Providers;

namespace PoVoiceHud.Core.ViewModels;

public sealed partial class JumpItem(Jump jump, string label, Func<Uri, Task> openUri)
{
    public string Label => label;
    public Uri Uri => jump.Uri;

    [RelayCommand]
    private Task Open() => openUri(jump.Uri);
}

/// <summary>Headline "[TAG] Title" → Tag/Title; jumps labelled relative to the repo root.</summary>
public sealed class InsightCardViewModel
{
    public InsightCardViewModel(Insight insight, string? repoRoot, Func<Uri, Task> openUri)
    {
        Insight = insight;
        var close = insight.Headline.IndexOf(']');
        (Tag, Title) = insight.Headline.StartsWith('[') && close > 0
            ? (insight.Headline[1..close], insight.Headline[(close + 1)..].Trim())
            : ("", insight.Headline);
        Jumps = insight.Jumps.Select(j => new JumpItem(j, $"{Relative(j.AbsolutePath, repoRoot)}:{j.Line}", openUri)).ToList();
        OpenLinkCommand = new AsyncRelayCommand(() => insight.Link is null ? Task.CompletedTask : openUri(insight.Link));
    }

    public Insight Insight { get; }
    public string Tag { get; }
    public string Title { get; }
    public string Answer => Insight.Answer;
    public string? Stale => Insight.Stale;
    public bool IsStale => Insight.Stale is not null;
    public Uri? Link => Insight.Link;
    public bool HasLink => Insight.Link is not null;
    public IReadOnlyList<JumpItem> Jumps { get; }
    public IAsyncRelayCommand OpenLinkCommand { get; }

    private static string Relative(string path, string? root)
    {
        var relative = root is not null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? path[root.Length..].TrimStart('\\', '/')
            : path;
        return relative.Replace('\\', '/');
    }
}

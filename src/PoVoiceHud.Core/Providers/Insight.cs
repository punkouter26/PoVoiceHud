using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Providers;

public sealed record Jump(string AbsolutePath, int Line)
{
    public Uri Uri => new($"vscode://file/{AbsolutePath.Replace('\\', '/')}:{Line}");
}

/// <summary>One HUD card: Headline → one-sentence Answer → Jump buttons. Confidence orders cards; Stale carries the ⚠ text.</summary>
public sealed record Insight(
    Intent Intent,
    string Headline,
    string Answer,
    IReadOnlyList<Jump> Jumps,
    double Confidence = 1,
    string? Stale = null,
    Uri? Link = null);

public sealed record Query(string Text, IReadOnlyList<SymbolMatch> Symbols, Intent Intents);

using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Providers;

public sealed record Jump(string AbsolutePath, int Line)
{
    public Uri Uri => new($"vscode://file/{AbsolutePath.Replace('\\', '/')}:{Line}");
}

/// <summary>A fact-check outcome; drives the card colour, the session tally and the contradiction sound.</summary>
public enum Verdict { Supported, Contradicted, Unverified }

public static class VerdictExtensions
{
    /// <summary>The mark that cards, the tally and the verdict line show for this outcome.</summary>
    public static string Emoji(this Verdict verdict) => verdict switch
    {
        Verdict.Supported => "✅",
        Verdict.Contradicted => "❌",
        _ => "⚠️",
    };
}

/// <summary>
/// One HUD card: Headline → one-sentence Answer → Jump buttons. Confidence orders cards; Stale carries the ⚠ text.
/// LinkChecked means Link is a page the app fetched itself (a Wikipedia article), not a URL the model wrote.
/// </summary>
public sealed record Insight(
    Intent Intent,
    string Headline,
    string Answer,
    IReadOnlyList<Jump> Jumps,
    double Confidence = 1,
    string? Stale = null,
    Uri? Link = null,
    Verdict? Verdict = null,
    bool LinkChecked = false);

/// <summary>Context is the sentences heard just before (to resolve "it"/"they"); Fresh bypasses provider caches.</summary>
public sealed record Query(string Text, IReadOnlyList<SymbolMatch> Symbols, Intent Intents, IReadOnlyList<string>? Context = null, bool Fresh = false);

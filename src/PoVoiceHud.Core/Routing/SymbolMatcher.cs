using System.Text.RegularExpressions;
using Raffinert.FuzzySharp;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.Routing;

public sealed record SymbolMatch(Symbol Symbol, double Score);

/// <summary>
/// Scores each symbol by the fraction of its name tokens heard in the sentence (camel-split, fuzzy ≥ 80 so
/// "inspections"/"tenent" still count). Returns the top five at ≥ 0.5; callers choose their own bar.
/// </summary>
public static partial class SymbolMatcher
{
    private const int TokenSimilarity = 80;
    private static readonly HashSet<string> Ignored = ["adr", "md", "cs"];

    public static IReadOnlyList<SymbolMatch> Match(string text, SymbolIndex index)
    {
        var spoken = Tokens(text).ToArray();
        if (spoken.Length == 0) return [];

        var matches = new List<SymbolMatch>();
        foreach (var symbol in index.Symbols)
        {
            var name = Tokens(symbol.Name).Where(t => !Ignored.Contains(t) && !t.All(char.IsDigit)).ToArray();
            if (name.Length == 0) continue;
            var heard = name.Count(n => spoken.Any(s => Fuzz.Ratio(n, s) >= TokenSimilarity));
            var score = (double)heard / name.Length;
            if (score >= 0.5) matches.Add(new SymbolMatch(symbol, score));
        }
        return matches.OrderByDescending(m => m.Score).ThenByDescending(m => m.Symbol.Name.Length).Take(5).ToList();
    }

    private static IEnumerable<string> Tokens(string text) =>
        Token().Matches(text).Select(m => m.Value.ToLowerInvariant()).Where(t => t.Length > 1);

    [GeneratedRegex(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+")]
    private static partial Regex Token();
}

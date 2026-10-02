using System.Text.RegularExpressions;

namespace PoVoiceHud.Core.Routing;

/// <summary>Keyword rules from SPEC §3.3. Web is not decided here: it needs the symbol index (see Router).</summary>
public static class IntentClassifier
{
    private static readonly (Intent Intent, Regex Rule)[] Rules =
    [
        (Intent.Git, Rule(@"who (?:modified|changed|wrote|touched|last)", @"when was .{0,40}\b(?:added|changed|introduced)",
            "commits?", "authors?", "blame", "history", "last touched")),
        (Intent.Schema, Rule("tables?", "columns?", "foreign keys?", "primary keys?", "nullable", "migrations?",
            "index(?:es|ed)?", "schema", "entit(?:y|ies)")),
        (Intent.Docs, Rule("adrs?", "patterns?", @"why (?:did|do) we", "standards?", "architecture", "decisions?", "design docs?")),
        (Intent.Pipeline, Rule("builds?", "pipelines?", "deploy(?:ed|ment|s)?", "staging", "ci", "releases?")),
    ];

    private static readonly HashSet<string> QuestionStarters = new(StringComparer.OrdinalIgnoreCase)
    {
        "what", "what's", "who", "who's", "when", "where", "why", "how", "which", "is", "are", "was", "were",
        "does", "do", "did", "can", "could", "should", "will", "would", "has", "have",
    };

    public static Intent Classify(string text)
    {
        var result = Intent.None;
        foreach (var (intent, rule) in Rules)
            if (rule.IsMatch(text)) result |= intent;
        return result;
    }

    private static readonly HashSet<string> CommandStarters = new(StringComparer.OrdinalIgnoreCase)
    {
        "let", "let's", "please", "go", "open", "show", "give", "send", "run", "check", "make", "tell", "call", "share", "try",
    };

    public static bool IsQuestion(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.EndsWith('?')) return true;
        return QuestionStarters.Contains(FirstWord(trimmed));
    }

    /// <summary>A claim worth fact-checking: not a question, not a request or instruction.</summary>
    public static bool IsDeclarative(string text) => !IsQuestion(text) && !CommandStarters.Contains(FirstWord(text.Trim()));

    private static string FirstWord(string trimmed) => trimmed.Split(' ', 2)[0].TrimEnd(',', '.');

    private static Regex Rule(params string[] alternatives) =>
        new(@"\b(?:" + string.Join('|', alternatives) + @")\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
}

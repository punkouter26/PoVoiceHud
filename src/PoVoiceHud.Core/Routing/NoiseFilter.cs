using System.Text.RegularExpressions;

namespace PoVoiceHud.Core.Routing;

public static partial class NoiseFilter
{
    private static readonly HashSet<string> Fillers = new(StringComparer.OrdinalIgnoreCase)
    {
        "yeah", "yep", "yes", "no", "ok", "okay", "um", "uh", "hmm", "right", "sure", "cool", "great", "good",
        "sounds", "thanks", "thank", "you", "everyone", "all", "hi", "hello", "hey", "morning", "afternoon",
        "bye", "alright", "so", "well", "like", "please", "can", "hear", "me", "guys", "folks", "perfect",
    };

    // ponytail: word-count + filler-set heuristic; a 3-word real question ("who changed this") is dropped too.
    public static bool IsNoise(string text)
    {
        var words = Words().Matches(text).Select(m => m.Value).ToArray();
        return words.Length < 4 || words.All(Fillers.Contains);
    }

    [GeneratedRegex(@"[\p{L}']+")]
    private static partial Regex Words();
}

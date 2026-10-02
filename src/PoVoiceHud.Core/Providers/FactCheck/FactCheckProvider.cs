using System.Text.Json;
using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Providers.FactCheck;

/// <summary>An LLM verdict for a spoken claim. Degrades to "unverified" at every failure point.</summary>
public sealed class FactCheckProvider(ILanguageModel? model) : IInsightProvider
{
    private const string SystemPrompt =
        "You are a careful fact checker listening to someone speak. Judge the sentence from your own knowledge and reply with ONLY a JSON object: " +
        "{\"verdict\": \"supported\" | \"contradicted\" | \"unverified\" | \"not_a_claim\", \"reason\": one short sentence, " +
        "\"source\": a URL you are confident exists that backs the reason, or \"\"}. Use \"not_a_claim\" for instructions, chit-chat, " +
        "questions, opinions, personal statements and fragments — anything that is not a checkable statement about the world. " +
        "Say \"unverified\" only for a real claim you genuinely cannot judge.";

    public Intent Handles => Intent.FactCheck;

    public async Task<Insight?> QueryAsync(Query query, CancellationToken ct)
    {
        var claim = query.Text.Trim().TrimEnd('.');
        var headline = $"[FACT] {(claim.Length <= 60 ? claim : claim[..57] + "…")}";

        if (model is null)
            return new Insight(Intent.FactCheck, headline, "⚠️ Unverified (no LLM configured) — add a fact-check model in Settings.", [], 0.5);

        string raw;
        try
        {
            raw = await model.CompleteAsync(SystemPrompt, $"Statement: {claim}", ct);
        }
        catch (HttpRequestException e)
        {
            var status = e.StatusCode is { } code ? ((int)code).ToString() : "network";
            return new Insight(Intent.FactCheck, headline, $"⚠️ Unverified — fact-check service unavailable ({status}).", [], 0.4);
        }

        var (verdict, reason, source) = Parse(raw);
        if (verdict.Replace(' ', '_') == "not_a_claim") return null;   // nothing to show for chatter or commands
        var (prefix, confidence) = verdict switch
        {
            "supported" => ("✅ Supported", 0.9),
            "contradicted" => ("❌ Contradicted", 0.9),
            _ => ("⚠️ Unverified", 0.6),
        };
        var link = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;
        return new Insight(Intent.FactCheck, headline, $"{prefix} — {reason}", [], confidence, Link: link);
    }

    /// <summary>Lenient: takes the first {...} in the reply so prose or code fences around the JSON don't matter.</summary>
    private static (string Verdict, string Reason, string? Source) Parse(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return ("unverified", "could not read the model's answer.", null);
        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var root = doc.RootElement;
            return (
                root.TryGetProperty("verdict", out var v) ? (v.GetString() ?? "").Trim().ToLowerInvariant() : "unverified",
                root.TryGetProperty("reason", out var r) ? (r.GetString() ?? "").Trim() : "no reason given.",
                root.TryGetProperty("source", out var s) ? s.GetString() : null);
        }
        catch (JsonException)
        {
            return ("unverified", "could not read the model's answer.", null);
        }
    }
}

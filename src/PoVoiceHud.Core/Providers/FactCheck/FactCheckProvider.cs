using System.Text;
using System.Text.Json;
using PoVoiceHud.Core.Providers.Web;
using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Providers.FactCheck;

/// <summary>Brave evidence + an LLM verdict for a spoken claim. Degrades to "unverified" at every failure point.</summary>
public sealed class FactCheckProvider(BraveSearch search, ILanguageModel? model) : IInsightProvider
{
    private const string SystemPrompt =
        "You are a careful fact checker. Given a claim and web evidence, reply with ONLY a JSON object: " +
        "{\"verdict\": \"supported\" | \"contradicted\" | \"unverified\", \"reason\": one short sentence, " +
        "\"source\": the most relevant evidence URL or \"\"}. Say \"unverified\" when the evidence is insufficient.";

    public Intent Handles => Intent.FactCheck;

    public async Task<Insight?> QueryAsync(Query query, CancellationToken ct)
    {
        var claim = query.Text.Trim().TrimEnd('.');
        var headline = $"[FACT] {(claim.Length <= 60 ? claim : claim[..57] + "…")}";

        IReadOnlyList<BraveResult> evidence = [];
        try
        {
            evidence = (await search.SearchAsync(claim, 5, ct)).Results;
        }
        catch (HttpRequestException)
        {
            // no evidence; the model is asked anyway and will say "unverified"
        }
        var top = evidence.FirstOrDefault();
        var topLink = top is null ? null : new Uri(top.Url);

        if (model is null)
        {
            var shown = top is null ? "no search results." : top.Description;
            return new Insight(Intent.FactCheck, headline, $"⚠️ Unverified (no LLM configured) — {shown}", [], 0.5, Link: topLink);
        }

        string raw;
        try
        {
            raw = await model.CompleteAsync(SystemPrompt, UserPrompt(claim, evidence), ct);
        }
        catch (HttpRequestException e)
        {
            var status = e.StatusCode is { } code ? ((int)code).ToString() : "network";
            return new Insight(Intent.FactCheck, headline, $"⚠️ Unverified — fact-check service unavailable ({status}).", [], 0.4, Link: topLink);
        }

        var (verdict, reason, source) = Parse(raw);
        var (prefix, confidence) = verdict switch
        {
            "supported" => ("✅ Supported", 0.9),
            "contradicted" => ("❌ Contradicted", 0.9),
            _ => ("⚠️ Unverified", 0.6),
        };
        var link = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : topLink;
        return new Insight(Intent.FactCheck, headline, $"{prefix} — {reason}", [], confidence, Link: link);
    }

    private static string UserPrompt(string claim, IReadOnlyList<BraveResult> evidence)
    {
        var sb = new StringBuilder().Append("Claim: ").AppendLine(claim).AppendLine().AppendLine("Evidence:");
        if (evidence.Count == 0) sb.AppendLine("No evidence was found online; judge from general knowledge and prefer \"unverified\".");
        for (var i = 0; i < evidence.Count; i++)
            sb.Append(i + 1).Append(". ").Append(evidence[i].Title).Append(" — ").Append(evidence[i].Description).Append(" (").Append(evidence[i].Url).AppendLine(")");
        return sb.ToString();
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

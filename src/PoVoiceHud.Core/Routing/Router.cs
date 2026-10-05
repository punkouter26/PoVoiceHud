using Microsoft.Extensions.Logging;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Providers.Code;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.Routing;

/// <summary>
/// Noise filter → intents (masked by the enabled sources) → symbol matches → parallel fan-out → rank by confidence.
/// Sentences naming code go to Code research; other declarative statements go to FactCheck; questions nothing claims are dropped.
/// </summary>
public sealed class Router(IEnumerable<IInsightProvider> providers, Func<SymbolIndex?> index, Func<Intent> enabledSources, ILogger<Router> log)
{
    private readonly IInsightProvider[] _providers = providers.ToArray();

    /// <summary>The sources this sentence will be sent to; None means it is dropped without any provider running.</summary>
    public Intent Plan(string text) => Decide(text).Intents;

    public Task<IReadOnlyList<Insight>> RouteAsync(string text, CancellationToken ct) => RouteAsync(text, context: null, fresh: false, ct);

    public async Task<IReadOnlyList<Insight>> RouteAsync(string text, IReadOnlyList<string>? context, bool fresh, CancellationToken ct)
    {
        var (intents, symbols) = Decide(text);
        if (intents == Intent.None) return [];

        var query = new Query(text, symbols, intents, context, fresh);
        var answers = await Task.WhenAll(_providers.Where(p => intents.HasFlag(p.Handles)).Select(p => Ask(p, query, ct)));
        return answers.OfType<Insight>().OrderByDescending(i => i.Confidence).ToList();
    }

    private (Intent Intents, IReadOnlyList<SymbolMatch> Symbols) Decide(string text)
    {
        if (NoiseFilter.IsNoise(text)) return (Intent.None, []);
        var enabled = enabledSources();
        var intents = IntentClassifier.Classify(text) & enabled;
        var symbols = index() is { } current ? SymbolMatcher.Match(text, current) : [];

        // A sentence that names something in the repository, and that no keyword source claimed, is researched in the
        // code. That also takes it away from the fact checker: a claim about your own code is not general knowledge.
        if (intents == Intent.None && enabled.HasFlag(Intent.Code) && CodeProvider.Mentions(symbols).Any())
            intents = Intent.Code;
        else if (enabled.HasFlag(Intent.FactCheck) && IntentClassifier.IsDeclarative(text))
            intents |= Intent.FactCheck;
        return (intents, symbols);
    }

    private async Task<Insight?> Ask(IInsightProvider provider, Query query, CancellationToken ct)
    {
        try
        {
            return await provider.QueryAsync(query, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "{Provider} failed for \"{Text}\"", provider.GetType().Name, query.Text);
            return null;
        }
    }
}

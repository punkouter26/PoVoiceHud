using Microsoft.Extensions.Logging;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.Routing;

/// <summary>
/// Noise filter → intents (masked by the enabled sources) → symbol matches → parallel fan-out → rank by confidence.
/// Declarative statements additionally go to FactCheck when that source is on.
/// </summary>
public sealed class Router(IEnumerable<IInsightProvider> providers, Func<SymbolIndex?> index, Func<Intent> enabledSources, ILogger<Router> log)
{
    private readonly IInsightProvider[] _providers = providers.ToArray();

    public async Task<IReadOnlyList<Insight>> RouteAsync(string text, CancellationToken ct)
    {
        if (NoiseFilter.IsNoise(text)) return [];

        var enabled = enabledSources();
        var intents = IntentClassifier.Classify(text) & enabled;
        var symbols = index() is { } current ? SymbolMatcher.Match(text, current) : [];
        if (intents == Intent.None && enabled.HasFlag(Intent.Web) && IntentClassifier.IsQuestion(text) && !symbols.Any(s => s.Score >= SymbolMatcher.Strong))
            intents = Intent.Web;
        if (enabled.HasFlag(Intent.FactCheck) && IntentClassifier.IsDeclarative(text))
            intents |= Intent.FactCheck;
        if (intents == Intent.None) return [];

        var query = new Query(text, symbols, intents);
        var answers = await Task.WhenAll(_providers.Where(p => intents.HasFlag(p.Handles)).Select(p => Ask(p, query, ct)));
        return answers.OfType<Insight>().OrderByDescending(i => i.Confidence).ToList();
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

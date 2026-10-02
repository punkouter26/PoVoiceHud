using Microsoft.Extensions.Logging;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.Routing;

/// <summary>Noise filter → intents → symbol matches → parallel fan-out to matching providers → rank by confidence.</summary>
public sealed class Router(IEnumerable<IInsightProvider> providers, Func<SymbolIndex?> index, ILogger<Router> log)
{
    private const double StrongSymbol = 0.8;
    private readonly IInsightProvider[] _providers = providers.ToArray();

    public async Task<IReadOnlyList<Insight>> RouteAsync(string text, CancellationToken ct)
    {
        if (NoiseFilter.IsNoise(text)) return [];

        var intents = IntentClassifier.Classify(text);
        var symbols = index() is { } current ? SymbolMatcher.Match(text, current) : [];
        if (intents == Intent.None && IntentClassifier.IsQuestion(text) && !symbols.Any(s => s.Score >= StrongSymbol))
            intents = Intent.Web;
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

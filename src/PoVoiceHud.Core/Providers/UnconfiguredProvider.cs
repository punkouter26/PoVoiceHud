using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Providers;

/// <summary>Stands in for a source whose service is not set up yet: answers with a low-confidence "how to configure" card.</summary>
public sealed class UnconfiguredProvider(Intent intent, string message) : IInsightProvider
{
    public Intent Handles => intent;

    public Task<Insight?> QueryAsync(Query query, CancellationToken ct) => Task.FromResult<Insight?>(
        new Insight(intent, $"[{intent.ToString().ToUpperInvariant()}] Not configured", message, [], Confidence: 0.1));
}

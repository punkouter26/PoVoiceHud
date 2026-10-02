using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Providers.Fakes;

/// <summary>Scripted provider for tests and for intents whose real provider is not configured yet.</summary>
public sealed class FakeProvider(Intent handles, Func<Query, Insight?> answer) : IInsightProvider
{
    public Intent Handles => handles;

    public Task<Insight?> QueryAsync(Query query, CancellationToken ct) => Task.FromResult(answer(query));

    public static FakeProvider Unconfigured(Intent intent, string message) =>
        new(intent, _ => new Insight(intent, $"[{intent.ToString().ToUpperInvariant()}] Not configured", message, [], Confidence: 0.1));
}

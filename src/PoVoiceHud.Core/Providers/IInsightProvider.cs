using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Providers;

public interface IInsightProvider
{
    Intent Handles { get; }

    /// <summary>Null when the provider has nothing to say about this query.</summary>
    Task<Insight?> QueryAsync(Query query, CancellationToken ct);
}

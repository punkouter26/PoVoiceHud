using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Tests.Fixtures;

/// <summary>Scripted provider.</summary>
public sealed class FakeProvider(Intent handles, Func<Query, Insight?> answer) : IInsightProvider
{
    public Intent Handles => handles;

    public Task<Insight?> QueryAsync(Query query, CancellationToken ct) => Task.FromResult(answer(query));
}

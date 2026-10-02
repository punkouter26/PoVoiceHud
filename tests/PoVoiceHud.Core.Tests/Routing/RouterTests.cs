using Microsoft.Extensions.Logging.Abstractions;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Providers.Fakes;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Tests.Fixtures;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.Tests.Routing;

public class RouterTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static FakeProvider Recording(Intent handles, List<string> calls, double confidence = 1) =>
        new(handles, q => { calls.Add($"{handles}:{q.Text}"); return new Insight(handles, handles.ToString(), "answer", [], confidence); });

    private const Intent RepoSources = Intent.Git | Intent.Schema | Intent.Docs | Intent.Pipeline | Intent.Web;

    private static Router Build(IEnumerable<IInsightProvider> providers, SymbolIndex? index = null, Intent enabled = RepoSources) =>
        new(providers, () => index, () => enabled, NullLogger<Router>.Instance);

    [Fact]
    public async Task Disabled_sources_are_never_asked()
    {
        var calls = new List<string>();
        var router = Build([Recording(Intent.Git, calls), Recording(Intent.Schema, calls)], enabled: Intent.Git);

        var insights = await router.RouteAsync("who changed the schema for the inspection table", _ct);

        Assert.Equal(Intent.Git, Assert.Single(insights).Intent);
        Assert.DoesNotContain(calls, c => c.StartsWith("Schema:"));
    }

    [Fact]
    public async Task Simple_mode_fact_checks_statements_and_ignores_questions()
    {
        var calls = new List<string>();
        var router = Build([Recording(Intent.FactCheck, calls), Recording(Intent.Web, calls), Recording(Intent.Git, calls)], enabled: Intent.FactCheck);

        Assert.Single(await router.RouteAsync("The Eiffel Tower was completed in 1889", _ct));
        Assert.Equal(["FactCheck:The Eiffel Tower was completed in 1889"], calls);

        calls.Clear();
        Assert.Empty(await router.RouteAsync("what does IAsyncDisposable do again", _ct));
        Assert.Empty(await router.RouteAsync("who modified the invoice service last week", _ct));
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Statements_are_not_fact_checked_when_the_source_is_off()
    {
        var calls = new List<string>();
        var router = Build([Recording(Intent.FactCheck, calls), Recording(Intent.Git, calls)], enabled: Intent.Git);

        Assert.Empty(await router.RouteAsync("The Eiffel Tower was completed in 1889", _ct));
        Assert.Single(await router.RouteAsync("who modified the invoice service last week", _ct));
        Assert.Equal(["Git:who modified the invoice service last week"], calls);
    }

    [Fact]
    public async Task Repo_questions_can_also_be_fact_checked_when_both_are_on()
    {
        var calls = new List<string>();
        var router = Build([Recording(Intent.FactCheck, calls), Recording(Intent.Git, calls)], enabled: Intent.Git | Intent.FactCheck);

        var insights = await router.RouteAsync("the last commit on the invoice service was by Jane", _ct);

        Assert.Equal(2, insights.Count);
    }

    [Fact]
    public async Task Noise_is_dropped_before_any_provider_runs()
    {
        var calls = new List<string>();
        var router = Build([Recording(Intent.Git, calls)]);

        Assert.Empty(await router.RouteAsync("okay sounds good", _ct));
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Fans_out_only_to_providers_for_detected_intents()
    {
        var calls = new List<string>();
        var router = Build([Recording(Intent.Git, calls), Recording(Intent.Schema, calls), Recording(Intent.Pipeline, calls)]);

        var insights = await router.RouteAsync("who changed the schema for the inspection table", _ct);

        Assert.Equal(2, insights.Count);
        Assert.Contains(calls, c => c.StartsWith("Git:"));
        Assert.Contains(calls, c => c.StartsWith("Schema:"));
        Assert.DoesNotContain(calls, c => c.StartsWith("Pipeline:"));
    }

    [Fact]
    public async Task Ranks_insights_by_confidence()
    {
        var calls = new List<string>();
        var router = Build([Recording(Intent.Git, calls, 0.4), Recording(Intent.Schema, calls, 0.9)]);

        var insights = await router.RouteAsync("who changed the schema for the inspection table", _ct);

        Assert.Equal(Intent.Schema, insights[0].Intent);
        Assert.Equal(Intent.Git, insights[1].Intent);
    }

    [Fact]
    public async Task A_failing_provider_does_not_hide_the_others()
    {
        var calls = new List<string>();
        var broken = new FakeProvider(Intent.Git, _ => throw new InvalidOperationException("boom"));
        var router = Build([broken, Recording(Intent.Schema, calls)]);

        var insights = await router.RouteAsync("who changed the schema for the inspection table", _ct);

        Assert.Equal(Intent.Schema, Assert.Single(insights).Intent);
    }

    [Fact]
    public async Task Questions_without_keywords_or_symbols_fall_back_to_web()
    {
        using var repo = TempRepo.WithSampleProject();
        var index = SymbolIndex.Build(repo.Root);
        var calls = new List<string>();
        var router = Build([Recording(Intent.Web, calls), Recording(Intent.Git, calls)], index);

        Assert.Single(await router.RouteAsync("what does IAsyncDisposable do again", _ct));
        Assert.Contains(calls, c => c.StartsWith("Web:"));

        calls.Clear();
        Assert.Empty(await router.RouteAsync("what is the invoice service for", _ct));   // symbol matched, no keyword, not web
        Assert.Empty(await router.RouteAsync("we should refactor that later", _ct));      // not a question
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Providers_receive_symbol_matches_from_the_index()
    {
        using var repo = TempRepo.WithSampleProject();
        var index = SymbolIndex.Build(repo.Root);
        Query? seen = null;
        var router = Build([new FakeProvider(Intent.Schema, q => { seen = q; return null; })], index);

        Assert.Empty(await router.RouteAsync("does the inspection table have a tenant id foreign key", _ct));

        Assert.NotNull(seen);
        Assert.Contains(seen.Symbols, m => m.Symbol.Name == "Inspection" && m.Symbol.Kind == SymbolKind.Entity);
        Assert.Equal(Intent.Schema, seen.Intents);
    }

    [Fact]
    public void Jump_builds_a_vscode_file_uri_with_forward_slashes()
    {
        var jump = new Jump(@"C:\src\pmis\src\Domain\Entities\Inspection.cs", 28);

        Assert.Equal("vscode", jump.Uri.Scheme);
        Assert.Equal("vscode://file/C:/src/pmis/src/Domain/Entities/Inspection.cs:28", jump.Uri.OriginalString);
    }

    [Fact]
    public async Task Unconfigured_fake_answers_with_low_confidence()
    {
        var fake = FakeProvider.Unconfigured(Intent.Pipeline, "AzDO not configured");
        var insight = await fake.QueryAsync(new Query("did ci pass", [], Intent.Pipeline), _ct);

        Assert.NotNull(insight);
        Assert.Equal("AzDO not configured", insight.Answer);
        Assert.True(insight.Confidence < 0.5);
    }
}

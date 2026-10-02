using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Providers.Docs;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Tests.Fixtures;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.Tests.Providers;

public class DocsProviderTests
{
    private const string AdrPath = "docs/adr/0004-auth-lifecycle.md";
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static Query QueryFor(string text, TempRepo repo) =>
        new(text, SymbolMatcher.Match(text, SymbolIndex.Build(repo.Root)), Intent.Docs);

    private static void TouchAuthCode(TempRepo repo, DateTimeOffset when)
    {
        repo.Write("src/Auth/ClaimsHeaderMiddleware.cs", $"// changed {when:yyyy-MM-dd}\n");
        repo.Commit("Tweak auth middleware", when);
    }

    [Fact]
    public void Parses_title_status_decision_and_code_paths()
    {
        using var repo = TempRepo.WithSampleProject();
        var adr = AdrParser.Parse(File.ReadAllText(Path.Combine(repo.Root, AdrPath)), AdrPath);

        Assert.Equal("ADR-0004: Auth Lifecycle", adr.Title);
        Assert.Equal("Accepted", adr.Status);
        Assert.Equal("Use a ClaimsPrincipal header populated by the gateway (src/Auth).", adr.Decision);
        Assert.Equal(10, adr.DecisionLine);
        Assert.Equal(["src/Auth"], adr.CodePaths);
    }

    [Fact]
    public async Task Answers_with_the_decision_and_no_stale_flag_when_fresh()
    {
        using var repo = TempRepo.WithSampleProject();
        var provider = new DocsProvider(() => repo.Root);

        var insight = await provider.QueryAsync(QueryFor("why did we use a claims header for auth", repo), _ct);

        Assert.NotNull(insight);
        Assert.Equal("[DOCS] ADR-0004: Auth Lifecycle", insight.Headline);
        Assert.Equal("Accepted: Use a ClaimsPrincipal header populated by the gateway (src/Auth).", insight.Answer);
        Assert.Null(insight.Stale);
        var jump = Assert.Single(insight.Jumps);
        Assert.Equal(Path.Combine(repo.Root, "docs", "adr", "0004-auth-lifecycle.md"), jump.AbsolutePath);
        Assert.Equal(10, jump.Line);
    }

    [Fact]
    public async Task Flags_stale_when_documented_code_changed_more_than_14_days_later()
    {
        using var repo = TempRepo.WithSampleProject();
        TouchAuthCode(repo, new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero));
        var provider = new DocsProvider(() => repo.Root);

        var insight = await provider.QueryAsync(QueryFor("why did we use a claims header for auth", repo), _ct);

        Assert.NotNull(insight);
        Assert.Equal("⚠️ STALE DOCUMENTATION: Underlying code was modified on 2026-09-20 (19 days after this document).", insight.Stale);
    }

    [Fact]
    public async Task No_stale_flag_within_the_14_day_grace_period()
    {
        using var repo = TempRepo.WithSampleProject();
        TouchAuthCode(repo, new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero));
        var provider = new DocsProvider(() => repo.Root);

        var insight = await provider.QueryAsync(QueryFor("why did we use a claims header for auth", repo), _ct);

        Assert.NotNull(insight);
        Assert.Null(insight.Stale);
    }

    [Fact]
    public async Task Returns_null_when_no_adr_matches()
    {
        using var repo = TempRepo.WithSampleProject();
        var provider = new DocsProvider(() => repo.Root);

        Assert.Null(await provider.QueryAsync(QueryFor("what pattern do we use for messaging", repo), _ct));
    }
}

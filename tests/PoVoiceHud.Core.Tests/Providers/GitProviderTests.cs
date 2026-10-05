using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Providers.Git;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Tests.Fixtures;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.Tests.Providers;

[Trait("Category", "Integration")]
public class GitProviderTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static Query QueryFor(string text, TempRepo repo) =>
        new(text, SymbolMatcher.Match(text, SymbolIndex.Build(repo.Root)), Intent.Git);

    [Fact]
    public async Task Answers_with_the_latest_author_change_and_a_jump_to_the_file()
    {
        using var repo = TempRepo.WithSampleProject();
        repo.Write("src/Services/InvoiceService.cs", "namespace Pmis.Services;\n\npublic class InvoiceService { decimal Round(decimal d) => Math.Round(d, 2); }\n");
        var fix = repo.Commit("Fix rounding", DateTimeOffset.UtcNow.AddDays(-3), author: "John Smith");
        var provider = new GitProvider(() => repo.Root);

        var insight = await provider.QueryAsync(QueryFor("who modified the invoice service", repo), _ct);

        Assert.NotNull(insight);
        Assert.Equal(Intent.Git, insight.Intent);
        Assert.Contains("InvoiceService", insight.Headline);
        Assert.Contains("John Smith", insight.Answer);
        Assert.Contains("3 days ago", insight.Answer);
        Assert.Contains("Fix rounding", insight.Answer);
        Assert.Contains(fix.Sha[..7], insight.Answer);
        var jump = Assert.Single(insight.Jumps);
        Assert.Equal(Path.Combine(repo.Root, "src", "Services", "InvoiceService.cs"), jump.AbsolutePath);
        Assert.Equal(1, jump.Line);
    }

    [Fact]
    public async Task Returns_null_without_a_file_symbol()
    {
        using var repo = TempRepo.WithSampleProject();
        var provider = new GitProvider(() => repo.Root);

        Assert.Null(await provider.QueryAsync(new Query("who modified the invoice service", [], Intent.Git), _ct));
        Assert.Null(await provider.QueryAsync(QueryFor("who modified the payment gateway", repo), _ct));
    }

    [Fact]
    public void History_lists_the_five_most_recent_commits_newest_first()
    {
        using var repo = TempRepo.WithSampleProject();
        for (var i = 1; i <= 7; i++)
        {
            repo.Write("src/Services/InvoiceService.cs", $"// revision {i}\n");
            repo.Commit($"Revision {i}", new DateTimeOffset(2026, 9, i, 9, 0, 0, TimeSpan.Zero));
        }

        var history = GitProvider.History(repo.Root, "src/Services/InvoiceService.cs");

        Assert.Equal(5, history.Count);
        Assert.Equal("Revision 7", history[0].Subject);
        Assert.Equal("Revision 3", history[4].Subject);
        Assert.Equal(7, history[0].Sha7.Length);
        Assert.Equal("Jane Doe", history[0].Author);
    }

    [Fact]
    public async Task Mentions_how_many_earlier_changes_exist()
    {
        using var repo = TempRepo.WithSampleProject();
        for (var i = 1; i <= 3; i++)
        {
            repo.Write("src/Services/InvoiceService.cs", $"// revision {i}\n");
            repo.Commit($"Revision {i}", new DateTimeOffset(2026, 9, i, 9, 0, 0, TimeSpan.Zero));
        }
        var provider = new GitProvider(() => repo.Root);

        var insight = await provider.QueryAsync(QueryFor("who changed invoice service", repo), _ct);

        Assert.NotNull(insight);
        Assert.Contains("3 earlier", insight.Answer);
    }
}

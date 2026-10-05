using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Providers.Code;
using PoVoiceHud.Core.Providers.FactCheck;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Tests.Fixtures;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.Tests.Providers;

[Trait("Category", "Integration")]
public class CodeProviderTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private sealed class FakeModel(Func<string, string> answer) : ILanguageModel
    {
        public List<string> Messages { get; } = [];
        public Task<string> CompleteAsync(string system, string user, CancellationToken ct)
        {
            Messages.Add(user);
            return Task.FromResult(answer(user));
        }
    }

    private static Query Said(string text, TempRepo repo) =>
        new(text, SymbolMatcher.Match(text, SymbolIndex.Build(repo.Root)), Intent.Code, Context: ["We were talking about billing."]);

    [Fact]
    public async Task The_model_reads_the_files_a_sentence_names_and_the_card_links_to_them()
    {
        using var repo = TempRepo.WithSampleProject();
        var model = new FakeModel(_ => "InvoiceService is an empty class in Pmis.Services; it has no retry logic yet.");

        var insight = await new CodeProvider(() => repo.Root, model).QueryAsync(Said("the invoice service should retry failed payments", repo), _ct);

        Assert.NotNull(insight);
        Assert.Equal("[CODE] InvoiceService.cs", insight.Headline);
        Assert.Equal("InvoiceService is an empty class in Pmis.Services; it has no retry logic yet.", insight.Answer);
        Assert.Equal(Path.Combine(repo.Root, "src", "Services", "InvoiceService.cs"), Assert.Single(insight.Jumps).AbsolutePath);

        using var sent = JsonDocument.Parse(Assert.Single(model.Messages));
        Assert.Equal("the invoice service should retry failed payments", sent.RootElement.GetProperty("said").GetString());
        Assert.Equal("We were talking about billing.", sent.RootElement.GetProperty("context")[0].GetString());
        var file = Assert.Single(sent.RootElement.GetProperty("files").EnumerateArray());
        Assert.Equal("src/Services/InvoiceService.cs", file.GetProperty("path").GetString());
        Assert.Contains("public class InvoiceService", file.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Without_a_useful_answer_the_card_still_points_at_the_files()
    {
        using var repo = TempRepo.WithSampleProject();
        var said = Said("the invoice service should retry failed payments", repo);
        const string pointer = "Mentioned in the repository: src/Services/InvoiceService.cs.";

        Assert.Equal(pointer, (await new CodeProvider(() => repo.Root, model: null).QueryAsync(said, _ct))?.Answer);                                  // no model configured
        Assert.Equal(pointer, (await new CodeProvider(() => repo.Root, new FakeModel(_ => "NONE")).QueryAsync(said, _ct))?.Answer);                    // excerpts did not help
        Assert.Equal(pointer, (await new CodeProvider(() => repo.Root, new FakeModel(_ => throw new TimeoutException())).QueryAsync(said, _ct))?.Answer);   // model down

        Assert.Null(await new CodeProvider(() => repo.Root, model: null).QueryAsync(Said("the weather is nice in Paris today", repo), _ct));           // names nothing in the repo
        Assert.Null(await new CodeProvider(() => null, model: null).QueryAsync(said, _ct));                                                           // no repository
    }

    [Fact]
    public async Task Repo_mode_researches_sentences_about_the_code_and_fact_checks_the_rest()
    {
        using var repo = TempRepo.WithSampleProject();
        var index = SymbolIndex.Build(repo.Root);
        var asked = new List<Intent>();
        IInsightProvider Recording(Intent intent) => new FakeProvider(intent, _ => { asked.Add(intent); return new Insight(intent, intent.ToString(), "answer", []); });
        var router = new Router([Recording(Intent.Code), Recording(Intent.FactCheck), Recording(Intent.Git)], () => index, () => Core.Config.Settings.RepoSources, NullLogger<Router>.Instance);

        await router.RouteAsync("the invoice service should retry failed payments", _ct);   // about our code: researched, not "fact-checked"
        Assert.Equal([Intent.Code], asked);

        asked.Clear();
        await router.RouteAsync("The Eiffel Tower was completed in 1889", _ct);             // everything Simple mode does still happens
        Assert.Equal([Intent.FactCheck], asked);

        asked.Clear();
        await router.RouteAsync("who modified the invoice service last week", _ct);         // a keyword source claims it: no second card from Code
        Assert.Equal([Intent.Git], asked);
    }
}

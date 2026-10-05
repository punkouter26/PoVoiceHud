using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Providers.Schema;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Tests.Fixtures;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.Tests.Providers;

[Trait("Category", "Integration")]
public class EfSnapshotParserTests
{
    private const string SnapshotPath = "src/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs";
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static Query QueryFor(string text, TempRepo repo) =>
        new(text, SymbolMatcher.Match(text, SymbolIndex.Build(repo.Root)), Intent.Schema);

    [Fact]
    public Task Parses_entities_columns_keys_indexes_and_foreign_keys()
    {
        using var repo = TempRepo.WithSampleProject();
        var model = EfSnapshotParser.Parse(File.ReadAllText(Path.Combine(repo.Root, SnapshotPath)), SnapshotPath);

        return Verify(model);
    }

    [Fact]
    public async Task Answers_the_tenant_id_foreign_key_question_exactly()
    {
        using var repo = TempRepo.WithSampleProject();
        var provider = new SchemaProvider(() => repo.Root);

        var insight = await provider.QueryAsync(QueryFor("does the inspection table have a tenant id foreign key", repo), _ct);

        Assert.NotNull(insight);
        Assert.Equal("[SCHEMA] Inspection.TenantId", insight.Headline);
        Assert.Equal("Yes. Inspection.TenantId is a required uniqueidentifier foreign key to Tenants.Id, indexed.", insight.Answer);
        Assert.Collection(insight.Jumps,
            j => { Assert.EndsWith(@"src\Domain\Entities\Inspection.cs", j.AbsolutePath); Assert.Equal(6, j.Line); },
            j => { Assert.EndsWith(@"Migrations\AppDbContextModelSnapshot.cs", j.AbsolutePath); Assert.Equal(12, j.Line); });
    }

    [Fact]
    public async Task Nullable_question_on_a_required_column_is_answered_no()
    {
        using var repo = TempRepo.WithSampleProject();
        var provider = new SchemaProvider(() => repo.Root);

        var insight = await provider.QueryAsync(QueryFor("is tenant id nullable on inspection", repo), _ct);

        Assert.NotNull(insight);
        Assert.StartsWith("No. ", insight.Answer);
        Assert.Contains("required", insight.Answer);
    }

    [Fact]
    public async Task Optional_column_is_described_as_optional()
    {
        using var repo = TempRepo.WithSampleProject();
        var provider = new SchemaProvider(() => repo.Root);

        var insight = await provider.QueryAsync(QueryFor("is the notes column nullable on the inspection table", repo), _ct);

        Assert.NotNull(insight);
        Assert.Equal("Yes. Inspection.Notes is an optional nvarchar(2000).", insight.Answer);
    }

    [Fact]
    public async Task Summarises_the_entity_when_no_column_is_named()
    {
        using var repo = TempRepo.WithSampleProject();
        var provider = new SchemaProvider(() => repo.Root);

        var insight = await provider.QueryAsync(QueryFor("what does the inspection table look like", repo), _ct);

        Assert.NotNull(insight);
        Assert.Equal("[SCHEMA] Inspection", insight.Headline);
        Assert.Equal("Table Inspections: 3 columns (Id, TenantId, Notes), key Id, FK TenantId → Tenants, index on TenantId.", insight.Answer);
    }

    [Fact]
    public async Task Returns_null_without_an_entity_symbol()
    {
        using var repo = TempRepo.WithSampleProject();
        var provider = new SchemaProvider(() => repo.Root);

        Assert.Null(await provider.QueryAsync(QueryFor("which columns are indexed on orders", repo), _ct));
    }
}

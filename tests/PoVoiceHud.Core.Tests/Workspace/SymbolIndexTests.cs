using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Tests.Fixtures;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.Tests.Workspace;

[Trait("Category", "Integration")]
public class SymbolIndexTests
{
    [Fact]
    public void Indexes_tracked_files_ef_entities_and_adr_titles()
    {
        using var repo = TempRepo.WithSampleProject();
        var index = SymbolIndex.Build(repo.Root);

        var file = Assert.Single(index.Symbols, s => s.Kind == SymbolKind.File && s.Name == "InvoiceService");
        Assert.Equal("src/Services/InvoiceService.cs", file.Path);

        var entity = Assert.Single(index.Symbols, s => s.Kind == SymbolKind.Entity && s.Name == "Inspection");
        Assert.Equal("src/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs", entity.Path);
        Assert.Equal(9, entity.Line);

        var adr = Assert.Single(index.Symbols, s => s.Kind == SymbolKind.Adr);
        Assert.Equal("ADR-0004: Auth Lifecycle", adr.Name);
        Assert.Equal("docs/adr/0004-auth-lifecycle.md", adr.Path);

        // Whisper's glossary: entities first, then the domain words in file names, without the ones every codebase has.
        var words = index.Vocabulary().ToList();
        Assert.Equal(["Inspection", "Tenant"], words.Take(2));
        Assert.Contains("Invoice", words);
        Assert.Contains("Middleware", words);
        Assert.DoesNotContain("Service", words);
        Assert.DoesNotContain("Context", words);
        Assert.Equal(words.Count, words.Distinct(StringComparer.OrdinalIgnoreCase).Count());   // "Inspection" is an entity and a file: once
    }

    [Fact]
    public void Matches_spoken_phrases_to_symbols()
    {
        using var repo = TempRepo.WithSampleProject();
        var index = SymbolIndex.Build(repo.Root);

        var schema = SymbolMatcher.Match("does the inspection table have a tenant id foreign key", index);
        Assert.Contains(schema, m => m.Symbol is { Kind: SymbolKind.Entity, Name: "Inspection" } && m.Score >= 0.8);
        Assert.Contains(schema, m => m.Symbol is { Kind: SymbolKind.Entity, Name: "Tenant" } && m.Score >= 0.8);

        var git = SymbolMatcher.Match("who modified the invoice service", index);
        Assert.Equal("InvoiceService", git[0].Symbol.Name);
        Assert.Equal(1.0, git[0].Score);

        var docs = SymbolMatcher.Match("why did we use a claims header for auth", index);
        Assert.Contains(docs, m => m.Symbol.Kind == SymbolKind.Adr && m.Score >= 0.5);

        Assert.Empty(SymbolMatcher.Match("what does IAsyncDisposable do again", index));
    }

    [Fact]
    public void Tolerates_plurals_and_small_transcription_errors()
    {
        using var repo = TempRepo.WithSampleProject();
        var index = SymbolIndex.Build(repo.Root);

        var matches = SymbolMatcher.Match("are the inspections tied to a tenent", index);
        Assert.Contains(matches, m => m.Symbol.Name == "Inspection");
        Assert.Contains(matches, m => m.Symbol.Name == "Tenant");
    }

    [Fact]
    public void Rejects_folders_that_are_not_git_repositories()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PoVoiceHud.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Throws<ArgumentException>(() => new Core.Workspace.Workspace(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Workspace_rebuilds_index_after_a_file_change()
    {
        using var repo = TempRepo.WithSampleProject();
        using var ws = new Core.Workspace.Workspace(repo.Root);
        var changed = new TaskCompletionSource();
        ws.Changed += () => changed.TrySetResult();
        Assert.DoesNotContain(ws.Index.Symbols, s => s.Name == "PaymentController");

        repo.Write("src/Api/PaymentController.cs", "public class PaymentController { }");
        repo.Commit("Add payments", DateTimeOffset.UtcNow);

        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains(ws.Index.Symbols, s => s.Name == "PaymentController");
    }
}

using LibGit2Sharp;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.Providers.Docs;

/// <summary>The matching ADR's decision, flagged STALE when the code it names changed &gt;14 days after the doc.</summary>
public sealed class DocsProvider(Func<string?> repoRoot) : IInsightProvider
{
    private const double MinScore = 0.5;
    private static readonly TimeSpan Grace = TimeSpan.FromDays(14);

    public Intent Handles => Intent.Docs;

    public Task<Insight?> QueryAsync(Query query, CancellationToken ct)
    {
        var root = repoRoot();
        var match = query.Symbols
            .Where(m => m.Score >= MinScore && m.Symbol.Kind == SymbolKind.Adr)
            .OrderByDescending(m => m.Score)
            .FirstOrDefault();
        if (root is null || match is null) return Task.FromResult<Insight?>(null);

        var absolute = match.Symbol.AbsolutePath(root);
        var adr = AdrParser.Parse(File.ReadAllText(absolute), match.Symbol.Path);
        var answer = (adr.Status, adr.Decision) switch
        {
            (not null, not null) => $"{adr.Status}: {adr.Decision}",
            (_, not null) => adr.Decision,
            (not null, _) => $"Status: {adr.Status}",
            _ => "No Decision section found.",
        };
        return Task.FromResult<Insight?>(new Insight(
            Intent.Docs, $"[DOCS] {adr.Title}", answer, [new Jump(absolute, adr.DecisionLine)], match.Score, Staleness(root, adr)));
    }

    private static string? Staleness(string root, AdrInfo adr)
    {
        using var repo = new Repository(root);
        var docDate = LastChange(repo, adr.Path);
        var codeDate = adr.CodePaths.Select(p => LastChange(repo, p)).Max();
        if (docDate is null || codeDate is null || codeDate <= docDate + Grace) return null;
        var days = (codeDate.Value.Date - docDate.Value.Date).Days;
        return $"⚠️ STALE DOCUMENTATION: Underlying code was modified on {codeDate:yyyy-MM-dd} ({days} days after this document).";
    }

    /// <summary>Author date of the newest commit whose tree entry for the path (file or directory) differs from its parent's.</summary>
    private static DateTimeOffset? LastChange(Repository repo, string path)
    {
        if (repo.Head.Tip?.Tree[path] is null) return null;   // never existed or deleted: nothing to compare
        return Git.GitProvider.FileLog(repo, path).FirstOrDefault()?.Commit.Author.When;
    }
}

using Humanizer;
using LibGit2Sharp;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.Providers.Git;

public sealed record CommitInfo(string Sha7, string Author, DateTimeOffset When, string Subject);

/// <summary>Who last touched the spoken file, from the local .git.</summary>
public sealed class GitProvider(Func<string?> repoRoot) : IInsightProvider
{
    public Intent Handles => Intent.Git;

    public Task<Insight?> QueryAsync(Query query, CancellationToken ct)
    {
        var root = repoRoot();
        var match = query.Symbols
            .Where(m => m.Score >= SymbolMatcher.Strong)
            .OrderByDescending(m => m.Symbol.Kind == SymbolKind.File)
            .ThenByDescending(m => m.Score)
            .FirstOrDefault();
        if (root is null || match is null) return Task.FromResult<Insight?>(null);

        // ponytail: synchronous log walk; fine for repos with thousands of commits, revisit for monorepos.
        var history = History(root, match.Symbol.Path);
        if (history.Count == 0) return Task.FromResult<Insight?>(null);

        var latest = history[0];
        var earlier = history.Count - 1;
        var answer = $"{latest.Author} {latest.When.Humanize()}: \"{latest.Subject}\" ({latest.Sha7})";
        answer += earlier == 0
            ? "."
            : $"; {earlier} earlier {(earlier == 1 ? "change" : "changes")} by {string.Join(", ", history.Skip(1).Select(c => c.Author).Distinct())}.";

        return Task.FromResult<Insight?>(new Insight(
            Intent.Git, $"[GIT] {Path.GetFileName(match.Symbol.Path)}", answer, [new Jump(match.Symbol.AbsolutePath(root), 1)], match.Score));
    }

    public static IReadOnlyList<CommitInfo> History(string root, string relativePath)
    {
        using var repo = new Repository(root);
        return FileLog(repo, relativePath)
            .Take(5)
            .Select(e => new CommitInfo(e.Commit.Sha[..7], e.Commit.Author.Name, e.Commit.Author.When, e.Commit.MessageShort))
            .ToList();
    }

    /// <summary>Commits touching a path, newest first. Topological order stays stable when commit dates are not monotonic (rebases, clock skew).</summary>
    internal static IEnumerable<LogEntry> FileLog(Repository repo, string path) =>
        repo.Commits.QueryBy(path, new CommitFilter { SortBy = CommitSortStrategies.Topological });
}

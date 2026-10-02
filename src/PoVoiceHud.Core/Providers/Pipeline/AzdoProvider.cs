using Humanizer;
using LibGit2Sharp;
using Microsoft.VisualStudio.Services.Common;
using Polly;
using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Providers.Pipeline;

/// <summary>Latest pipeline run for the repo's current branch, with transient-failure retries.</summary>
public sealed class AzdoProvider(IBuildClient client, Func<string?> repoRoot, ResiliencePipeline? pipeline = null) : IInsightProvider
{
    private readonly ResiliencePipeline _pipeline = pipeline ?? Resilience.Default;

    public Intent Handles => Intent.Pipeline;

    public async Task<Insight?> QueryAsync(Query query, CancellationToken ct)
    {
        var branch = CurrentBranch(repoRoot()) ?? "main";
        IReadOnlyList<BuildRun> runs;
        try
        {
            runs = await _pipeline.ExecuteAsync(async token => await client.GetRecentBuildsAsync(branch, 3, token), ct);
        }
        catch (VssUnauthorizedException)
        {
            return Card("AzDO auth failed — check the PAT in Settings.", 0.3);
        }
        catch (Exception e) when (e is HttpRequestException or VssServiceException)
        {
            return Card($"AzDO unreachable: {e.Message}", 0.2);
        }

        if (runs.Count == 0) return Card($"No runs for {branch}.", 0.3);
        var run = runs[0];
        return new Insight(Intent.Pipeline, $"[AZDO] {run.Definition}", $"{branch}: {Describe(run)} · run #{run.Number}", [], 0.9, Link: run.WebUrl);
    }

    private static Insight Card(string message, double confidence) => new(Intent.Pipeline, "[AZDO] Pipelines", message, [], confidence);

    private static string Describe(BuildRun run)
    {
        var duration = run.Start is { } start && run.Finish is { } finish ? $" ({Short(finish - start)})" : "";
        return (run.Status.ToLowerInvariant(), run.Result?.ToLowerInvariant()) switch
        {
            ("inprogress", _) or (_, null) when run.Start is { } started => $"⏳ In progress, started {started.Humanize()}",
            (_, "succeeded") => $"✅ Succeeded{duration}",
            (_, "partiallysucceeded") => $"⚠️ Partially succeeded{duration}",
            (_, "failed") => run.FailedStage is null ? $"❌ Failed{duration}" : $"❌ Failed at stage {run.FailedStage}{duration}",
            (_, "canceled") => $"⛔ Canceled{duration}",
            _ => $"{run.Status} {run.Result}".Trim(),
        };
    }

    private static string Short(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m" : $"{t.Seconds}s";

    private static string? CurrentBranch(string? root)
    {
        if (root is null || !Repository.IsValid(root)) return null;
        using var repo = new Repository(root);
        return repo.Head.FriendlyName;
    }
}

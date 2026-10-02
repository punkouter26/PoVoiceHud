using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace PoVoiceHud.Core.Providers.Pipeline;

/// <summary>Azure DevOps Build API (YAML pipelines) over a PAT.</summary>
public sealed class AzdoBuildClient(Uri organization, string project, string pat) : IBuildClient, IDisposable
{
    private readonly VssConnection _connection = new(organization, new VssBasicCredential(string.Empty, pat));

    public async Task<IReadOnlyList<BuildRun>> GetRecentBuildsAsync(string branch, int top, CancellationToken ct)
    {
        var client = await _connection.GetClientAsync<BuildHttpClient>(ct);
        var builds = await client.GetBuildsAsync(project, branchName: $"refs/heads/{branch}", top: top,
            queryOrder: BuildQueryOrder.QueueTimeDescending, cancellationToken: ct);

        var runs = new List<BuildRun>();
        foreach (var build in builds)
        {
            string? failedStage = null;
            if (build.Result == BuildResult.Failed)
            {
                var timeline = await client.GetBuildTimelineAsync(project, build.Id, cancellationToken: ct);
                failedStage = timeline?.Records
                    .Where(r => r.Result == TaskResult.Failed && r.RecordType is "Stage" or "Job")
                    .OrderBy(r => r.Order)
                    .Select(r => r.Name)
                    .FirstOrDefault();
            }
            var web = build.Links.Links.TryGetValue("web", out var link) && link is ReferenceLink reference
                ? new Uri(reference.Href)
                : new Uri(organization, $"{project}/_build/results?buildId={build.Id}");
            runs.Add(new BuildRun(build.Id, build.BuildNumber, build.Definition.Name, build.Status?.ToString() ?? "unknown",
                build.Result?.ToString(), build.SourceBranch, build.SourceVersion, Utc(build.StartTime), Utc(build.FinishTime), web, failedStage));
        }
        return runs;
    }

    private static DateTimeOffset? Utc(DateTime? time) =>
        time is { } t ? new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)) : null;

    public void Dispose() => _connection.Dispose();
}

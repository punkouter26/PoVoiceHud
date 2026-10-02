using System.Net;
using Microsoft.VisualStudio.Services.Common;
using Polly;
using Polly.Retry;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Providers.Pipeline;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Tests.Fixtures;

namespace PoVoiceHud.Core.Tests.Providers;

public class AzdoProviderTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private static readonly Query Question = new("did ci pass on this branch", [], Intent.Pipeline);

    private static readonly ResiliencePipeline NoDelay = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3, Delay = TimeSpan.Zero, ShouldHandle = AzdoProvider.Transient })
        .Build();

    private static BuildRun Run(string status = "completed", string? result = "succeeded", string? failedStage = null, DateTimeOffset? start = null) =>
        new(42, "20261001.3", "PR Validation", status, result, "refs/heads/master", "a1b2c3d",
            start ?? DateTimeOffset.UtcNow.AddMinutes(-10), result is null ? null : (start ?? DateTimeOffset.UtcNow.AddMinutes(-10)).AddMinutes(8),
            new Uri("https://dev.azure.com/acme/PMIS/_build/results?buildId=42"), failedStage);

    private sealed class FakeClient(Func<string, IReadOnlyList<BuildRun>> answer) : IBuildClient
    {
        public List<string> Branches { get; } = [];
        public Task<IReadOnlyList<BuildRun>> GetRecentBuildsAsync(string branch, int top, CancellationToken ct)
        {
            Branches.Add(branch);
            return Task.FromResult(answer(branch));
        }
    }

    [Fact]
    public async Task Reports_the_latest_run_for_the_current_branch()
    {
        using var repo = TempRepo.WithSampleProject();
        var client = new FakeClient(_ => [Run()]);
        var provider = new AzdoProvider(client, () => repo.Root, NoDelay);

        var insight = await provider.QueryAsync(Question, _ct);

        Assert.NotNull(insight);
        Assert.Equal(["master"], client.Branches);
        Assert.Equal("[AZDO] PR Validation", insight.Headline);
        Assert.Equal("master: ✅ Succeeded (8m) · run #20261001.3", insight.Answer);
        Assert.Equal("https://dev.azure.com/acme/PMIS/_build/results?buildId=42", insight.Link?.ToString());
        Assert.Empty(insight.Jumps);
    }

    [Fact]
    public async Task Names_the_failed_stage()
    {
        using var repo = TempRepo.WithSampleProject();
        var provider = new AzdoProvider(new FakeClient(_ => [Run(result: "failed", failedStage: "Integration tests")]), () => repo.Root, NoDelay);

        var insight = await provider.QueryAsync(Question, _ct);

        Assert.NotNull(insight);
        Assert.Equal("master: ❌ Failed at stage Integration tests (8m) · run #20261001.3", insight.Answer);
    }

    [Fact]
    public async Task Describes_an_in_progress_run()
    {
        using var repo = TempRepo.WithSampleProject();
        var provider = new AzdoProvider(new FakeClient(_ => [Run(status: "inProgress", result: null, start: DateTimeOffset.UtcNow.AddMinutes(-3))]), () => repo.Root, NoDelay);

        var insight = await provider.QueryAsync(Question, _ct);

        Assert.NotNull(insight);
        Assert.Equal("master: ⏳ In progress, started 3 minutes ago · run #20261001.3", insight.Answer);
    }

    [Fact]
    public async Task Says_so_when_the_branch_has_no_runs()
    {
        using var repo = TempRepo.WithSampleProject();
        var provider = new AzdoProvider(new FakeClient(_ => []), () => repo.Root, NoDelay);

        var insight = await provider.QueryAsync(Question, _ct);

        Assert.NotNull(insight);
        Assert.Equal("No runs for master.", insight.Answer);
        Assert.True(insight.Confidence < 0.5);
    }

    [Fact]
    public async Task Reports_an_unauthorized_pat_without_retrying()
    {
        using var repo = TempRepo.WithSampleProject();
        var client = new FakeClient(_ => throw new VssUnauthorizedException("401"));
        var provider = new AzdoProvider(client, () => repo.Root, NoDelay);

        var insight = await provider.QueryAsync(Question, _ct);

        Assert.NotNull(insight);
        Assert.Equal("AzDO auth failed — check the PAT in Settings.", insight.Answer);
        Assert.Single(client.Branches);
    }

    [Fact]
    public async Task Retries_transient_http_failures()
    {
        using var repo = TempRepo.WithSampleProject();
        var attempts = 0;
        var client = new FakeClient(_ => ++attempts < 3 ? throw new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable) : [Run()]);
        var provider = new AzdoProvider(client, () => repo.Root, NoDelay);

        var insight = await provider.QueryAsync(Question, _ct);

        Assert.NotNull(insight);
        Assert.Equal(3, attempts);
        Assert.StartsWith("master: ✅", insight.Answer);
    }
}

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PoVoiceHud.Core.Asr;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Providers.Schema;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Tests.Fixtures;
using PoVoiceHud.Core.Tests.Vad;
using PoVoiceHud.Core.Vad;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.Tests;

[Trait("Category", "Integration")]
[Collection("Timing")]
public class HudPipelineTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Blocks inside TranscribeAsync until released, so tests can mute mid-transcription.</summary>
    private sealed class GatedTranscriber(string text) : ITranscriber
    {
        public TaskCompletionSource Started { get; } = new();
        public TaskCompletionSource Release { get; } = new();
        public int Calls { get; private set; }

        public async Task<string> TranscribeAsync(short[] pcm, CancellationToken ct)
        {
            Calls++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return text;
        }

        public void Dispose() { }
    }

    private static async Task<SileroVad> VadAsync()
    {
        await SileroVadTests.EnsureModelAsync();
        return new SileroVad(SileroVadTests.ModelPath);
    }

    private static Router RouterFor(TempRepo repo, params IInsightProvider[] providers) =>
        new(providers, () => SymbolIndex.Build(repo.Root), () => Settings.RepoSources, NullLogger<Router>.Instance);

    [Fact]
    public async Task Replayed_question_yields_a_schema_card_within_1200ms()
    {
        using var repo = TempRepo.WithSampleProject();
        var models = await ModelStore.EnsureAsync(null, _ct);
        using var vad = await VadAsync();
        using var transcriber = new WhisperTranscriber(models.Whisper, threads: 8);
        var audio = new FakeAudioSource(SileroVadTests.ReadFixture("tenant-question.wav"), TimeSpan.FromSeconds(1));
        using var pipeline = new HudPipeline(audio, vad, transcriber, RouterFor(repo, new SchemaProvider(() => repo.Root)), NullLogger<HudPipeline>.Instance);

        var sw = Stopwatch.StartNew();
        pipeline.Start();
        var routed = await pipeline.Insights.ReadAsync(_ct).AsTask().WaitAsync(Timeout, _ct);
        sw.Stop();

        var line = await pipeline.Transcript.ReadAsync(_ct).AsTask().WaitAsync(Timeout, _ct);
        TestContext.Current.TestOutputHelper?.WriteLine($"end-to-end {sw.ElapsedMilliseconds} ms; transcript: \"{line.Text}\"");
        Assert.Contains("tenant", line.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SourceHint.Teams, line.Source);
        Assert.Equal("[SCHEMA] Inspection.TenantId", routed.Insights[0].Headline);
        Assert.Equal(line, routed.Line);
        Assert.True(sw.ElapsedMilliseconds <= 1200, $"end-to-end took {sw.ElapsedMilliseconds} ms (SPEC criterion 4: ≤ 1200 ms)");
    }

    [Fact]
    public async Task Mute_discards_queued_audio_and_the_transcription_in_flight()
    {
        using var repo = TempRepo.WithSampleProject();
        using var vad = await VadAsync();
        var transcriber = new GatedTranscriber("does the inspection table have a tenant id foreign key");
        var audio = new FakeAudioSource(SileroVadTests.ReadFixture("tenant-question.wav"), TimeSpan.FromSeconds(1));
        var calls = 0;
        var provider = new FakeProvider(Intent.Schema, _ => { calls++; return new Insight(Intent.Schema, "x", "y", []); });
        using var pipeline = new HudPipeline(audio, vad, transcriber, RouterFor(repo, provider), NullLogger<HudPipeline>.Instance);

        pipeline.Start();
        await transcriber.Started.Task.WaitAsync(Timeout, _ct);
        pipeline.IsMuted = true;
        transcriber.Release.SetResult();
        await Task.Delay(300, _ct);

        Assert.True(audio.IsMuted);
        Assert.False(pipeline.Transcript.TryRead(out _));
        Assert.False(pipeline.Insights.TryRead(out _));
        Assert.Equal(0, calls);
        Assert.Equal(1, transcriber.Calls);
    }

    [Fact]
    public async Task Recheck_sends_the_last_sentence_to_the_providers_again_marked_fresh()
    {
        using var repo = TempRepo.WithSampleProject();
        using var vad = await VadAsync();
        var transcriber = new GatedTranscriber("does the inspection table have a tenant id foreign key");
        transcriber.Release.SetResult();
        var audio = new FakeAudioSource(SileroVadTests.ReadFixture("tenant-question.wav"), TimeSpan.FromSeconds(1));
        var seen = new System.Collections.Concurrent.ConcurrentQueue<Query>();
        var provider = new FakeProvider(Intent.Schema, q => { seen.Enqueue(q); return new Insight(Intent.Schema, "x", "y", []); });
        using var pipeline = new HudPipeline(audio, vad, transcriber, RouterFor(repo, provider), NullLogger<HudPipeline>.Instance);

        pipeline.Start();
        await pipeline.Insights.ReadAsync(_ct).AsTask().WaitAsync(Timeout, _ct);
        pipeline.Recheck();
        await pipeline.Insights.ReadAsync(_ct).AsTask().WaitAsync(Timeout, _ct);

        Assert.Equal([false, true], seen.Select(q => q.Fresh));
        Assert.True(await pipeline.Checking.ReadAsync(_ct).AsTask().WaitAsync(Timeout, _ct));    // busy while the providers work…
        Assert.False(await pipeline.Checking.ReadAsync(_ct).AsTask().WaitAsync(Timeout, _ct));   // …and released afterwards
    }

    [Fact]
    public async Task Blank_transcriptions_never_reach_the_transcript_or_router()
    {
        using var repo = TempRepo.WithSampleProject();
        using var vad = await VadAsync();
        var transcriber = new GatedTranscriber("");
        transcriber.Release.SetResult();
        var audio = new FakeAudioSource(SileroVadTests.ReadFixture("tenant-question.wav"), TimeSpan.FromSeconds(1));
        var calls = 0;
        var provider = new FakeProvider(Intent.Schema, _ => { calls++; return null; });
        using var pipeline = new HudPipeline(audio, vad, transcriber, RouterFor(repo, provider), NullLogger<HudPipeline>.Instance);

        pipeline.Start();
        await transcriber.Started.Task.WaitAsync(Timeout, _ct);
        await Task.Delay(300, _ct);

        Assert.False(pipeline.Transcript.TryRead(out _));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Status_from_the_audio_source_is_forwarded()
    {
        using var repo = TempRepo.WithSampleProject();
        using var vad = await VadAsync();
        var transcriber = new GatedTranscriber("");
        transcriber.Release.SetResult();
        var audio = new FakeAudioSource([], TimeSpan.Zero);
        using var pipeline = new HudPipeline(audio, vad, transcriber, RouterFor(repo), NullLogger<HudPipeline>.Instance);

        pipeline.Start();

        Assert.Equal("Replaying", await pipeline.Status.ReadAsync(_ct).AsTask().WaitAsync(Timeout, _ct));
    }

    [Fact]
    public async Task Container_wires_all_providers_with_fakes_for_unconfigured_services()
    {
        using var repo = TempRepo.WithSampleProject();
        await SileroVadTests.EnsureModelAsync();
        var services = new ServiceCollection();
        services.AddSingleton<IAudioSource>(new FakeAudioSource([], TimeSpan.Zero));
        services.AddSingleton<ITranscriber>(new GatedTranscriber(""));
        services.AddPoVoiceHudCore(new Settings { RepoPath = repo.Root }.ApplyMode(HudMode.Repo), new ModelPaths("unused", SileroVadTests.ModelPath));
        await using var container = services.BuildServiceProvider();

        var providers = container.GetRequiredService<IEnumerable<IInsightProvider>>().ToList();
        Assert.Equal(6, providers.Count);
        Assert.Contains(providers, p => p is Core.Providers.Code.CodeProvider);
        Assert.IsType<UnconfiguredProvider>(providers.Single(p => p.Handles == Intent.Pipeline));
        Assert.Contains(providers, p => p is SchemaProvider);
        Assert.Contains(providers, p => p is Core.Providers.FactCheck.FactCheckProvider);
        Assert.NotNull(container.GetRequiredService<HudPipeline>());
        Assert.NotNull(container.GetRequiredService<Core.Workspace.Workspace>());
    }

    [Fact]
    public async Task Simple_mode_container_needs_no_repo_and_routes_statements_to_fact_check()
    {
        await SileroVadTests.EnsureModelAsync();
        var services = new ServiceCollection();
        services.AddSingleton<IAudioSource>(new FakeAudioSource([], TimeSpan.Zero));
        services.AddSingleton<ITranscriber>(new GatedTranscriber(""));
        services.AddPoVoiceHudCore(new Settings(), new ModelPaths("unused", SileroVadTests.ModelPath));
        await using var container = services.BuildServiceProvider();

        Assert.Null(container.GetService<Core.Workspace.Workspace>());
        var router = container.GetRequiredService<Router>();
        var insights = await router.RouteAsync("The Eiffel Tower was completed in 1889", _ct);

        var card = Assert.Single(insights);
        Assert.Equal(Intent.FactCheck, card.Intent);
        Assert.StartsWith("⚠️ Unverified (no LLM configured)", card.Answer);   // no Foundry endpoint in this test's settings
        Assert.Empty(await router.RouteAsync("who modified the invoice service last week", _ct));
    }
}

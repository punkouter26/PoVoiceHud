using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PoVoiceHud.Core.Asr;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Providers.Docs;
using PoVoiceHud.Core.Providers.FactCheck;
using PoVoiceHud.Core.Providers.Fakes;
using PoVoiceHud.Core.Providers.Git;
using PoVoiceHud.Core.Providers.Pipeline;
using PoVoiceHud.Core.Providers.Schema;
using PoVoiceHud.Core.Providers.Web;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Vad;

namespace PoVoiceHud.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>Everything below the UI. Register IAudioSource/ITranscriber first to substitute fakes.</summary>
    public static IServiceCollection AddPoVoiceHudCore(this IServiceCollection services, Settings settings, ModelPaths models)
    {
        services.AddLogging();
        services.AddSingleton(settings);
        if (settings.RepoPath is { } root && Repository.IsValid(root))
            services.AddSingleton(_ => new Workspace.Workspace(root));

        services.TryAddSingleton<IAudioSource>(_ => new WasapiDualCapture(settings.AudioInput));
        services.TryAddSingleton(_ => new SileroVad(models.Silero));
        services.TryAddSingleton<ITranscriber>(_ => new WhisperTranscriber(models.Whisper, settings.WhisperThreads));

        services.AddSingleton<IInsightProvider>(sp => new GitProvider(() => sp.GetService<Workspace.Workspace>()?.Root));
        services.AddSingleton<IInsightProvider>(sp => new SchemaProvider(() => sp.GetService<Workspace.Workspace>()?.Root));
        services.AddSingleton<IInsightProvider>(sp => new DocsProvider(() => sp.GetService<Workspace.Workspace>()?.Root));

        var pat = CredentialStore.Read(CredentialStore.AzdoPat);
        if (settings is { AzdoOrgUrl: { } org, AzdoProject: { } project } && pat is not null)
            services.AddSingleton<IInsightProvider>(sp =>
                new AzdoProvider(new AzdoBuildClient(new Uri(org), project, pat), () => sp.GetService<Workspace.Workspace>()?.Root));
        else
            services.AddSingleton<IInsightProvider>(FakeProvider.Unconfigured(Intent.Pipeline, "Add your Azure DevOps org, project and PAT in Settings."));

        services.AddSingleton(new HttpClient());
        services.AddSingleton(sp => new BraveSearch(sp.GetRequiredService<HttpClient>(), () => CredentialStore.Read(CredentialStore.BraveKey)));
        services.AddSingleton<IInsightProvider>(sp => new BraveProvider(sp.GetRequiredService<BraveSearch>()));

        // A key is required for a cloud endpoint; a local (loopback) endpoint such as Ollama needs none.
        var foundryKey = CredentialStore.Read(CredentialStore.FoundryKey);
        var llmReady = FoundryClient.IsValidEndpoint(settings.FoundryEndpoint)
                       && (foundryKey is not null || new Uri(settings.FoundryEndpoint!).IsLoopback);
        services.AddSingleton<IInsightProvider>(sp => new FactCheckProvider(
            sp.GetRequiredService<BraveSearch>(),
            llmReady ? new FoundryClient(sp.GetRequiredService<HttpClient>(), new Uri(settings.FoundryEndpoint!), settings.FoundryModel, () => foundryKey) : null));

        services.AddSingleton(sp => new Router(
            sp.GetServices<IInsightProvider>(), () => sp.GetService<Workspace.Workspace>()?.Index, () => settings.EnabledSources,
            sp.GetRequiredService<ILogger<Router>>()));
        services.AddSingleton<HudPipeline>();
        return services;
    }
}

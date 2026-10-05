using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PoVoiceHud.Core.Asr;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Providers.Code;
using PoVoiceHud.Core.Providers.Docs;
using PoVoiceHud.Core.Providers.FactCheck;
using PoVoiceHud.Core.Providers.Git;
using PoVoiceHud.Core.Providers.Pipeline;
using PoVoiceHud.Core.Providers.Schema;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Vad;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Everything below the UI. Register IAudioSource/ITranscriber/HttpClient first to substitute your own.
    /// <c>settings.RepoPath</c> must already be a local folder here (see <see cref="RepoSource.EnsureLocal"/>).
    /// <paramref name="ollamaModel"/> is a model a running local Ollama offers; in front of a cloud model it drops chatter first.
    /// </summary>
    public static IServiceCollection AddPoVoiceHudCore(this IServiceCollection services, Settings settings, ModelPaths models, string? ollamaModel = null)
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

        services.TryAddSingleton(_ => new HttpClient());

        var pat = CredentialStore.Read(CredentialStore.AzdoPat);
        if (pat is not null && AzdoTarget(settings) is var (org, project))
            services.AddSingleton<IInsightProvider>(sp => new AzdoProvider(
                new AzdoBuildClient(sp.GetRequiredService<HttpClient>(), org, project, pat), () => sp.GetService<Workspace.Workspace>()?.Root));
        else
            services.AddSingleton<IInsightProvider>(new UnconfiguredProvider(Intent.Pipeline,
                "Pipelines need an Azure DevOps repository (its URL, or a folder cloned from one) and an access token in Settings."));

        // A key is required for a cloud endpoint; a local (loopback) endpoint such as Ollama needs none.
        var foundryKey = CredentialStore.Read(CredentialStore.FoundryKey);
        var llmReady = FoundryClient.IsValidEndpoint(settings.FoundryEndpoint)
                       && (foundryKey is not null || new Uri(settings.FoundryEndpoint!).IsLoopback);
        // The gate only makes sense in front of a cloud model: a local one already keeps everything on the machine.
        var gated = llmReady && ollamaModel is not null && !new Uri(settings.FoundryEndpoint!).IsLoopback;
        services.AddSingleton<IInsightProvider>(sp => new FactCheckProvider(
            llmReady ? new FoundryClient(sp.GetRequiredService<HttpClient>(), new Uri(settings.FoundryEndpoint!), settings.FoundryModel, foundryKey, FoundryClient.VerdictFormat) : null,
            gated ? new FoundryClient(sp.GetRequiredService<HttpClient>(), FoundryClient.OllamaEndpoint, ollamaModel!, apiKey: null, FoundryClient.VerdictFormat) : null));
        // Code research answers in plain sentences, so its client asks for no response schema.
        services.AddSingleton<IInsightProvider>(sp => new CodeProvider(
            () => sp.GetService<Workspace.Workspace>()?.Root,
            llmReady ? new FoundryClient(sp.GetRequiredService<HttpClient>(), new Uri(settings.FoundryEndpoint!), settings.FoundryModel, foundryKey) : null));

        services.AddSingleton(sp => new Router(
            sp.GetServices<IInsightProvider>(), () => sp.GetService<Workspace.Workspace>()?.Index, () => settings.EnabledSources,
            sp.GetRequiredService<ILogger<Router>>()));
        services.AddSingleton<HudPipeline>();
        return services;
    }

    /// <summary>
    /// The Azure DevOps organization and project to ask about pipelines: the ones written in settings.json if any,
    /// otherwise read from where the repository was cloned from.
    /// </summary>
    private static (Uri Organization, string Project)? AzdoTarget(Settings settings)
    {
        if (settings.AzdoProject is { } named && IsHttps(settings.AzdoOrgUrl, out var explicitOrg)) return (explicitOrg, named);
        if (settings.RepoPath is { } root && Repository.IsValid(root) && RepoSource.TryParseAzdo(RepoSource.OriginUrl(root), out var org, out var project, out _))
            return (org, project);
        return null;
    }

    /// <summary>A PAT is a password: it only ever goes to an https organization URL.</summary>
    public static bool IsHttps(string? url, out Uri uri) =>
        Uri.TryCreate(url, UriKind.Absolute, out uri!) && uri.Scheme == Uri.UriSchemeHttps;
}

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
using PoVoiceHud.Core.Sessions;
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
        if (settings.RepoPath is { } root && Repository.IsValid(root))
            services.AddSingleton(_ => new Workspace.Workspace(root));

        services.TryAddSingleton<IAudioSource>(_ => new WasapiDualCapture(settings.AudioInput));
        services.TryAddSingleton(_ => new SileroVad(models.Silero));
        services.TryAddSingleton<ITranscriber>(sp =>
        {
            // Whisper is told the words it should expect: the ones typed in settings, then the repository's own names,
            // as many as fit in PromptFor's short budget.
            var workspace = sp.GetService<Workspace.Workspace>();
            string? Prompt() => WhisperTranscriber.PromptFor((settings.Vocabulary ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Concat(workspace?.Index.Vocabulary() ?? []).Distinct(StringComparer.OrdinalIgnoreCase));
            var whisper = new WhisperTranscriber(models.Whisper, settings.WhisperThreads, Prompt());
            if (workspace is not null) workspace.Changed += () => whisper.Prompt = Prompt();
            return whisper;
        });

        static Func<string?> RepoRoot(IServiceProvider sp) => () => sp.GetService<Workspace.Workspace>()?.Root;
        services.AddSingleton<IInsightProvider>(sp => new GitProvider(RepoRoot(sp)));
        services.AddSingleton<IInsightProvider>(sp => new SchemaProvider(RepoRoot(sp)));
        services.AddSingleton<IInsightProvider>(sp => new DocsProvider(RepoRoot(sp)));

        services.TryAddSingleton(_ => new HttpClient());

        var pat = CredentialStore.Read(CredentialStore.AzdoPat);
        if (pat is not null && AzdoTarget(settings) is var (org, project))
            services.AddSingleton<IInsightProvider>(sp => new AzdoProvider(
                new AzdoBuildClient(sp.GetRequiredService<HttpClient>(), org, project, pat), RepoRoot(sp)));
        else
            services.AddSingleton<IInsightProvider>(new UnconfiguredProvider(Intent.Pipeline,
                "Pipelines need an Azure DevOps repository (its URL, or a folder cloned from one) and an access token in Settings."));

        // A key is required for a cloud endpoint; a local (loopback) endpoint such as Ollama needs none.
        var foundryKey = CredentialStore.Read(CredentialStore.FoundryKey);
        Uri? endpoint = FoundryClient.IsReady(settings.FoundryEndpoint, foundryKey is not null) ? new(settings.FoundryEndpoint!) : null;
        FoundryClient? Main(IServiceProvider sp, object? format) =>
            endpoint is null ? null : new FoundryClient(sp.GetRequiredService<HttpClient>(), endpoint, settings.FoundryModel, foundryKey, format);
        // The gate only makes sense in front of a cloud model: a local one already keeps everything on the machine.
        var gated = endpoint is { IsLoopback: false } && ollamaModel is not null;
        services.AddSingleton<IInsightProvider>(sp => new FactCheckProvider(
            Main(sp, FoundryClient.VerdictFormat),
            gated ? new FoundryClient(sp.GetRequiredService<HttpClient>(), FoundryClient.OllamaEndpoint, ollamaModel!, apiKey: null, FoundryClient.VerdictFormat) : null,
            settings.LookUpClaims ? new WikipediaEvidence(sp.GetRequiredService<HttpClient>()) : null));
        // Code research answers in plain sentences, so its client asks for no response schema.
        services.AddSingleton<IInsightProvider>(sp => new CodeProvider(RepoRoot(sp), Main(sp, format: null)));
        // A recap is a page, not a sentence: more tokens, and time for a local model to write them.
        services.AddSingleton(sp => new RecapWriter(endpoint is null ? null
            : new FoundryClient(sp.GetRequiredService<HttpClient>(), endpoint, settings.FoundryModel, foundryKey, maxTokens: 1200, timeout: TimeSpan.FromSeconds(120))));

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
        if (settings.AzdoProject is { } named && RepoSource.IsHttps(settings.AzdoOrgUrl, out var explicitOrg)) return (explicitOrg, named);
        if (settings.RepoPath is { } root && Repository.IsValid(root) && RepoSource.TryParseAzdo(RepoSource.OriginUrl(root), out var org, out var project, out _))
            return (org, project);
        return null;
    }
}

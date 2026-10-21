using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PoVoiceHud.Core.Asr;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers.FactCheck;
using PoVoiceHud.Core.Repos;
using PoVoiceHud.Core.Vad;

namespace PoVoiceHud.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>Everything below the UI. Register IAudioSource/ITranscriber/HttpClient first to substitute your own.</summary>
    /// <param name="repoRoot">The local copy of <see cref="Settings.Repository"/> (<see cref="RepoSource.Open"/>), or null for none.</param>
    public static IServiceCollection AddPoVoiceHudCore(this IServiceCollection services, Settings settings, ModelPaths models, string? repoRoot = null)
    {
        services.AddLogging();
        services.TryAddSingleton<IAudioSource>(_ => new WasapiDualCapture(AudioInput.Both));   // you and the call, always
        services.TryAddSingleton(_ => new SileroVad(models.Silero));
        if (repoRoot is not null)
        {
            var location = settings.Repository ?? repoRoot;
            services.AddSingleton(_ => new CodeRepository(repoRoot, RepoSource.NameOf(location), path => RepoSource.Link(location, repoRoot, path)));
        }
        // Whisper is told the words it should expect: the ones in settings.json, then the repository's name and names, as many as fit in PromptFor's short budget.
        services.TryAddSingleton<ITranscriber>(sp => new WhisperTranscriber(models.Whisper, settings.WhisperThreads,
            WhisperTranscriber.PromptFor((settings.Vocabulary ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Concat(sp.GetService<CodeRepository>() is { } code ? [.. SymbolMatcher.Tokens(code.Name).Where(t => t.Length >= 3).Select(Capitalised), .. code.Index.Vocabulary()] : []))));
        services.TryAddSingleton(_ => new HttpClient());
        services.AddSingleton(sp => CreateChecker(settings, sp.GetRequiredService<HttpClient>()));
        services.AddSingleton(sp => new HudPipeline(sp.GetRequiredService<IAudioSource>(), sp.GetRequiredService<SileroVad>(), sp.GetRequiredService<ITranscriber>(),
            sp.GetRequiredService<IFactChecker>(), sp.GetRequiredService<ILogger<HudPipeline>>())
        {
            Code = CreateCodeAnswerer(settings, sp.GetRequiredService<HttpClient>(), sp.GetService<CodeRepository>()),
        });
        return services;
    }

    /// <summary>
    /// The fact checker for these settings and the stored key. Also used on its own when only the checker's settings change,
    /// so Whisper and the audio keep running. Without an endpoint and key every check reports that setup is needed.
    /// </summary>
    public static IFactChecker CreateChecker(Settings settings, HttpClient http) =>
        new FactChecker(Model(settings, http), settings.LookUpClaims ? new WikipediaEvidence(http) : null);

    /// <summary>What answers and checks sentences about the code, or null without a repository. Rebuilt with the checker.</summary>
    public static CodeAnswerer? CreateCodeAnswerer(Settings settings, HttpClient http, CodeRepository? code) =>
        code is null ? null : new CodeAnswerer(Model(settings, http, CodeAnswerer.ReplyFormat), code);

    private static FoundryClient? Model(Settings settings, HttpClient http, object? replyFormat = null)
    {
        var key = CredentialStore.Read(CredentialStore.FoundryKey);
        return FoundryClient.IsReady(settings.FoundryEndpoint, key is not null)
            ? new FoundryClient(http, new Uri(settings.FoundryEndpoint!), key, responseFormat: replyFormat)
            : null;
    }

    private static string Capitalised(string word) => char.ToUpperInvariant(word[0]) + word[1..];
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PoVoiceHud.Core.Asr;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers.FactCheck;
using PoVoiceHud.Core.Vad;

namespace PoVoiceHud.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>Everything below the UI. Register IAudioSource/ITranscriber/HttpClient first to substitute your own.</summary>
    public static IServiceCollection AddPoVoiceHudCore(this IServiceCollection services, Settings settings, ModelPaths models)
    {
        services.AddLogging();
        services.TryAddSingleton<IAudioSource>(_ => new WasapiDualCapture(settings.AudioInput));
        services.TryAddSingleton(_ => new SileroVad(models.Silero));
        // Whisper is told the words it should expect, as many of the ones in settings.json as fit in PromptFor's short budget.
        services.TryAddSingleton<ITranscriber>(_ => new WhisperTranscriber(models.Whisper, settings.WhisperThreads,
            WhisperTranscriber.PromptFor((settings.Vocabulary ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))));
        services.TryAddSingleton(_ => new HttpClient());
        services.AddSingleton(sp => CreateChecker(settings, sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<HudPipeline>();
        return services;
    }

    /// <summary>
    /// The fact checker for these settings and the stored key. Also used on its own when only the checker's settings change,
    /// so Whisper and the audio keep running. Without an endpoint and key every check reports that setup is needed.
    /// </summary>
    public static IFactChecker CreateChecker(Settings settings, HttpClient http)
    {
        var key = CredentialStore.Read(CredentialStore.FoundryKey);
        var model = FoundryClient.IsReady(settings.FoundryEndpoint, key is not null) ? new FoundryClient(http, new Uri(settings.FoundryEndpoint!), key) : null;
        return new FactChecker(model, settings.LookUpClaims ? new WikipediaEvidence(http) : null);
    }
}

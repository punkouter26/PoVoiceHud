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

        // Without an endpoint and a key every claim gets a "can't tell" that says what to set up.
        var key = CredentialStore.Read(CredentialStore.FoundryKey);
        Uri? endpoint = FoundryClient.IsReady(settings.FoundryEndpoint, key is not null) ? new(settings.FoundryEndpoint!) : null;
        services.AddSingleton<IFactChecker>(sp => new FactChecker(
            endpoint is null ? null : new FoundryClient(sp.GetRequiredService<HttpClient>(), endpoint, key),
            settings.LookUpClaims ? new WikipediaEvidence(sp.GetRequiredService<HttpClient>()) : null));
        services.AddSingleton<HudPipeline>();
        return services;
    }
}

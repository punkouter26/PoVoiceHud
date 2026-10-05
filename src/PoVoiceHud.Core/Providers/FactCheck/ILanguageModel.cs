namespace PoVoiceHud.Core.Providers.FactCheck;

public interface ILanguageModel
{
    /// <summary>One chat turn: system + user prompt → assistant text. Throws HttpRequestException on transport/HTTP failure, TimeoutException when no reply arrives in time.</summary>
    Task<string> CompleteAsync(string system, string user, CancellationToken ct);
}

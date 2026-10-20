using System.Globalization;
using System.Net;

namespace PoVoiceHud.Core.Providers.FactCheck;

/// <summary>
/// Why claims cannot be checked right now: no endpoint or key, a refused key, no deployment, or a service that is down or slow.
/// The pipeline shows it once in the header instead of a "can't tell" on every line.
/// </summary>
public sealed class FactCheckUnavailableException : Exception
{
    public FactCheckUnavailableException() { }

    public FactCheckUnavailableException(string message) : base(message) { }

    public FactCheckUnavailableException(string message, Exception? inner) : base(message, inner) { }

    /// <summary>The endpoint or key is wrong (rather than the service being down): only Settings can fix it.</summary>
    public bool NeedsSetup { get; init; }
}

/// <summary>One round trip to the model, so a wrong endpoint or key shows up before the app relies on it.</summary>
public static class Connection
{
    public const string NotSetUp = "Add the Azure AI Foundry endpoint and key in Settings.";

    /// <summary>Null when the model answered; otherwise what went wrong, in words for the person setting it up.</summary>
    public static async Task<FactCheckUnavailableException?> TestAsync(ILanguageModel model, CancellationToken ct)
    {
        try
        {
            await model.CompleteAsync("Reply with the single word OK.", "ping", ct);
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or TimeoutException)
        {
            return Describe(e);
        }
    }

    /// <summary>A failed model call as a <see cref="FactCheckUnavailableException"/> that says what to do about it.</summary>
    public static FactCheckUnavailableException Describe(Exception e) => e switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } h =>
            new($"The key was refused (HTTP {(int)h.StatusCode!}).", e) { NeedsSetup = true },
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } =>
            new($"No {FoundryClient.Model} deployment at that endpoint (HTTP 404).", e) { NeedsSetup = true },
        HttpRequestException { StatusCode: { } code } => new($"The fact-check service answered HTTP {((int)code).ToString(CultureInfo.InvariantCulture)}.", e),
        HttpRequestException => new($"Could not reach the fact-check service: {e.Message}", e),
        _ => new(e.Message, e),   // a TimeoutException already says how long it waited
    };
}

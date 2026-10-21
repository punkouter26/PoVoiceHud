using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Polly;

namespace PoVoiceHud.Core.Providers.FactCheck;

/// <summary>The resource's content filter refused this one prompt; the next sentence may well go through.</summary>
public sealed class ContentFilteredException : Exception
{
    public ContentFilteredException() { }

    public ContentFilteredException(string message) : base(message) { }

    public ContentFilteredException(string message, Exception? inner) : base(message, inner) { }
}

/// <summary>
/// Chat completions on an Azure AI Foundry resource: the resource root (https://&lt;resource&gt;.services.ai.azure.com) gets
/// /models/chat/completions?api-version=… appended and the key goes in the api-key header. The model is fixed, and the
/// reply is held to the verdict schema. Each attempt has its own time limit and transient failures are retried.
/// </summary>
/// <param name="responseFormat">The JSON schema the reply is held to: the fact checker's verdict unless another is given (<see cref="Repos.CodeAnswerer.ReplyFormat"/>).</param>
public sealed class FoundryClient(HttpClient http, Uri endpoint, string? apiKey, ResiliencePipeline? retry = null, TimeSpan? timeout = null,
    object? responseFormat = null) : ILanguageModel
{
    /// <summary>
    /// ~1–1.5 s per verdict on a GlobalStandard deployment, and it honours the JSON schema natively. The resource must have
    /// a deployment of this name.
    /// </summary>
    public const string Model = "gpt-4.1-mini";

    private const string ApiVersion = "2024-05-01-preview";
    private const int MaxTokens = 300;

    /// <summary>The verdict shape <see cref="FactChecker"/> reads; the server enforces it.</summary>
    private static readonly object VerdictFormat = new
    {
        type = "json_schema",
        json_schema = new
        {
            name = "verdict",
            strict = true,
            schema = new
            {
                type = "object",
                properties = new
                {
                    verdict = new { type = "string", @enum = new[] { "supported", "contradicted", "unverified", "not_a_claim" } },
                    claim = new { type = "string" },
                    reason = new { type = "string" },
                    source = new { type = "string" },
                },
                required = new[] { "verdict", "claim", "reason", "source" },
                additionalProperties = false,
            },
        },
    };

    private readonly ResiliencePipeline _retry = retry ?? Resilience.Default;
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(8);
    private readonly object _format = responseFormat ?? VerdictFormat;

    /// <summary>The resource root: an https URL.</summary>
    public static bool IsValidEndpoint(string? endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == "https";

    /// <summary>Usable as configured: a valid endpoint and a stored key.</summary>
    public static bool IsReady(string? endpoint, bool hasKey) => IsValidEndpoint(endpoint) && hasKey;

    public static Uri Resolve(Uri endpoint) =>
        endpoint.AbsolutePath is "" or "/" ? new Uri(endpoint, $"/models/chat/completions?api-version={ApiVersion}") : endpoint;

    /// <summary>Throws HttpRequestException on transport/HTTP failure and TimeoutException when an attempt gets no reply in time.</summary>
    public async Task<string> CompleteAsync(string system, string user, CancellationToken ct) =>
        await _retry.ExecuteAsync(async token =>
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(_timeout);
            try
            {
                var body = new Dictionary<string, object>
                {
                    ["model"] = Model,
                    ["messages"] = new object[] { new { role = "system", content = system }, new { role = "user", content = user } },
                    ["temperature"] = 0,
                    ["max_tokens"] = MaxTokens,
                    ["response_format"] = _format,
                };
                using var request = new HttpRequestMessage(HttpMethod.Post, Resolve(endpoint)) { Content = JsonContent.Create(body) };
                if (!string.IsNullOrEmpty(apiKey)) request.Headers.Add("api-key", apiKey);
                using var response = await http.SendAsync(request, limit.Token);
                return await ReadAsync(response, limit.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException($"No reply from {endpoint.Host} within {_timeout.TotalSeconds:0} s.");
            }
        }, ct);

    /// <summary>The assistant text, or "" when the body is not the expected shape (the caller treats that as unreadable).</summary>
    private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        // Azure's content filter refuses some sentences (one that sounds like a jailbreak, say) with a 400: about that sentence, not the service.
        if (response.StatusCode == HttpStatusCode.BadRequest && (await response.Content.ReadAsStringAsync(ct)).Contains("\"content_filter\"", StringComparison.Ordinal))
            throw new ContentFilteredException("Azure's content filter refused the sentence.");
        response.EnsureSuccessStatusCode();
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return body.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
                   && choices[0].TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)
                   && content.ValueKind == JsonValueKind.String
                ? content.GetString()!
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Polly;

namespace PoVoiceHud.Core.Providers.FactCheck;

/// <summary>
/// OpenAI-style chat completions. The endpoint is either an Azure AI Foundry resource root
/// (https://&lt;resource&gt;.services.ai.azure.com → /models/chat/completions?api-version=… is appended, api-key header)
/// or a full URL such as Ollama's http://localhost:11434/v1/chat/completions (used as-is).
/// The key is never sent to a loopback endpoint, so a stored Azure key stays off the local wire.
/// Each attempt has its own time limit and transient failures are retried. A verdict is short; a recap passes a larger
/// <paramref name="maxTokens"/> and a longer <paramref name="timeout"/>.
/// </summary>
public sealed class FoundryClient(HttpClient http, Uri endpoint, string model, string? apiKey, object? responseFormat = null, ResiliencePipeline? retry = null,
    int maxTokens = 300, TimeSpan? timeout = null) : ILanguageModel
{
    private const string ApiVersion = "2024-05-01-preview";

    public static readonly Uri OllamaEndpoint = new("http://localhost:11434/v1/chat/completions");
    public const string OllamaDefaultModel = "llama3.2";

    /// <summary>The verdict shape FactCheckProvider asks for: pass it as responseFormat and the server enforces it where the deployment can.</summary>
    public static readonly object VerdictFormat = new
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
                    reason = new { type = "string" },
                    source = new { type = "string" },
                },
                required = new[] { "verdict", "reason", "source" },
                additionalProperties = false,
            },
        },
    };

    private readonly ResiliencePipeline _retry = retry ?? Resilience.Default;
    private bool _structured = responseFormat is not null;   // turned off the first time the deployment rejects response_format

    // A local model may still be loading into memory on its first call; a cloud endpoint has no such excuse.
    private TimeSpan Timeout => timeout ?? TimeSpan.FromSeconds(endpoint.IsLoopback ? 30 : 8);

    /// <summary>https anywhere, or http on localhost only (local Ollama).</summary>
    public static bool IsValidEndpoint(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback));

    /// <summary>A loopback endpoint such as local Ollama: it needs no key and keeps every sentence on the machine.</summary>
    public static bool IsLocal(string? endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.IsLoopback;

    /// <summary>Usable as configured: a valid endpoint, and a key unless it is local.</summary>
    public static bool IsReady(string? endpoint, bool hasKey) => IsValidEndpoint(endpoint) && (hasKey || IsLocal(endpoint));

    public static Uri Resolve(Uri endpoint) =>
        endpoint.AbsolutePath is "" or "/" ? new Uri(endpoint, $"/models/chat/completions?api-version={ApiVersion}") : endpoint;

    /// <summary>The first model a local Ollama has pulled, or null when Ollama is not running (asked with a 1 s limit).</summary>
    public static async Task<string?> OllamaModelAsync(HttpClient http, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            var tags = await http.GetFromJsonAsync<JsonElement>(new Uri(OllamaEndpoint, "/api/tags"), limit.Token);
            return tags.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array && models.GetArrayLength() > 0
                   && models[0].TryGetProperty("name", out var name)
                ? name.GetString()
                : null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }

    /// <summary>Throws HttpRequestException on transport/HTTP failure and TimeoutException when an attempt gets no reply in time.</summary>
    public async Task<string> CompleteAsync(string system, string user, CancellationToken ct) =>
        await _retry.ExecuteAsync(async token =>
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(Timeout);
            try
            {
                using var response = await SendAsync(system, user, _structured, limit.Token);
                if (response.StatusCode != HttpStatusCode.BadRequest || !_structured) return await ReadAsync(response, limit.Token);

                // Some deployments (e.g. Llama on Foundry) only produce plain text: ask again without the schema and stop sending it.
                _structured = false;
                using var plain = await SendAsync(system, user, structured: false, limit.Token);
                return await ReadAsync(plain, limit.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException($"No reply from {endpoint.Host} within {Timeout.TotalSeconds:0} s.");
            }
        }, ct);

    private async Task<HttpResponseMessage> SendAsync(string system, string user, bool structured, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new object[] { new { role = "system", content = system }, new { role = "user", content = user } },
            ["temperature"] = 0,
            ["max_tokens"] = maxTokens,
        };
        if (structured) body["response_format"] = responseFormat!;

        using var request = new HttpRequestMessage(HttpMethod.Post, Resolve(endpoint)) { Content = JsonContent.Create(body) };
        if (!string.IsNullOrEmpty(apiKey) && !endpoint.IsLoopback) request.Headers.Add("api-key", apiKey);
        return await http.SendAsync(request, ct);
    }

    /// <summary>The assistant text, or "" when the body is not the expected shape (the caller treats that as unreadable).</summary>
    private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
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

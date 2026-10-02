using System.Net.Http.Json;
using System.Text.Json;

namespace PoVoiceHud.Core.Providers.FactCheck;

/// <summary>
/// OpenAI-style chat completions. The endpoint is either an Azure AI Foundry resource root
/// (https://&lt;resource&gt;.services.ai.azure.com → /models/chat/completions?api-version=… is appended, api-key header)
/// or a full URL such as Ollama's http://localhost:11434/v1/chat/completions (used as-is).
/// The key is never sent to a loopback endpoint, so a stored Azure key stays off the local wire.
/// </summary>
public sealed class FoundryClient(HttpClient http, Uri endpoint, string model, string? apiKey) : ILanguageModel
{
    private const string ApiVersion = "2024-05-01-preview";

    public static readonly Uri OllamaEndpoint = new("http://localhost:11434/v1/chat/completions");
    public const string OllamaDefaultModel = "llama3.2";

    /// <summary>https anywhere, or http on localhost only (local Ollama).</summary>
    public static bool IsValidEndpoint(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback));

    public static Uri Resolve(Uri endpoint) =>
        endpoint.AbsolutePath is "" or "/" ? new Uri(endpoint, $"/models/chat/completions?api-version={ApiVersion}") : endpoint;

    public async Task<string> CompleteAsync(string system, string user, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Resolve(endpoint))
        {
            Content = JsonContent.Create(new
            {
                model,
                messages = new object[] { new { role = "system", content = system }, new { role = "user", content = user } },
                temperature = 0,
                max_tokens = 300,
            }),
        };
        if (!string.IsNullOrEmpty(apiKey) && !endpoint.IsLoopback) request.Headers.Add("api-key", apiKey);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}

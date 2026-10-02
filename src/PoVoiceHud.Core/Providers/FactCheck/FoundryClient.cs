using System.Net.Http.Json;
using System.Text.Json;

namespace PoVoiceHud.Core.Providers.FactCheck;

/// <summary>
/// Azure AI Foundry serverless chat completions (OpenAI-compatible):
/// POST {endpoint}/models/chat/completions?api-version=2024-05-01-preview with an api-key header.
/// Works for any catalog model (Llama, Phi, Mistral, GPT) deployed in the user's project.
/// </summary>
public sealed class FoundryClient(HttpClient http, Uri endpoint, string model, Func<string?> apiKey) : ILanguageModel
{
    private const string ApiVersion = "2024-05-01-preview";

    public async Task<string> CompleteAsync(string system, string user, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, $"/models/chat/completions?api-version={ApiVersion}"))
        {
            Content = JsonContent.Create(new
            {
                model,
                messages = new object[] { new { role = "system", content = system }, new { role = "user", content = user } },
                temperature = 0,
                max_tokens = 300,
            }),
        };
        request.Headers.Add("api-key", apiKey() ?? "");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return body.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}

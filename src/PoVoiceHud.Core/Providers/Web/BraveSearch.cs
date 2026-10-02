using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Polly;
using Polly.Retry;

namespace PoVoiceHud.Core.Providers.Web;

/// <summary>Status is Unused when no key is configured (nothing was sent).</summary>
public sealed record BraveOutcome(HttpStatusCode Status, IReadOnlyList<BraveResult> Results)
{
    public static readonly BraveOutcome NoKey = new(HttpStatusCode.Unused, []);
}

/// <summary>One Brave web search with retries; descriptions come back as plain text.</summary>
public sealed partial class BraveSearch(HttpClient http, Func<string?> apiKey, ResiliencePipeline? pipeline = null)
{
    private const string Endpoint = "https://api.search.brave.com/res/v1/web/search";

    public static readonly PredicateBuilder<object> Transient = new PredicateBuilder()
        .Handle<HttpRequestException>(e => e.StatusCode is null or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError);

    private static readonly ResiliencePipeline Default = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(500),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = Transient,
        })
        .Build();

    private readonly ResiliencePipeline _pipeline = pipeline ?? Default;

    public bool HasKey => !string.IsNullOrWhiteSpace(apiKey());

    /// <summary>Throws HttpRequestException once retries are exhausted; 401/403 come back as a status with no results.</summary>
    public async Task<BraveOutcome> SearchAsync(string query, int count, CancellationToken ct)
    {
        var key = apiKey();
        if (string.IsNullOrWhiteSpace(key)) return BraveOutcome.NoKey;

        return await _pipeline.ExecuteAsync(async token =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}?q={WebUtility.UrlEncode(query)}&count={count}");
            request.Headers.Add("X-Subscription-Token", key);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new BraveOutcome(response.StatusCode, []);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<BraveResponse>(token);
            var results = body?.Web?.Results?.Select(r => r with { Description = Plain(r.Description ?? "") }).ToList() ?? [];
            return new BraveOutcome(response.StatusCode, results);
        }, ct);
    }

    private static string Plain(string html) => WebUtility.HtmlDecode(Tag().Replace(html, "")).Trim();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();
}

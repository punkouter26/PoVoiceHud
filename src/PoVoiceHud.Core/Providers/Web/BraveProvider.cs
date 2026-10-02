using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Polly;
using Polly.Retry;
using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Providers.Web;

/// <summary>
/// Brave Search for questions nothing in the repo answers. Only the spoken question leaves the machine, and only
/// when a key is configured; otherwise (or on any failure) the card carries a [Search] link for the browser.
/// </summary>
public sealed partial class BraveProvider(HttpClient http, Func<string?> apiKey, ResiliencePipeline? pipeline = null) : IInsightProvider
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

    public Intent Handles => Intent.Web;

    public async Task<Insight?> QueryAsync(Query query, CancellationToken ct)
    {
        var encoded = WebUtility.UrlEncode(query.Text);
        var searchLink = new Uri($"https://duckduckgo.com/?q={encoded}");
        var key = apiKey();
        if (string.IsNullOrWhiteSpace(key))
            return Fallback(query.Text, "No search key configured — [Search] opens your browser.", searchLink);

        try
        {
            var (status, body) = await _pipeline.ExecuteAsync(async token =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}?q={encoded}&count=3");
                request.Headers.Add("X-Subscription-Token", key);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await http.SendAsync(request, token);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return (response.StatusCode, (BraveResponse?)null);
                response.EnsureSuccessStatusCode();
                return (response.StatusCode, await response.Content.ReadFromJsonAsync<BraveResponse>(token));
            }, ct);

            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Fallback(query.Text, "Brave rejected the API key — check Settings; [Search] opens your browser.", searchLink);
            var top = body?.Web?.Results?.FirstOrDefault();
            if (top is null)
                return Fallback(query.Text, "No web results — [Search] opens your browser.", searchLink);
            return new Insight(Intent.Web, $"[WEB] {top.Title}", Plain(top.Description ?? ""), [], 0.6, Link: new Uri(top.Url));
        }
        catch (HttpRequestException)
        {
            return Fallback(query.Text, "Web search unavailable — [Search] opens your browser.", searchLink);
        }
    }

    private static Insight Fallback(string text, string message, Uri link) =>
        new(Intent.Web, $"[WEB] {text}", message, [], 0.3, Link: link);

    private static string Plain(string html) => WebUtility.HtmlDecode(Tag().Replace(html, "")).Trim();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();
}

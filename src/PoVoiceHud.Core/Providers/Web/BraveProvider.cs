using System.Net;
using Polly;
using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Providers.Web;

/// <summary>
/// Brave Search for questions nothing in the repo answers. Only the spoken question leaves the machine, and only
/// when a key is configured; otherwise (or on any failure) the card carries a [Search] link for the browser.
/// </summary>
public sealed class BraveProvider(BraveSearch search) : IInsightProvider
{
    public BraveProvider(HttpClient http, Func<string?> apiKey, ResiliencePipeline? pipeline = null)
        : this(new BraveSearch(http, apiKey, pipeline)) { }

    public static PredicateBuilder<object> Transient => BraveSearch.Transient;

    public Intent Handles => Intent.Web;

    public async Task<Insight?> QueryAsync(Query query, CancellationToken ct)
    {
        var searchLink = new Uri($"https://duckduckgo.com/?q={WebUtility.UrlEncode(query.Text)}");
        if (!search.HasKey)
            return Fallback(query.Text, "No search key configured — [Search] opens your browser.", searchLink);

        try
        {
            var outcome = await search.SearchAsync(query.Text, 3, ct);
            if (outcome.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Fallback(query.Text, "Brave rejected the API key — check Settings; [Search] opens your browser.", searchLink);
            var top = outcome.Results.FirstOrDefault();
            if (top is null)
                return Fallback(query.Text, "No web results — [Search] opens your browser.", searchLink);
            return new Insight(Intent.Web, $"[WEB] {top.Title}", top.Description ?? "", [], 0.6, Link: new Uri(top.Url));
        }
        catch (HttpRequestException)
        {
            return Fallback(query.Text, "Web search unavailable — [Search] opens your browser.", searchLink);
        }
    }

    private static Insight Fallback(string text, string message, Uri link) =>
        new(Intent.Web, $"[WEB] {text}", message, [], 0.3, Link: link);
}

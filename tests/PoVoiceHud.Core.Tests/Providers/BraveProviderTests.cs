using System.Net;
using Polly;
using Polly.Retry;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Providers.Web;
using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Tests.Providers;

public class BraveProviderTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private static readonly Query Question = new("what does IAsyncDisposable do again", [], Intent.Web);

    private static readonly ResiliencePipeline NoDelay = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3, Delay = TimeSpan.Zero, ShouldHandle = BraveProvider.Transient })
        .Build();

    private const string Response = """
        {
          "query": { "original": "what does IAsyncDisposable do again", "more_results_available": true },
          "web": {
            "type": "search",
            "results": [
              {
                "title": "IAsyncDisposable Interface (System) | Microsoft Learn",
                "url": "https://learn.microsoft.com/dotnet/api/system.iasyncdisposable",
                "description": "Provides a mechanism for releasing unmanaged resources <strong>asynchronously</strong>.",
                "age": "2026-03-01"
              },
              { "title": "Second", "url": "https://example.com/2", "description": "ignored" }
            ]
          }
        }
        """;

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static (BraveProvider Provider, FakeHandler Handler) Build(string? key, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        return (new BraveProvider(new HttpClient(handler), () => key, NoDelay), handler);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Without_a_key_offers_a_search_link_and_makes_no_request()
    {
        var (provider, handler) = Build(null, _ => throw new InvalidOperationException("must not be called"));

        var insight = await provider.QueryAsync(Question, _ct);

        Assert.NotNull(insight);
        Assert.Equal(Intent.Web, insight.Intent);
        Assert.Equal("[WEB] what does IAsyncDisposable do again", insight.Headline);
        Assert.Equal("https://duckduckgo.com/?q=what+does+IAsyncDisposable+do+again", insight.Link?.ToString());
        Assert.Contains("[Search]", insight.Answer);
        Assert.Empty(handler.Requests);
        Assert.True(insight.Confidence < 0.5);
    }

    [Fact]
    public async Task Parses_the_top_result_and_sends_the_key_header()
    {
        var (provider, handler) = Build("brave-key", _ => Json(Response));

        var insight = await provider.QueryAsync(Question, _ct);

        Assert.NotNull(insight);
        Assert.Equal("[WEB] IAsyncDisposable Interface (System) | Microsoft Learn", insight.Headline);
        Assert.Equal("Provides a mechanism for releasing unmanaged resources asynchronously.", insight.Answer);
        Assert.Equal("https://learn.microsoft.com/dotnet/api/system.iasyncdisposable", insight.Link?.ToString());
        var request = Assert.Single(handler.Requests);
        Assert.Equal("brave-key", request.Headers.GetValues("X-Subscription-Token").Single());
        Assert.StartsWith("https://api.search.brave.com/res/v1/web/search?", request.RequestUri?.ToString());
        Assert.Contains("q=what+does+IAsyncDisposable+do+again", request.RequestUri?.Query);
    }

    [Fact]
    public async Task Rate_limiting_falls_back_to_the_search_link_after_retries()
    {
        var (provider, handler) = Build("brave-key", _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var insight = await provider.QueryAsync(Question, _ct);

        Assert.NotNull(insight);
        Assert.Equal("https://duckduckgo.com/?q=what+does+IAsyncDisposable+do+again", insight.Link?.ToString());
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task A_rejected_key_says_so_without_retrying()
    {
        var (provider, handler) = Build("bad-key", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var insight = await provider.QueryAsync(Question, _ct);

        Assert.NotNull(insight);
        Assert.Contains("key", insight.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(insight.Link);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task No_results_falls_back_to_the_search_link()
    {
        var (provider, _) = Build("brave-key", _ => Json("""{ "web": { "results": [] } }"""));

        var insight = await provider.QueryAsync(Question, _ct);

        Assert.NotNull(insight);
        Assert.Contains("No web results", insight.Answer);
        Assert.NotNull(insight.Link);
    }
}

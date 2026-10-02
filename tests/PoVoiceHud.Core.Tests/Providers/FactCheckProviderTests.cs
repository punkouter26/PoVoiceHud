using System.Net;
using System.Text.Json;
using Polly;
using Polly.Retry;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Providers.FactCheck;
using PoVoiceHud.Core.Providers.Web;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Tests.Fixtures;

namespace PoVoiceHud.Core.Tests.Providers;

public class FactCheckProviderTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private const string Claim = "The Eiffel Tower was completed in 1889";
    private static readonly Query Statement = new(Claim, [], Intent.FactCheck);

    private static readonly ResiliencePipeline NoDelay = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3, Delay = TimeSpan.Zero, ShouldHandle = Resilience.Transient })
        .Build();

    private const string SearchResponse = """
        { "web": { "results": [
            { "title": "Eiffel Tower - Wikipedia", "url": "https://en.wikipedia.org/wiki/Eiffel_Tower",
              "description": "The tower was <strong>completed in March 1889</strong> for the Exposition Universelle." },
            { "title": "Eiffel Tower history", "url": "https://www.toureiffel.paris/en/the-monument/history",
              "description": "Construction took two years, two months and five days." }
        ] } }
        """;

    private sealed class FakeModel(Func<string, string, string> answer) : ILanguageModel
    {
        public List<(string System, string User)> Prompts { get; } = [];
        public Task<string> CompleteAsync(string system, string user, CancellationToken ct)
        {
            Prompts.Add((system, user));
            return Task.FromResult(answer(system, user));
        }
    }

    private static FactCheckProvider Build(ILanguageModel? model, string? braveKey = "brave-key", string searchJson = SearchResponse)
    {
        var search = new BraveSearch(new HttpClient(new FakeHttpHandler(_ => FakeHttpHandler.Json(searchJson))), () => braveKey, NoDelay);
        return new FactCheckProvider(search, model);
    }

    [Fact]
    public async Task Supported_verdict_renders_a_green_card_with_the_cited_source()
    {
        var model = new FakeModel((_, _) => """{"verdict":"supported","reason":"Completed in March 1889 for the Exposition Universelle.","source":"https://en.wikipedia.org/wiki/Eiffel_Tower"}""");

        var insight = await Build(model).QueryAsync(Statement, _ct);

        Assert.NotNull(insight);
        Assert.Equal(Intent.FactCheck, insight.Intent);
        Assert.Equal("[FACT] The Eiffel Tower was completed in 1889", insight.Headline);
        Assert.Equal("✅ Supported — Completed in March 1889 for the Exposition Universelle.", insight.Answer);
        Assert.Equal("https://en.wikipedia.org/wiki/Eiffel_Tower", insight.Link?.ToString());
        Assert.Equal(0.9, insight.Confidence);
        var (system, user) = Assert.Single(model.Prompts);
        Assert.Contains("JSON", system);
        Assert.Contains(Claim, user);
        Assert.Contains("completed in March 1889", user);      // evidence snippets, tags stripped
        Assert.DoesNotContain("<strong>", user);
    }

    [Theory]
    [InlineData("contradicted", "❌ Contradicted — Python was created by Guido van Rossum.", 0.9)]
    [InlineData("unverified", "⚠️ Unverified — No source confirms the date.", 0.6)]
    public async Task Other_verdicts_render_their_own_prefix_and_confidence(string verdict, string expected, double confidence)
    {
        var reason = expected[(expected.IndexOf("— ") + 2)..];
        var model = new FakeModel((_, _) => JsonSerializer.Serialize(new { verdict, reason, source = "https://en.wikipedia.org/wiki/Eiffel_Tower" }));

        var insight = await Build(model).QueryAsync(Statement, _ct);

        Assert.NotNull(insight);
        Assert.Equal(expected, insight.Answer);
        Assert.Equal(confidence, insight.Confidence);
    }

    [Fact]
    public async Task Without_an_llm_the_top_result_is_shown_as_unverified()
    {
        var insight = await Build(model: null).QueryAsync(Statement, _ct);

        Assert.NotNull(insight);
        Assert.StartsWith("⚠️ Unverified (no LLM configured) — ", insight.Answer);
        Assert.Contains("completed in March 1889", insight.Answer);
        Assert.Equal("https://en.wikipedia.org/wiki/Eiffel_Tower", insight.Link?.ToString());
    }

    [Fact]
    public async Task Prose_around_the_json_is_tolerated()
    {
        var model = new FakeModel((_, _) => "Sure! Here is the verdict:\n```json\n{\"verdict\":\"supported\",\"reason\":\"Yes.\",\"source\":\"https://en.wikipedia.org/wiki/Eiffel_Tower\"}\n```");

        var insight = await Build(model).QueryAsync(Statement, _ct);

        Assert.NotNull(insight);
        Assert.Equal("✅ Supported — Yes.", insight.Answer);
    }

    [Fact]
    public async Task Unusable_source_falls_back_to_the_top_search_result()
    {
        var model = new FakeModel((_, _) => """{"verdict":"supported","reason":"Yes.","source":"not a url"}""");

        var insight = await Build(model).QueryAsync(Statement, _ct);

        Assert.NotNull(insight);
        Assert.Equal("https://en.wikipedia.org/wiki/Eiffel_Tower", insight.Link?.ToString());
    }

    [Fact]
    public async Task Llm_failure_yields_an_unverified_card()
    {
        var model = new FakeModel((_, _) => throw new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable));

        var insight = await Build(model).QueryAsync(Statement, _ct);

        Assert.NotNull(insight);
        Assert.Equal("⚠️ Unverified — fact-check service unavailable (503).", insight.Answer);
        Assert.Equal("https://en.wikipedia.org/wiki/Eiffel_Tower", insight.Link?.ToString());
    }

    [Fact]
    public async Task Without_a_search_key_the_llm_is_asked_without_evidence()
    {
        var model = new FakeModel((_, user) => user.Contains("No evidence")
            ? """{"verdict":"unverified","reason":"No evidence available.","source":""}"""
            : throw new InvalidOperationException("expected the no-evidence prompt"));

        var insight = await Build(model, braveKey: null).QueryAsync(Statement, _ct);

        Assert.NotNull(insight);
        Assert.Equal("⚠️ Unverified — No evidence available.", insight.Answer);
        Assert.Null(insight.Link);
    }

    [Fact]
    public async Task Foundry_client_posts_an_openai_style_request_and_reads_the_content()
    {
        const string recorded = """
            { "id": "chatcmpl-1", "object": "chat.completion", "model": "Llama-3.3-70B-Instruct",
              "choices": [ { "index": 0, "finish_reason": "stop",
                "message": { "role": "assistant", "content": "{\"verdict\":\"supported\",\"reason\":\"ok\",\"source\":\"\"}" } } ],
              "usage": { "prompt_tokens": 120, "completion_tokens": 20, "total_tokens": 140 } }
            """;
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(recorded));
        var client = new FoundryClient(new HttpClient(handler), new Uri("https://acme.services.ai.azure.com"), "Llama-3.3-70B-Instruct", "foundry-key");

        var content = await client.CompleteAsync("You are a fact checker. Answer in JSON.", "Claim: x", _ct);

        Assert.Equal("""{"verdict":"supported","reason":"ok","source":""}""", content);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://acme.services.ai.azure.com/models/chat/completions?api-version=2024-05-01-preview", request.RequestUri?.ToString());
        Assert.Equal("foundry-key", request.Headers.GetValues("api-key").Single());
        using var body = JsonDocument.Parse(handler.Bodies.Single());
        Assert.Equal("Llama-3.3-70B-Instruct", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("system", body.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("Claim: x", body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task A_full_chat_completions_url_such_as_ollama_is_used_as_is_without_a_key_header()
    {
        const string recorded = """{ "choices": [ { "message": { "role": "assistant", "content": "{\"verdict\":\"unverified\"}" } } ] }""";
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(recorded));
        // A stored Azure key must not be sent to a loopback endpoint.
        var client = new FoundryClient(new HttpClient(handler), new Uri("http://localhost:11434/v1/chat/completions"), "llama3.2", "azure-key");

        var content = await client.CompleteAsync("s", "u", _ct);

        Assert.Equal("""{"verdict":"unverified"}""", content);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://localhost:11434/v1/chat/completions", request.RequestUri?.ToString());
        Assert.False(request.Headers.Contains("api-key"));
        using var body = JsonDocument.Parse(handler.Bodies.Single());
        Assert.Equal("llama3.2", body.RootElement.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData("https://acme.services.ai.azure.com", true)]
    [InlineData("https://acme.services.ai.azure.com/", true)]
    [InlineData("http://localhost:11434/v1/chat/completions", true)]
    [InlineData("http://acme.example.com", false)]
    [InlineData("acme", false)]
    public void Endpoint_validation_allows_https_anywhere_and_http_only_on_localhost(string endpoint, bool valid)
    {
        Assert.Equal(valid, FoundryClient.IsValidEndpoint(endpoint));
    }

    [Fact]
    public async Task Foundry_client_surfaces_http_failures_as_HttpRequestException()
    {
        var client = new FoundryClient(new HttpClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))),
            new Uri("https://acme.services.ai.azure.com"), "Llama-3.3-70B-Instruct", "bad");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync("s", "u", _ct));
    }
}

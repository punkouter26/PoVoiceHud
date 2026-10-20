using PoVoiceHud.Core.Providers.FactCheck;
using PoVoiceHud.Core.Sessions;

namespace PoVoiceHud.Core.Tests.Sessions;

public class SessionLogTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 14, 0, 0, TimeSpan.FromHours(2));
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PoVoiceHud.Tests", Guid.NewGuid().ToString("N"));
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private sealed class FakeModel(Func<string, string> answer) : ILanguageModel
    {
        public string? User { get; private set; }
        public Task<string> CompleteAsync(string system, string user, CancellationToken ct)
        {
            User = user;
            return Task.FromResult(answer(user));
        }
    }

    [Fact]
    public void The_conversation_starts_after_a_long_pause_or_the_last_recap_and_only_last_ratings_count()
    {
        var log = new SessionLog(_dir);
        log.Append(new SessionEntry { At = T0.AddHours(-2), Source = "Mic", Heard = "An earlier meeting." });
        log.Append(new SessionEntry { At = T0, Source = "Teams", Heard = "Did the build pass?" });
        log.Append(new SessionEntry { At = T0.AddMinutes(1), Card = "[FACT] x", Answer = "✅ Supported — Yes.", Verdict = "Supported" });
        log.Append(new SessionEntry { At = T0.AddMinutes(2), Card = "[FACT] x", Rating = "up" });
        log.Append(new SessionEntry { At = T0.AddMinutes(3), Card = "[FACT] x", Rating = "down" });   // changed their mind
        log.Append(new SessionEntry { At = T0.AddMinutes(3), Card = "[FACT] y", Rating = "up" });
        File.AppendAllText(Path.Combine(_dir, "2026-10-19.jsonl"), "{\"at\": \"2026-10-19T14:04:00+02:00\", \"heard\": \"cut sho\n");   // a crash mid-line

        var conversation = log.Conversation(T0.AddMinutes(10), TimeSpan.FromMinutes(20));
        Assert.Equal("Did the build pass?", conversation[0].Heard);   // the meeting two hours earlier is a different conversation
        Assert.Equal(5, conversation.Count);
        Assert.Equal((1, 1), log.Ratings(T0.AddDays(-7), T0.AddMinutes(10)));

        log.Append(new SessionEntry { At = T0.AddMinutes(11), Recap = "recap.md" });
        log.Append(new SessionEntry { At = T0.AddMinutes(12), Source = "Mic", Heard = "Next topic." });
        Assert.Equal("Next topic.", Assert.Single(log.Conversation(T0.AddMinutes(13), TimeSpan.FromMinutes(20))).Heard);
        Assert.Contains("✅ Supported", File.ReadAllText(Path.Combine(_dir, "2026-10-19.jsonl")));   // readable, not \u-escaped
    }

    [Fact]
    public async Task A_recap_has_the_model_summary_then_what_happened_and_still_works_without_a_model()
    {
        SessionEntry[] conversation =
        [
            new() { At = T0, Source = "Teams", Heard = "Let's ship on Friday." },
            new() { At = T0.AddMinutes(1), Source = "Mic", Heard = "Python was created by Dennis Ritchie." },
            new() { At = T0.AddMinutes(1), Source = "Mic", Card = "[FACT] Python was created by Dennis Ritchie", Verdict = "Contradicted",
                    Answer = "❌ Contradicted — Python was created by Guido van Rossum.", Link = new Uri("https://en.wikipedia.org/wiki/Python_(programming_language)") },
            new() { At = T0.AddMinutes(2), Card = "[FACT] Python was created by Dennis Ritchie", Rating = "up" },
            new() { At = T0.AddMinutes(3), Source = "Teams", Card = "[SCHEMA] Inspection.TenantId", Answer = "Yes, a required foreign key." },
        ];
        var model = new FakeModel(_ => "### Summary\n- Ship on Friday.\n### Decisions\n- Ship Friday.\n### Action items\n- None\n### Open questions\n- None");

        var recap = await new RecapWriter(model).WriteAsync(conversation, _ct);

        Assert.Equal("2 lines, 1 fact check, 1 repository answer", recap.Summary);
        Assert.StartsWith("# Call recap — 2026-10-19 14:00–14:03\n\n### Summary\n- Ship on Friday.", recap.Markdown);
        Assert.Contains("- ❌ “Python was created by Dennis Ritchie” — Python was created by Guido van Rossum. " +
                        "([source](https://en.wikipedia.org/wiki/Python_(programming_language))) 👍", recap.Markdown);
        Assert.Contains("- **[SCHEMA] Inspection.TenantId** — Yes, a required foreign key.", recap.Markdown);
        Assert.Contains("- 14:00 **Them:** Let's ship on Friday.", recap.Markdown);
        Assert.Contains("\"speaker\":\"Them\"", model.User);   // the transcript travels as JSON data

        var offline = await new RecapWriter(model: null).WriteAsync(conversation, _ct);
        Assert.Contains("_No summary: add a fact-check model in Settings", offline.Markdown);
        Assert.Contains("## Transcript", offline.Markdown);

        var down = await new RecapWriter(new FakeModel(_ => throw new TimeoutException("No reply within 120 s."))).WriteAsync(conversation, _ct);
        Assert.Contains("_No summary: the model could not be reached (No reply within 120 s)._", down.Markdown);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}

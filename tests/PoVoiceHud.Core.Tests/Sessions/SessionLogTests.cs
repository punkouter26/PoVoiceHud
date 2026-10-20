using PoVoiceHud.Core.Sessions;

namespace PoVoiceHud.Core.Tests.Sessions;

public class SessionLogTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 14, 0, 0, TimeSpan.FromHours(2));
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PoVoiceHud.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Lines_and_verdicts_are_appended_to_a_readable_file_per_day()
    {
        var log = new SessionLog(_dir);
        log.Append(new SessionEntry { At = T0, Source = "Mic", Heard = "Python was created by Dennis Ritchie." });
        log.Append(new SessionEntry { At = T0.AddMinutes(1), Source = "Mic", Claim = "Python was created by Dennis Ritchie",
            Answer = "❌ Incorrect — Guido van Rossum created it.", Verdict = "Contradicted" });
        log.Append(new SessionEntry { At = T0.AddDays(1), Heard = "Next day." });

        var lines = File.ReadAllLines(Path.Combine(_dir, "2026-10-19.jsonl"));
        Assert.Equal(2, lines.Length);
        Assert.Equal("""{"at":"2026-10-19T14:00:00+02:00","source":"Mic","heard":"Python was created by Dennis Ritchie."}""", lines[0]);
        Assert.Contains("❌ Incorrect", lines[1]);   // readable, not \u-escaped; empty fields are left out
        Assert.True(File.Exists(Path.Combine(_dir, "2026-10-20.jsonl")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}

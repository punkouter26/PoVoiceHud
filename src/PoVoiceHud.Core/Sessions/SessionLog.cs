using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PoVoiceHud.Core.Config;

namespace PoVoiceHud.Core.Sessions;

/// <summary>
/// One line of sessions\{date}.jsonl. A heard sentence sets Heard; a card shown sets Card and Answer; a 👍/👎 on a card sets
/// Card and Rating ("up", "down" or "none" when taken back); a recap sets Recap to the file it wrote.
/// </summary>
public sealed record SessionEntry
{
    public DateTimeOffset At { get; init; }
    /// <summary>Who said it: "Mic" or "Teams" (<see cref="Audio.SourceHint"/>).</summary>
    public string? Source { get; init; }
    public string? Heard { get; init; }
    public string? Card { get; init; }
    public string? Answer { get; init; }
    public string? Verdict { get; init; }
    public Uri? Link { get; init; }
    public string? Rating { get; init; }
    public string? Recap { get; init; }
}

/// <summary>
/// What was heard and answered, appended one JSON object per line to a file per day. It stays on this PC; the recap and the
/// rating summary read it back. Writing is best-effort: a locked or full disk must not interrupt a call.
/// </summary>
public sealed class SessionLog(string directory)
{
    public static readonly string DefaultDirectory = Path.Combine(AppPaths.DataDir, "sessions");

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // the file is read by people too: keep ✅ and quotes as written
    };

    private readonly Lock _gate = new();

    public string Directory => directory;

    public void Append(SessionEntry entry)
    {
        try
        {
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(directory);
                File.AppendAllText(FileFor(entry.At.Date), JsonSerializer.Serialize(entry, Options) + "\n");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the log is a convenience
        }
    }

    /// <summary>Entries from <paramref name="from"/> to <paramref name="to"/>, oldest first; unreadable lines are skipped.</summary>
    public IReadOnlyList<SessionEntry> Read(DateTimeOffset from, DateTimeOffset to)
    {
        var entries = new List<SessionEntry>();
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            var file = FileFor(day);
            if (!File.Exists(file)) continue;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var line in lines)
            {
                SessionEntry? entry = null;
                try
                {
                    entry = JsonSerializer.Deserialize<SessionEntry>(line, Options);
                }
                catch (JsonException)
                {
                    // a line cut short by a crash
                }
                if (entry is not null && entry.At >= from && entry.At <= to) entries.Add(entry);
            }
        }
        return entries;
    }

    /// <summary>
    /// The conversation going on at <paramref name="now"/>: going back from now, every entry until a silence longer than
    /// <paramref name="gap"/> or the last recap. Read from the files, so restarting the app mid-call does not cut it short.
    /// </summary>
    public IReadOnlyList<SessionEntry> Conversation(DateTimeOffset now, TimeSpan gap)
    {
        var entries = Read(now.AddDays(-1), now);   // a call can run past midnight
        var start = entries.Count;
        var later = now;
        for (var i = entries.Count - 1; i >= 0 && entries[i].Recap is null && later - entries[i].At <= gap; i--)
        {
            later = entries[i].At;
            start = i;
        }
        return entries.Skip(start).ToList();
    }

    /// <summary>👍 and 👎 given since <paramref name="since"/>. A card rated and then changed counts once, as last rated.</summary>
    public (int Up, int Down) Ratings(DateTimeOffset since, DateTimeOffset now)
    {
        var last = Read(since, now)
            .Where(e => e is { Rating: not null, Card: not null })
            .GroupBy(e => (e.At.Date, e.Card))
            .Select(g => g.Last().Rating)
            .ToList();
        return (last.Count(r => r == "up"), last.Count(r => r == "down"));
    }

    private string FileFor(DateTime day) => Path.Combine(directory, $"{day:yyyy-MM-dd}.jsonl");
}

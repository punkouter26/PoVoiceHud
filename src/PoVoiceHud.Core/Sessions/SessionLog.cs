using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PoVoiceHud.Core.Config;

namespace PoVoiceHud.Core.Sessions;

/// <summary>One line of sessions\{date}.jsonl: a heard sentence sets Heard; a verdict sets Claim, Answer and Verdict.</summary>
public sealed record SessionEntry
{
    public DateTimeOffset At { get; init; }
    /// <summary>Who said it: "Mic" or "Teams" (<see cref="Audio.SourceHint"/>).</summary>
    public string? Source { get; init; }
    public string? Heard { get; init; }
    public string? Claim { get; init; }
    public string? Answer { get; init; }
    public string? Verdict { get; init; }
    public Uri? Link { get; init; }
}

/// <summary>
/// What was heard and checked, appended one JSON object per line to a file per day, on this PC only: a history to look
/// back at (tray → Open the history folder). Writing is best-effort: a locked or full disk must not interrupt a call.
/// </summary>
public sealed class SessionLog(string directory)
{
    public static readonly string DefaultDirectory = Path.Combine(AppPaths.DataDir, "sessions");

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // the file is read by people: keep ✅ and quotes as written
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
                File.AppendAllText(Path.Combine(directory, $"{entry.At:yyyy-MM-dd}.jsonl"), JsonSerializer.Serialize(entry, Options) + "\n");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the log is a convenience
        }
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;

namespace PoVoiceHud.Core.Config;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } = Path.Combine(AppPaths.DataDir, "settings.json");

    public static (Settings Settings, string? Warning) Load(string path)
    {
        if (!File.Exists(path)) return (new Settings(), null);
        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<Settings>(json, Options) ?? new Settings();
            // Older versions stored the call switch as "AudioInput": "Both" (or "Remote"); carry it over rather than drop it.
            if (JsonNode.Parse(json) is JsonObject old && !old.ContainsKey(nameof(Settings.ListenToCall))
                && old["AudioInput"] is JsonValue input && input.TryGetValue<string>(out var audio) && audio is "Both" or "Remote")
                settings = settings with { ListenToCall = true };
            return (settings, null);
        }
        catch (JsonException e)
        {
            return (new Settings(), $"{Path.GetFileName(path)} is unreadable, using defaults: {e.Message}");
        }
    }

    public static void Save(Settings settings, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}

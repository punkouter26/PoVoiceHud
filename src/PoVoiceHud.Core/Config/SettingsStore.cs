using System.Text.Json;

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
            return (JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options) ?? new Settings(), null);
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

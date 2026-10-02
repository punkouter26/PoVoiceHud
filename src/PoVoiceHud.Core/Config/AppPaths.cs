namespace PoVoiceHud.Core.Config;

public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\PoVoiceHud — settings, models, window placement, crash log.</summary>
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoVoiceHud");
}

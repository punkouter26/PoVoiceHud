using PoVoiceHud.Core.Config;

namespace PoVoiceHud.Core.Tests.Config;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PoVoiceHud.Tests", Guid.NewGuid().ToString("N"));
    private string File_ => Path.Combine(_dir, "settings.json");

    [Fact]
    public void Round_trips_all_fields()
    {
        var s = new Settings { RepoPath = @"C:\src\pmis", WhisperModel = "tiny.en", WhisperThreads = 4, AzdoOrgUrl = "https://dev.azure.com/acme", AzdoProject = "PMIS", AutoExpandSeconds = 30 };

        SettingsStore.Save(s, File_);
        var (loaded, warning) = SettingsStore.Load(File_);

        Assert.Equal(s, loaded);
        Assert.Null(warning);
    }

    [Fact]
    public void Missing_file_gives_defaults_without_warning()
    {
        var (loaded, warning) = SettingsStore.Load(File_);

        Assert.Equal(new Settings(), loaded);
        Assert.Null(warning);
        Assert.Equal("base.en", loaded.WhisperModel);
    }

    [Fact]
    public void Corrupt_file_gives_defaults_with_warning()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, "{ not json");

        var (loaded, warning) = SettingsStore.Load(File_);

        Assert.Equal(new Settings(), loaded);
        Assert.Contains("settings.json", warning);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}

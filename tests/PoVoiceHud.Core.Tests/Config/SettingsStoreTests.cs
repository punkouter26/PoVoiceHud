using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Tests.Config;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PoVoiceHud.Tests", Guid.NewGuid().ToString("N"));
    private string File_ => Path.Combine(_dir, "settings.json");

    [Fact]
    public void Round_trips_all_fields()
    {
        var s = new Settings
        {
            RepoPath = @"C:\src\pmis", WhisperModel = "tiny.en", WhisperThreads = 4, AzdoOrgUrl = "https://dev.azure.com/acme", AzdoProject = "PMIS", AutoExpandSeconds = 30,
            Mode = HudMode.Repo, AudioInput = AudioInput.Both, EnabledSources = Intent.Git | Intent.Schema | Intent.FactCheck,
            FoundryEndpoint = "https://acme.services.ai.azure.com", FoundryModel = "Llama-3.3-70B-Instruct",
        };

        SettingsStore.Save(s, File_);
        var (loaded, warning) = SettingsStore.Load(File_);

        Assert.Equal(s, loaded);
        Assert.Null(warning);
        Assert.Contains("\"Git, Schema, FactCheck\"", File.ReadAllText(File_));
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
    public void Defaults_are_simple_mode_mic_only_fact_check()
    {
        var s = new Settings();

        Assert.Equal(HudMode.Simple, s.Mode);
        Assert.Equal(AudioInput.Mic, s.AudioInput);
        Assert.Equal(Intent.FactCheck, s.EnabledSources);
        Assert.Null(s.RepoPath);
    }

    [Fact]
    public void Mode_presets_set_audio_and_sources()
    {
        var repo = new Settings().ApplyMode(HudMode.Repo);
        Assert.Equal(AudioInput.Both, repo.AudioInput);
        Assert.Equal(Intent.Git | Intent.Schema | Intent.Docs | Intent.Pipeline, repo.EnabledSources);

        var simple = repo.ApplyMode(HudMode.Simple);
        Assert.Equal(AudioInput.Mic, simple.AudioInput);
        Assert.Equal(Intent.FactCheck, simple.EnabledSources);
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

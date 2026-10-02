using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Tests.Fixtures;
using PoVoiceHud.Core.ViewModels;

namespace PoVoiceHud.Core.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly Dictionary<string, string> _secrets = [];
    private Settings? _saved;
    private string? _pickedFolder;

    private SettingsViewModel Build(Settings? settings = null) => new(
        settings ?? new Settings(),
        readSecret: name => _secrets.GetValueOrDefault(name),
        writeSecret: (name, value) => _secrets[name] = value,
        pickFolder: () => Task.FromResult(_pickedFolder),
        save: s => _saved = s);

    [Fact]
    public void Loads_current_settings_and_whether_secrets_exist()
    {
        _secrets[CredentialStore.FoundryKey] = "k";
        var vm = Build(new Settings { RepoPath = @"C:\src\pmis", FoundryEndpoint = "https://acme.services.ai.azure.com" }.ApplyMode(HudMode.Repo));

        Assert.Equal(HudMode.Repo, vm.Mode);
        Assert.Equal(AudioInput.Both, vm.AudioInput);
        Assert.True(vm.GitEnabled);
        Assert.False(vm.FactCheckEnabled);
        Assert.Equal(@"C:\src\pmis", vm.RepoPath);
        Assert.True(vm.HasFoundryKey);
        Assert.False(vm.HasAzdoPat);
        Assert.Equal("", vm.FoundryKey);
    }

    [Fact]
    public void Choosing_a_mode_applies_its_preset_but_toggles_stay_editable()
    {
        var vm = Build();
        Assert.True(vm.FactCheckEnabled);
        Assert.False(vm.PipelineEnabled);

        vm.Mode = HudMode.Repo;
        Assert.Equal(AudioInput.Both, vm.AudioInput);
        Assert.True(vm.PipelineEnabled);
        Assert.False(vm.FactCheckEnabled);

        vm.FactCheckEnabled = true;
        vm.AudioInput = AudioInput.Mic;
        var settings = vm.ToSettings();
        Assert.Equal(HudMode.Repo, settings.Mode);
        Assert.Equal(AudioInput.Mic, settings.AudioInput);
        Assert.Equal(Settings.RepoSources | Intent.FactCheck, settings.EnabledSources);
    }

    [Fact]
    public async Task Browse_accepts_a_git_repository_and_rejects_other_folders()
    {
        using var repo = TempRepo.WithSampleProject();
        var vm = Build();

        _pickedFolder = repo.Root;
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(repo.Root, vm.RepoPath);
        Assert.Null(vm.RepoError);

        _pickedFolder = Path.GetTempPath();
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(repo.Root, vm.RepoPath);
        Assert.Contains("not a git repository", vm.RepoError);

        _pickedFolder = null;   // dialog cancelled
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(repo.Root, vm.RepoPath);
    }

    [Fact]
    public void Save_persists_settings_and_only_the_secrets_that_were_typed()
    {
        _secrets[CredentialStore.AzdoPat] = "old-pat";
        var vm = Build();
        vm.FoundryEndpoint = "https://acme.services.ai.azure.com";
        vm.FoundryModel = "Phi-4";
        vm.FoundryKey = "new-foundry";

        vm.SaveCommand.Execute(null);

        Assert.NotNull(_saved);
        Assert.Equal("https://acme.services.ai.azure.com", _saved.FoundryEndpoint);
        Assert.Equal("Phi-4", _saved.FoundryModel);
        Assert.Equal("new-foundry", _secrets[CredentialStore.FoundryKey]);
        Assert.Equal("old-pat", _secrets[CredentialStore.AzdoPat]);
        Assert.True(vm.HasFoundryKey);
        Assert.Equal("", vm.FoundryKey);
        Assert.True(vm.NeedsRestart);
    }

    [Fact]
    public void Save_is_blocked_by_an_invalid_foundry_endpoint()
    {
        var vm = Build();
        vm.FoundryEndpoint = "acme";

        vm.SaveCommand.Execute(null);

        Assert.Null(_saved);
        Assert.Contains("https://", vm.FoundryError);
    }

    [Fact]
    public void Use_ollama_fills_in_the_local_endpoint_and_model()
    {
        var vm = Build();

        vm.UseOllamaCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        Assert.NotNull(_saved);
        Assert.Equal("http://localhost:11434/v1/chat/completions", _saved.FoundryEndpoint);
        Assert.Equal("llama3.2", _saved.FoundryModel);
        Assert.Null(vm.FoundryError);
    }
}

using System.Net;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers.FactCheck;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Tests.Fixtures;
using PoVoiceHud.Core.ViewModels;

namespace PoVoiceHud.Core.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly Dictionary<string, string> _secrets = [];
    private readonly List<(Uri Endpoint, string Model, string? Key)> _connections = [];
    private Func<string> _modelReply = () => "OK";
    private Settings? _saved;
    private string? _keyWhenSaved;
    private string? _pickedFolder;

    private sealed class StubModel(Func<string> reply) : ILanguageModel
    {
        public Task<string> CompleteAsync(string system, string user, CancellationToken ct) => Task.FromResult(reply());
    }

    private SettingsViewModel Build(Settings? settings = null, string? ollamaModel = null) => new(
        settings ?? new Settings(),
        readSecret: name => _secrets.GetValueOrDefault(name),
        writeSecret: (name, value) => _secrets[name] = value,
        pickFolder: () => Task.FromResult(_pickedFolder),
        save: s =>
        {
            _saved = s;
            _keyWhenSaved = _secrets.GetValueOrDefault(CredentialStore.FoundryKey);
        },
        connect: (endpoint, model, key) =>
        {
            _connections.Add((endpoint, model, key));
            return new StubModel(_modelReply);
        },
        ollamaModel: () => ollamaModel);

    [Fact]
    public void Loads_current_settings_and_whether_secrets_exist()
    {
        _secrets[CredentialStore.FoundryKey] = "k";
        var vm = Build(new Settings { RepoPath = @"C:\src\pmis", FoundryEndpoint = "https://acme.services.ai.azure.com" }.ApplyMode(HudMode.Repo));

        Assert.True(vm.HasRepo);
        Assert.Equal(@"C:\src\pmis", vm.RepoPath);
        Assert.True(vm.IsModelCollapsed);   // endpoint + stored key: the model folds to one line
        Assert.Equal("Azure AI Foundry · Llama-3.3-70B-Instruct", vm.ModelSummary);
        vm.ChangeModelCommand.Execute(null);
        Assert.True(vm.IsEditingModel);
        Assert.True(Build().IsEditingModel);   // nothing configured yet: the fields are simply there
        Assert.Equal(SettingsViewModel.Foundry, vm.Provider);
        Assert.True(vm.HasFoundryKey);
        Assert.False(vm.HasAzdoPat);
        Assert.Equal("", vm.FoundryKey);
    }

    [Fact]
    public void A_repository_is_what_switches_between_simple_and_repo_mode()
    {
        var custom = new Settings { EnabledSources = Intent.FactCheck | Intent.Git };   // hand-edited settings.json
        var vm = Build(custom);
        vm.FoundryModel = "Phi-4";
        Assert.Equal(custom.EnabledSources, vm.ToSettings().EnabledSources);            // mode unchanged: saved choices are kept

        vm.RepoPath = "https://dev.azure.com/acme/PMIS/_git/pmis";
        Assert.True(vm.HasRepo);
        var repo = vm.ToSettings();
        Assert.Equal((HudMode.Repo, AudioInput.Both, Settings.RepoSources, "Phi-4"), (repo.Mode, repo.AudioInput, repo.EnabledSources, repo.FoundryModel));

        vm.Apply();
        vm.RepoPath = " ";   // clearing it goes back to mic-only fact checking
        var simple = vm.ToSettings();
        Assert.Equal((HudMode.Simple, AudioInput.Mic, Intent.FactCheck, (string?)null), (simple.Mode, simple.AudioInput, simple.EnabledSources, simple.RepoPath));
    }

    [Fact]
    public async Task Browse_accepts_a_git_repository_and_the_repository_test_reports_what_it_finds()
    {
        using var repo = TempRepo.WithSampleProject();
        var vm = Build();

        vm.IsOpen = true;
        _pickedFolder = repo.Root;
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(repo.Root, vm.RepoPath);
        Assert.Null(vm.RepoError);
        Assert.Null(_saved);   // still open: nothing is applied until the settings close

        _pickedFolder = Path.GetTempPath();
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(repo.Root, vm.RepoPath);
        Assert.Contains("not a git repository", vm.RepoError);

        _pickedFolder = null;   // dialog cancelled
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(repo.Root, vm.RepoPath);

        await vm.TestRepoCommand.ExecuteAsync(null);
        Assert.StartsWith("✓ Git repository on branch", vm.RepoTestResult);
        vm.RepoPath = Path.GetTempPath();
        await vm.TestRepoCommand.ExecuteAsync(null);
        Assert.StartsWith("✗ That folder is not a git repository", vm.RepoTestResult);

        vm.RepoPath = "https://dev.azure.com/acme/PMIS/_git/pmis";   // a URL is a valid setting; whether it answers is Test's job
        vm.Apply();
        Assert.Null(vm.RepoError);
        Assert.Equal("https://dev.azure.com/acme/PMIS/_git/pmis", _saved?.RepoPath);

        vm.IsOpen = false;        // the folder dialog took the focus and the settings closed behind it…
        _pickedFolder = repo.Root;
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(repo.Root, _saved?.RepoPath);   // …so the picked folder is applied as soon as it arrives
    }

    [Fact]
    public void Save_is_offered_only_for_changes_and_writes_typed_secrets_before_the_settings()
    {
        _secrets[CredentialStore.AzdoPat] = "old-pat";
        var vm = Build();
        Assert.False(vm.SaveCommand.CanExecute(null));   // nothing edited yet

        vm.FoundryEndpoint = "https://acme.services.ai.azure.com";
        vm.FoundryModel = "Phi-4";
        vm.FoundryKey = "new-foundry";
        Assert.True(vm.SaveCommand.CanExecute(null));

        vm.SaveCommand.Execute(null);

        Assert.NotNull(_saved);
        Assert.Equal("new-foundry", _keyWhenSaved);   // applying the settings rebuilds the pipeline, which reads the key
        Assert.Equal("https://acme.services.ai.azure.com", _saved.FoundryEndpoint);
        Assert.Equal("Phi-4", _saved.FoundryModel);
        Assert.Equal("new-foundry", _secrets[CredentialStore.FoundryKey]);
        Assert.Equal("old-pat", _secrets[CredentialStore.AzdoPat]);
        Assert.True(vm.HasFoundryKey);
        Assert.Equal("", vm.FoundryKey);
        Assert.False(vm.SaveCommand.CanExecute(null));   // saved state is the new baseline
        Assert.True(vm.IsModelCollapsed);                // and a working model folds back to one line

        _saved = null;
        vm.Apply();                                      // closing the settings again with nothing changed does nothing
        Assert.Null(_saved);
        vm.FoundryModel = "Phi-4-mini";
        vm.Apply();                                      // closing them after an edit is what saves it
        Assert.Equal("Phi-4-mini", _saved?.FoundryModel);
        Assert.Null(vm.Problem);
    }

    [Fact]
    public void Save_is_blocked_by_an_invalid_endpoint_or_repository()
    {
        var vm = Build();
        vm.FoundryEndpoint = "acme";
        vm.RepoPath = "http://dev.azure.com/acme/PMIS/_git/pmis";   // the access token must not travel over plain http

        vm.SaveCommand.Execute(null);

        Assert.Null(_saved);
        Assert.Contains("https://", vm.FoundryError);
        Assert.Contains("https://dev.azure.com", vm.RepoError);
        Assert.Equal(vm.FoundryError, vm.Problem);   // what the pill reports, since the settings are closed by then
    }

    [Fact]
    public void Choosing_ollama_fills_in_the_local_endpoint_and_a_model_it_has()
    {
        var vm = Build(new Settings { FoundryEndpoint = "https://acme.services.ai.azure.com", FoundryModel = "Phi-4" }, ollamaModel: "gemma3:4b");

        vm.Provider = SettingsViewModel.Ollama;
        Assert.False(vm.IsFoundry);
        vm.SaveCommand.Execute(null);

        Assert.NotNull(_saved);
        Assert.Equal("http://localhost:11434/v1/chat/completions", _saved.FoundryEndpoint);
        Assert.Equal("gemma3:4b", _saved.FoundryModel);
        Assert.Null(vm.FoundryError);

        vm.Provider = SettingsViewModel.Foundry;   // nothing was saved for Foundry any more: back to an empty endpoint and the default model
        Assert.Null(vm.FoundryEndpoint);
        Assert.Equal(new Settings().FoundryModel, vm.FoundryModel);
    }

    [Fact]
    public async Task Test_calls_the_model_with_what_is_typed_and_reports_the_outcome()
    {
        _secrets[CredentialStore.FoundryKey] = "stored-key";
        var vm = Build();

        await vm.TestCommand.ExecuteAsync(null);
        Assert.StartsWith("✗", vm.TestResult);   // no endpoint yet
        Assert.Empty(_connections);

        vm.FoundryEndpoint = "https://acme.services.ai.azure.com";
        vm.FoundryModel = "Phi-4";
        await vm.TestCommand.ExecuteAsync(null);
        Assert.Equal("✓ Phi-4 answered.", vm.TestResult);
        Assert.Equal((new Uri("https://acme.services.ai.azure.com"), "Phi-4", "stored-key"), Assert.Single(_connections));

        _modelReply = () => throw new HttpRequestException("401", null, HttpStatusCode.Unauthorized);
        vm.FoundryKey = "typed-key";
        await vm.TestCommand.ExecuteAsync(null);
        Assert.Contains("HTTP 401", vm.TestResult);
        Assert.Equal("typed-key", _connections[^1].Key);   // the typed key is what gets tested, not the stored one
    }
}

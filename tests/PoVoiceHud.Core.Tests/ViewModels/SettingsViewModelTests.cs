using System.Net;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers.FactCheck;
using PoVoiceHud.Core.ViewModels;

namespace PoVoiceHud.Core.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly Dictionary<string, string> _secrets = [];
    private readonly List<(Uri Endpoint, string? Key)> _connections = [];
    private Func<string> _modelReply = () => "OK";
    private Settings? _saved;
    private string? _keyWhenSaved;

    private sealed class StubModel(Func<string> reply) : ILanguageModel
    {
        public Task<string> CompleteAsync(string system, string user, CancellationToken ct) => Task.FromResult(reply());
    }

    private SettingsViewModel Build(Settings? settings = null) => new(
        settings ?? new Settings(),
        readSecret: name => _secrets.GetValueOrDefault(name),
        writeSecret: (name, value) => _secrets[name] = value,
        save: s =>
        {
            _saved = s;
            _keyWhenSaved = _secrets.GetValueOrDefault(CredentialStore.FoundryKey);
        },
        connect: (endpoint, key) =>
        {
            _connections.Add((endpoint, key));
            return new StubModel(_modelReply);
        });

    [Fact]
    public void Loads_the_settings_and_whether_a_key_is_stored()
    {
        _secrets[CredentialStore.FoundryKey] = "k";
        var vm = Build(new Settings { FoundryEndpoint = "https://acme.services.ai.azure.com", ListenToCall = true, Sounds = false });

        Assert.Equal("https://acme.services.ai.azure.com", vm.Endpoint);
        Assert.True(vm.HasKey);
        Assert.Equal("", vm.Key);
        Assert.True(vm.ListenToCall);
        Assert.False(vm.Sounds);
        Assert.True(vm.LookUpClaims);
        Assert.Contains("Wikipedia's search", vm.LookUpNote);

        _secrets.Clear();
        Assert.False(Build().HasKey);
    }

    [Fact]
    public void Closing_the_settings_saves_only_changes_and_writes_a_typed_key_before_the_settings()
    {
        var vm = Build();
        Assert.False(vm.SaveCommand.CanExecute(null));   // nothing edited yet

        vm.Endpoint = "https://acme.services.ai.azure.com";
        vm.Key = "new-key";
        vm.ListenToCall = true;
        vm.Apply();

        Assert.NotNull(_saved);
        Assert.Equal("new-key", _keyWhenSaved);   // applying the settings rebuilds the pipeline, which reads the key
        Assert.Equal("https://acme.services.ai.azure.com", _saved.FoundryEndpoint);
        Assert.Equal(AudioInput.Both, _saved.AudioInput);
        Assert.True(vm.HasKey);
        Assert.Equal("", vm.Key);
        Assert.False(vm.SaveCommand.CanExecute(null));   // the saved state is the new baseline

        _saved = null;
        vm.Apply();                                      // closing them again with nothing changed does nothing
        Assert.Null(_saved);
        vm.Sounds = false;
        vm.Apply();
        Assert.False(_saved?.Sounds);
    }

    [Fact]
    public void An_endpoint_that_is_not_https_is_refused_and_reported()
    {
        var vm = Build();
        vm.Endpoint = "http://acme.services.ai.azure.com";

        vm.Apply();

        Assert.Null(_saved);
        Assert.Contains("https://", vm.EndpointError);
        Assert.Equal(vm.EndpointError, vm.Problem);   // what the pill reports, since the settings are closed by then
    }

    [Fact]
    public async Task Check_calls_the_model_with_what_is_typed_and_says_what_went_wrong()
    {
        _secrets[CredentialStore.FoundryKey] = "stored-key";
        var vm = Build();

        await vm.CheckCommand.ExecuteAsync(null);
        Assert.StartsWith("✗ Enter the endpoint", vm.CheckResult);
        Assert.Empty(_connections);

        vm.Endpoint = "https://acme.services.ai.azure.com";
        await vm.CheckCommand.ExecuteAsync(null);
        Assert.Equal("✓ Connected — gpt-4.1-mini answered.", vm.CheckResult);
        Assert.Equal((new Uri("https://acme.services.ai.azure.com"), "stored-key"), Assert.Single(_connections));

        vm.Key = "typed-key";
        _modelReply = () => throw new HttpRequestException("401", null, HttpStatusCode.Unauthorized);
        await vm.CheckCommand.ExecuteAsync(null);
        Assert.Equal("✗ The key was refused (HTTP 401).", vm.CheckResult);
        Assert.Equal("typed-key", _connections[^1].Key);   // the typed key is what gets checked, not the stored one

        _modelReply = () => throw new HttpRequestException("404", null, HttpStatusCode.NotFound);
        await vm.CheckCommand.ExecuteAsync(null);
        Assert.Contains("No gpt-4.1-mini deployment", vm.CheckResult);
    }
}

using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibGit2Sharp;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers.FactCheck;
using PoVoiceHud.Core.Workspace;

namespace PoVoiceHud.Core.ViewModels;

/// <summary>
/// Edits <see cref="Settings"/> plus the two secrets. There are two things to set: the fact-check model, which
/// folds to one line once it is configured, and an optional repository. A repository switches the app to Repo
/// mode (call audio and repo answers on top of fact checking); without one it is Simple mode.
/// Secrets are written only when typed (blank keeps the stored one). There is no Save button: the window calls
/// <see cref="Apply"/> when the settings close, and applying takes effect at once — no restart.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const string Ollama = "Local Ollama (private, no key)";
    public const string Foundry = "Azure AI Foundry";

    // Properties that report on an action or on the view rather than hold a setting.
    private static readonly string[] NotSettings =
        [nameof(TestResult), nameof(RepoTestResult), nameof(FoundryError), nameof(RepoError), nameof(IsEditingModel), nameof(IsModelCollapsed)];

    private readonly Func<string, string?> _readSecret;
    private readonly Action<string, string> _writeSecret;
    private readonly Func<Task<string?>> _pickFolder;
    private readonly Action<Settings> _save;
    private readonly Func<Uri, string, string?, ILanguageModel> _connect;
    private readonly Func<string?> _ollamaModel;
    private Settings _original;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsFoundry), nameof(ModelSummary))] private string _provider;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasRepo))] private string? _repoPath;
    [ObservableProperty] private string? _repoError;
    [ObservableProperty] private string? _repoTestResult;
    [ObservableProperty] private string _azdoPat = "";
    [ObservableProperty] private bool _hasAzdoPat;
    [ObservableProperty] private string? _foundryEndpoint;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ModelSummary))] private string _foundryModel;
    [ObservableProperty] private string _foundryKey = "";
    [ObservableProperty] private bool _hasFoundryKey;
    [ObservableProperty] private string? _foundryError;
    [ObservableProperty] private string? _testResult;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsModelCollapsed))] private bool _isEditingModel;

    /// <param name="connect">Builds the model client for "Test" from endpoint, model and key.</param>
    /// <param name="ollamaModel">A model the local Ollama has pulled (null if it is not running), offered when switching to Ollama.</param>
    public SettingsViewModel(Settings settings, Func<string, string?> readSecret, Action<string, string> writeSecret,
        Func<Task<string?>> pickFolder, Action<Settings> save, Func<Uri, string, string?, ILanguageModel> connect, Func<string?>? ollamaModel = null)
    {
        (_original, _readSecret, _writeSecret, _pickFolder, _save, _connect) = (settings, readSecret, writeSecret, pickFolder, save, connect);
        _ollamaModel = ollamaModel ?? (() => null);
        _provider = FoundryClient.IsLocal(settings.FoundryEndpoint) ? Ollama : Foundry;
        _repoPath = settings.RepoPath;
        _foundryEndpoint = settings.FoundryEndpoint;
        _foundryModel = settings.FoundryModel;
        _hasAzdoPat = readSecret(CredentialStore.AzdoPat) is not null;
        _hasFoundryKey = readSecret(CredentialStore.FoundryKey) is not null;
        _isEditingModel = !ModelIsSetUp;
    }

    public IReadOnlyList<string> Providers { get; } = [Ollama, Foundry];

    public bool IsFoundry => Provider == Foundry;
    public bool HasRepo => Blank(RepoPath) is not null;

    /// <summary>The model fields are folded into <see cref="ModelSummary"/> until "Change…" is pressed.</summary>
    public bool IsModelCollapsed => !IsEditingModel;
    public string ModelSummary => $"{(IsFoundry ? Foundry : "Local Ollama")} · {FoundryModel}";

    /// <summary>True while the settings are on screen; set by the window. A folder picked after they closed is applied straight away.</summary>
    public bool IsOpen { get; set; }

    /// <summary>What stopped the last <see cref="Apply"/>, for showing once the settings are no longer on screen.</summary>
    public string? Problem => FoundryError ?? RepoError;

    private bool ModelIsSetUp => FoundryClient.IsReady(FoundryEndpoint, HasFoundryKey);

    /// <summary>A repository means Repo mode; a change of mode takes that mode's audio input and sources.</summary>
    public Settings ToSettings()
    {
        var mode = HasRepo ? HudMode.Repo : HudMode.Simple;
        return (mode == _original.Mode ? _original : _original.ApplyMode(mode)) with
        {
            RepoPath = Blank(RepoPath),
            FoundryEndpoint = Blank(FoundryEndpoint),
            FoundryModel = FoundryModel,
        };
    }

    /// <summary>Saves and applies whatever changed; does nothing when nothing did. Check <see cref="Problem"/> afterwards.</summary>
    public void Apply()
    {
        if (SaveCommand.CanExecute(null)) SaveCommand.Execute(null);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!NotSettings.Contains(e.PropertyName)) SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Ollama needs only a model name; Foundry gets back whatever endpoint and model were saved for it.</summary>
    partial void OnProviderChanged(string value)
    {
        FoundryError = TestResult = null;
        if (value == Ollama)
        {
            FoundryEndpoint = FoundryClient.OllamaEndpoint.ToString();
            FoundryModel = FoundryClient.IsLocal(_original.FoundryEndpoint) ? _original.FoundryModel : _ollamaModel() ?? FoundryClient.OllamaDefaultModel;
        }
        else
        {
            var saved = !FoundryClient.IsLocal(_original.FoundryEndpoint);
            FoundryEndpoint = saved ? _original.FoundryEndpoint : null;
            FoundryModel = saved ? _original.FoundryModel : new Settings().FoundryModel;
        }
    }

    [RelayCommand]
    private void ChangeModel() => IsEditingModel = true;

    [RelayCommand]
    private async Task Browse()
    {
        var folder = await _pickFolder();
        if (folder is null) return;
        if (!Repository.IsValid(folder))
        {
            RepoError = $"'{folder}' is not a git repository.";
            return;
        }
        RepoPath = folder;
        RepoError = null;
        if (!IsOpen) Apply();   // the folder dialog took the focus and the settings closed behind it
    }

    /// <summary>Checks the repository with what is typed right now: a folder is opened, a URL is asked for its branches using the token.</summary>
    [RelayCommand]
    private async Task TestRepo()
    {
        RepoTestResult = "Testing…";
        var (location, pat) = (Blank(RepoPath), Blank(AzdoPat) ?? _readSecret(CredentialStore.AzdoPat));
        RepoTestResult = await Task.Run(() => RepoSource.Probe(location, pat));
    }

    /// <summary>One real round trip to the model with what is typed right now, so a bad key or model name shows up here.</summary>
    [RelayCommand]
    private async Task Test()
    {
        if (!FoundryClient.IsValidEndpoint(Blank(FoundryEndpoint)))
        {
            TestResult = "✗ Enter the endpoint first.";
            return;
        }
        TestResult = "Testing…";
        var key = Blank(FoundryKey) ?? _readSecret(CredentialStore.FoundryKey);
        try
        {
            await _connect(new Uri(FoundryEndpoint!.Trim()), FoundryModel, key).CompleteAsync("Reply with the single word OK.", "ping", CancellationToken.None);
            TestResult = $"✓ {FoundryModel} answered.";
        }
        catch (HttpRequestException e)
        {
            TestResult = e.StatusCode is { } code
                ? $"✗ The service answered HTTP {(int)code} — check the key and the model name."
                : $"✗ Could not reach the endpoint: {e.Message}";
        }
        catch (TimeoutException e)
        {
            TestResult = $"✗ {e.Message}";
        }
    }

    private bool CanSave() => ToSettings() != _original || Blank(FoundryKey) is not null || Blank(AzdoPat) is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        FoundryError = RepoError = null;
        if (Blank(FoundryEndpoint) is { } endpoint && !FoundryClient.IsValidEndpoint(endpoint))
            FoundryError = "Endpoint must be an https:// URL (e.g. https://<resource>.services.ai.azure.com) or http://localhost… for Ollama";
        if (Blank(RepoPath) is { } repo && !RepoSource.IsValid(repo))
            RepoError = "Repository must be a folder that holds a git repository, or an https:// URL such as https://dev.azure.com/org/project/_git/repo";
        if (Problem is not null)
        {
            IsEditingModel |= FoundryError is not null;   // next time the settings open, the field at fault is on show
            return;
        }

        // Secrets first: applying the settings rebuilds the pipeline, which reads them.
        if (Store(CredentialStore.AzdoPat, AzdoPat)) { HasAzdoPat = true; AzdoPat = ""; }
        if (Store(CredentialStore.FoundryKey, FoundryKey)) { HasFoundryKey = true; FoundryKey = ""; }
        var settings = ToSettings();
        _save(settings);
        _original = settings;
        SaveCommand.NotifyCanExecuteChanged();
        IsEditingModel = !ModelIsSetUp;
    }

    private bool Store(string name, string value)
    {
        if (Blank(value) is not { } secret) return false;
        _writeSecret(name, secret);
        return true;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

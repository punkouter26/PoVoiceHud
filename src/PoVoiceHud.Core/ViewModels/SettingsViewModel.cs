using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibGit2Sharp;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers.FactCheck;
using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.ViewModels;

/// <summary>
/// Edits <see cref="Settings"/> plus the two secrets. Picking a mode applies its preset; toggles stay editable.
/// Secrets are written only when typed (blank keeps the stored one). Saving needs a restart to rebuild the pipeline.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly Settings _original;
    private readonly Action<string, string> _writeSecret;
    private readonly Func<Task<string?>> _pickFolder;
    private readonly Action<Settings> _save;

    [ObservableProperty] private HudMode _mode;
    [ObservableProperty] private AudioInput _audioInput;
    [ObservableProperty] private bool _gitEnabled;
    [ObservableProperty] private bool _schemaEnabled;
    [ObservableProperty] private bool _docsEnabled;
    [ObservableProperty] private bool _pipelineEnabled;
    [ObservableProperty] private bool _factCheckEnabled;
    [ObservableProperty] private string? _repoPath;
    [ObservableProperty] private string? _repoError;
    [ObservableProperty] private string? _azdoOrgUrl;
    [ObservableProperty] private string? _azdoProject;
    [ObservableProperty] private string _azdoPat = "";
    [ObservableProperty] private bool _hasAzdoPat;
    [ObservableProperty] private string? _foundryEndpoint;
    [ObservableProperty] private string _foundryModel;
    [ObservableProperty] private string _foundryKey = "";
    [ObservableProperty] private bool _hasFoundryKey;
    [ObservableProperty] private string? _foundryError;
    [ObservableProperty] private bool _needsRestart;

    public SettingsViewModel(Settings settings, Func<string, string?> readSecret, Action<string, string> writeSecret,
        Func<Task<string?>> pickFolder, Action<Settings> save)
    {
        (_original, _writeSecret, _pickFolder, _save) = (settings, writeSecret, pickFolder, save);
        _mode = settings.Mode;
        _audioInput = settings.AudioInput;
        SetSources(settings.EnabledSources);
        _repoPath = settings.RepoPath;
        _azdoOrgUrl = settings.AzdoOrgUrl;
        _azdoProject = settings.AzdoProject;
        _foundryEndpoint = settings.FoundryEndpoint;
        _foundryModel = settings.FoundryModel;
        _hasAzdoPat = readSecret(CredentialStore.AzdoPat) is not null;
        _hasFoundryKey = readSecret(CredentialStore.FoundryKey) is not null;
    }

    public IReadOnlyList<HudMode> Modes { get; } = [HudMode.Simple, HudMode.Repo];
    public IReadOnlyList<AudioInput> AudioInputs { get; } = [AudioInput.Mic, AudioInput.Remote, AudioInput.Both];

    public Settings ToSettings() => _original with
    {
        Mode = Mode,
        AudioInput = AudioInput,
        EnabledSources = Sources,
        RepoPath = Blank(RepoPath),
        AzdoOrgUrl = Blank(AzdoOrgUrl),
        AzdoProject = Blank(AzdoProject),
        FoundryEndpoint = Blank(FoundryEndpoint),
        FoundryModel = FoundryModel,
    };

    partial void OnModeChanged(HudMode value)
    {
        var preset = new Settings().ApplyMode(value);
        AudioInput = preset.AudioInput;
        SetSources(preset.EnabledSources);
    }

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
    }

    /// <summary>Local Llama through Ollama: no key, nothing leaves the machine.</summary>
    [RelayCommand]
    private void UseOllama()
    {
        FoundryEndpoint = FoundryClient.OllamaEndpoint.ToString();
        FoundryModel = FoundryClient.OllamaDefaultModel;
        FoundryError = null;
    }

    [RelayCommand]
    private void Save()
    {
        FoundryError = null;
        if (Blank(FoundryEndpoint) is { } endpoint && !FoundryClient.IsValidEndpoint(endpoint))
        {
            FoundryError = "Endpoint must be an https:// URL (e.g. https://<resource>.services.ai.azure.com) or http://localhost… for Ollama";
            return;
        }

        _save(ToSettings());
        if (Store(CredentialStore.AzdoPat, AzdoPat)) { HasAzdoPat = true; AzdoPat = ""; }
        if (Store(CredentialStore.FoundryKey, FoundryKey)) { HasFoundryKey = true; FoundryKey = ""; }
        NeedsRestart = true;
    }

    private bool Store(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        _writeSecret(name, value.Trim());
        return true;
    }

    private Intent Sources =>
        (GitEnabled ? Intent.Git : 0) | (SchemaEnabled ? Intent.Schema : 0) | (DocsEnabled ? Intent.Docs : 0) |
        (PipelineEnabled ? Intent.Pipeline : 0) | (FactCheckEnabled ? Intent.FactCheck : 0);

    private void SetSources(Intent sources)
    {
        GitEnabled = sources.HasFlag(Intent.Git);
        SchemaEnabled = sources.HasFlag(Intent.Schema);
        DocsEnabled = sources.HasFlag(Intent.Docs);
        PipelineEnabled = sources.HasFlag(Intent.Pipeline);
        FactCheckEnabled = sources.HasFlag(Intent.FactCheck);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

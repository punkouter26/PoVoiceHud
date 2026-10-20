using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers.FactCheck;

namespace PoVoiceHud.Core.ViewModels;

/// <summary>
/// Edits <see cref="Settings"/> plus the Foundry key: the endpoint and key, then four switches. The key is written only
/// when typed (blank keeps the stored one). There is no Save button: the window calls <see cref="Apply"/> when the
/// settings close, and applying takes effect at once — no restart.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    // Properties that report on an action rather than hold a setting.
    private static readonly string[] NotSettings = [nameof(CheckResult), nameof(EndpointError)];

    private readonly Func<string, string?> _readSecret;
    private readonly Action<string, string> _writeSecret;
    private readonly Action<Settings> _save;
    private readonly Func<Uri, string?, ILanguageModel> _connect;
    private Settings _original;

    [ObservableProperty] private string? _endpoint;
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private bool _hasKey;
    [ObservableProperty] private string? _endpointError;
    [ObservableProperty] private string? _checkResult;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LookUpNote))] private bool _lookUpClaims;
    [ObservableProperty] private bool _listenToCall;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _sounds;

    /// <param name="connect">Builds the model client for "Check" from the endpoint and key.</param>
    public SettingsViewModel(Settings settings, Func<string, string?> readSecret, Action<string, string> writeSecret,
        Action<Settings> save, Func<Uri, string?, ILanguageModel> connect)
    {
        (_original, _readSecret, _writeSecret, _save, _connect) = (settings, readSecret, writeSecret, save, connect);
        _endpoint = settings.FoundryEndpoint;
        _lookUpClaims = settings.LookUpClaims;
        _listenToCall = settings.ListenToCall;
        _startWithWindows = settings.StartWithWindows;
        _sounds = settings.Sounds;
        _hasKey = readSecret(CredentialStore.FoundryKey) is not null;
    }

    /// <summary>What the Wikipedia switch means for privacy.</summary>
    public string LookUpNote => LookUpClaims
        ? "The key words of each claim go to Wikipedia's search, and the verdict can link the article it used."
        : "Claims are judged from the model's own knowledge; nothing goes to Wikipedia.";

    /// <summary>What stopped the last <see cref="Apply"/>, for showing once the settings are no longer on screen.</summary>
    public string? Problem => EndpointError;

    public Settings ToSettings() => _original with
    {
        FoundryEndpoint = Blank(Endpoint),
        LookUpClaims = LookUpClaims,
        ListenToCall = ListenToCall,
        StartWithWindows = StartWithWindows,
        Sounds = Sounds,
    };

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

    /// <summary>One real round trip to the model with what is typed right now, so a bad endpoint or key shows up here.</summary>
    [RelayCommand]
    private async Task Check()
    {
        if (!FoundryClient.IsValidEndpoint(Blank(Endpoint)))
        {
            CheckResult = "✗ Enter the endpoint first: https://<resource>.services.ai.azure.com";
            return;
        }
        CheckResult = "Checking…";
        var key = Blank(Key) ?? _readSecret(CredentialStore.FoundryKey);
        try
        {
            await _connect(new Uri(Endpoint!.Trim()), key).CompleteAsync("Reply with the single word OK.", "ping", CancellationToken.None);
            CheckResult = $"✓ Connected — {FoundryClient.Model} answered.";
        }
        catch (HttpRequestException e)
        {
            CheckResult = e.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "✗ The key was refused (HTTP 401).",
                System.Net.HttpStatusCode.NotFound => $"✗ No {FoundryClient.Model} deployment at that endpoint (HTTP 404).",
                { } code => $"✗ The service answered HTTP {(int)code}.",
                null => $"✗ Could not reach the endpoint: {e.Message}",
            };
        }
        catch (TimeoutException e)
        {
            CheckResult = $"✗ {e.Message}";
        }
    }

    private bool CanSave() => ToSettings() != _original || Blank(Key) is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        EndpointError = Blank(Endpoint) is { } endpoint && !FoundryClient.IsValidEndpoint(endpoint)
            ? "The endpoint must be an https:// URL, such as https://<resource>.services.ai.azure.com"
            : null;
        if (Problem is not null) return;

        // The key first: applying the settings rebuilds the pipeline, which reads it.
        if (Blank(Key) is { } key)
        {
            _writeSecret(CredentialStore.FoundryKey, key);
            HasKey = true;
            Key = "";
        }
        var settings = ToSettings();
        _save(settings);
        _original = settings;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

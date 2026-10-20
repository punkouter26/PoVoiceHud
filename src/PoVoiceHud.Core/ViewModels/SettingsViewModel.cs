using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers.FactCheck;

namespace PoVoiceHud.Core.ViewModels;

/// <summary>
/// Edits <see cref="Settings"/> plus the Foundry key: the endpoint and key, then four switches. Nothing changes until
/// <see cref="ApplyCommand"/>, which first makes one real call to the model whenever the endpoint or key changed (or setup
/// is needed), so a wrong one is caught here. <see cref="CancelCommand"/> puts back what was applied. The key is written
/// only when typed (blank keeps the stored one).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    // Properties that report on the page rather than hold a setting.
    private static readonly string[] NotSettings = [nameof(Message), nameof(RequiresSetup)];

    private readonly Func<string, string?> _readSecret;
    private readonly Action<string, string> _writeSecret;
    private readonly Action<Settings> _save;
    private readonly Func<Uri, string?, ILanguageModel> _connect;
    private Settings _original;

    [ObservableProperty] private string? _endpoint;
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private bool _hasKey;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LookUpNote))] private bool _lookUpClaims;
    [ObservableProperty] private bool _listenToCall;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _sounds;
    /// <summary>What the last Apply found: "✓ Connected…", "✗ The key was refused…", or "Checking the connection…".</summary>
    [ObservableProperty] private string? _message;
    /// <summary>No working endpoint and key yet: Apply always checks the connection, and there is nothing to cancel back to.</summary>
    [ObservableProperty] private bool _requiresSetup;

    /// <param name="connect">Builds the model client for the connection check from the endpoint and key.</param>
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

    /// <summary>Applied or cancelled: the window leaves the settings.</summary>
    public event Action? Done;

    /// <summary>What the Wikipedia switch means for privacy.</summary>
    public string LookUpNote => LookUpClaims
        ? "The key words of each claim go to Wikipedia's search, and the verdict can link the article it used."
        : "Claims are judged from the model's own knowledge; nothing goes to Wikipedia.";

    public Settings ToSettings() => _original with
    {
        FoundryEndpoint = Blank(Endpoint),
        LookUpClaims = LookUpClaims,
        ListenToCall = ListenToCall,
        StartWithWindows = StartWithWindows,
        Sounds = Sounds,
    };

    /// <summary>The endpoint or key cannot be used (or was never set): the app stays in Settings until Apply succeeds.</summary>
    public void RequireSetup(string? why)
    {
        RequiresSetup = true;
        Message = why is null ? null : $"✗ {why}";
    }

    /// <summary>Puts back what was last applied: leaving the settings without Apply discards the edits.</summary>
    public void Revert()
    {
        Endpoint = _original.FoundryEndpoint;
        Key = "";
        LookUpClaims = _original.LookUpClaims;
        ListenToCall = _original.ListenToCall;
        StartWithWindows = _original.StartWithWindows;
        Sounds = _original.Sounds;
        if (!RequiresSetup) Message = null;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!NotSettings.Contains(e.PropertyName)) ApplyCommand.NotifyCanExecuteChanged();
        if (e.PropertyName == nameof(RequiresSetup))
        {
            ApplyCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanApply() => RequiresSetup || ToSettings() != _original || Blank(Key) is not null;

    /// <summary>Checks a new endpoint or key with one real call, then stores the key and saves and applies the settings.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task Apply()
    {
        var endpoint = Blank(Endpoint);
        if (!FoundryClient.IsValidEndpoint(endpoint))
        {
            Message = "✗ The endpoint must be an https:// URL, such as https://<resource>.services.ai.azure.com";
            return;
        }
        var typed = Blank(Key);
        if ((typed ?? _readSecret(CredentialStore.FoundryKey)) is not { } key)
        {
            Message = "✗ Enter the key.";
            return;
        }

        var connectionChanged = RequiresSetup || typed is not null || endpoint != _original.FoundryEndpoint;
        if (connectionChanged)
        {
            Message = "Checking the connection…";
            if (await Connection.TestAsync(_connect(new Uri(endpoint!), key), CancellationToken.None) is { } problem)
            {
                Message = $"✗ {problem.Message}";
                return;
            }
        }

        // The key first: applying the settings builds a fact checker, which reads it.
        if (typed is not null)
        {
            _writeSecret(CredentialStore.FoundryKey, typed);
            HasKey = true;
            Key = "";
        }
        var settings = ToSettings();
        _original = settings;
        RequiresSetup = false;
        Message = connectionChanged ? $"✓ Connected — {FoundryClient.Model} answered." : null;
        ApplyCommand.NotifyCanExecuteChanged();
        _save(settings);
        Done?.Invoke();
    }

    private bool CanCancel() => !RequiresSetup;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        Revert();
        Done?.Invoke();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

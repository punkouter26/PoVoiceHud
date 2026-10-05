using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using PoVoiceHud.App.Interop;
using PoVoiceHud.Core;
using PoVoiceHud.Core.Asr;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Providers.FactCheck;
using PoVoiceHud.Core.ViewModels;
using PoVoiceHud.Core.Workspace;
using WinUIEx;

namespace PoVoiceHud.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The application object lives as long as the process; its hotkeys, docking and container are released in OnClosed.")]
public partial class App : Application
{
    private const string RecheckHotkey = "Ctrl+Alt+R";

    private static readonly string WindowFile = Path.Combine(AppPaths.DataDir, "window.json");
    private static readonly string SessionsDir = Path.Combine(AppPaths.DataDir, "sessions");
    private static readonly HttpClient Http = new();

    // WinUIEx persists window placement through this dictionary (string values only);
    // unpackaged apps have no ApplicationData, so we back it with a JSON file.
    private readonly Dictionary<string, object> _placement;
    private MainWindow? _window;
    private ServiceProvider? _container;
    private HudPipeline? _pipeline;
    private int _run;   // bumped by every (re)start, so a start that was overtaken throws its pipeline away
    private string? _ollamaModel;
    private GlobalHotkey? _muteKey;
    private GlobalHotkey? _recheckKey;
    private WindowDocking? _docking;
    private DispatcherTimer? _meter;

    public App()
    {
        InitializeComponent();
        Directory.CreateDirectory(AppPaths.DataDir);
        UnhandledException += (_, e) =>
        {
            File.AppendAllText(Path.Combine(AppPaths.DataDir, "crash.log"), $"{DateTimeOffset.Now:O} {e.Exception}\n\n");
            // Only failures known to leave the app usable are survived (a file, the network, a Windows API call).
            // Anything else is a bug: it is logged above and allowed to end the process rather than run on in an unknown state.
            e.Handled = e.Exception is IOException or UnauthorizedAccessException or HttpRequestException or TimeoutException
                or OperationCanceledException or Win32Exception or COMException;
            if (e.Handled) _window?.ViewModel.SetStatus($"Error: {e.Exception.Message} (details in crash.log)");
        };
        Dictionary<string, string>? saved = null;
        try
        {
            if (File.Exists(WindowFile)) saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(WindowFile));
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // half-written placement file: start with default placement rather than crash on every launch
        }
        _placement = saved?.ToDictionary(p => p.Key, p => (object)p.Value) ?? [];
        WindowManager.PersistenceStorage = _placement;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var firstRun = !File.Exists(SettingsStore.DefaultPath);
        var (settings, warning) = SettingsStore.Load(SettingsStore.DefaultPath);
        var viewModel = new HudViewModel(TimeSpan.FromSeconds(settings.AutoExpandSeconds), settings.RepoPath, OpenAsync);
        viewModel.Apply(settings);
        _window = new MainWindow(viewModel);
        _window.Closed += OnClosed;
        _window.Activate();
        _docking = new WindowDocking(_window);
        BindHotkeys();
        AttachSettings(settings);

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HudViewModel.IsMuted) && _pipeline is { } pipeline) pipeline.IsMuted = viewModel.IsMuted;
        };
        _meter = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _meter.Tick += (_, _) =>
        {
            if (_pipeline is { } pipeline) viewModel.MicLevel = pipeline.Level;
        };
        _meter.Start();

#if DEBUG
        // Layout checks without a microphone: --demo seeds sample cards (add --settings to show the settings instead), --pin starts expanded.
        var flags = Environment.GetCommandLineArgs();
        if (flags.Contains("--pin")) viewModel.ToggleExpandCommand.Execute(null);
        if (flags.Contains("--demo"))
        {
            _window.SeedDemo();
            if (flags.Contains("--settings")) _window.ShowSettings(everything: flags.Contains("--all"));
            return;
        }
#endif
        if (warning is not null) viewModel.SetStatus(warning);
        _ = StartAsync(settings, firstRun);
    }

    private async Task StartAsync(Settings settings, bool firstRun)
    {
        var run = ++_run;
        var window = _window!;
        var viewModel = window.ViewModel;
        var note = "";   // a standing problem (repository out of date or unavailable) that every status keeps showing
        void Status(string text) => window.DispatcherQueue.TryEnqueue(() => viewModel.SetStatus(text + note));
        try
        {
            Status("Checking models…");
            var models = await ModelStore.EnsureAsync(new Progress<double>(p => Status($"Downloading models… {p:P0}")), CancellationToken.None);

            _ollamaModel = await FoundryClient.OllamaModelAsync(Http, CancellationToken.None);
            if (firstRun && settings.FoundryEndpoint is null && _ollamaModel is not null)
            {
                // Nothing configured yet and a local model is already running: use it, so the first sentence gets a verdict.
                settings = settings with { FoundryEndpoint = FoundryClient.OllamaEndpoint.ToString(), FoundryModel = _ollamaModel };
                SettingsStore.Save(settings, SettingsStore.DefaultPath);
                AttachSettings(settings);
            }

            if (settings is { Mode: HudMode.Repo, RepoPath: { } location })
            {
                // A URL is cloned (first time) or refreshed into the app's cache; a folder is used where it is.
                Status(RepoSource.IsRemote(location) ? "Fetching the repository…" : "Opening the repository…");
                try
                {
                    string? stale = null;
                    var root = await Task.Run(() => RepoSource.EnsureLocal(location, CredentialStore.Read(CredentialStore.AzdoPat), out stale));
                    settings = settings with { RepoPath = root };
                    if (stale is not null) note = $" ({stale})";
                }
                catch (Exception e) when (e is LibGit2Sharp.LibGit2SharpException or IOException or UnauthorizedAccessException)
                {
                    // Fact checking does not need the repository, so carry on without it and keep saying why.
                    settings = settings with { RepoPath = null };
                    note = $" (repository unavailable: {e.Message.TrimEnd('.')})";
                }
                viewModel.Apply(settings);   // cards label their file links relative to the folder actually read
            }

            Status("Loading models…");
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddDebug());
            services.AddSingleton(Http);
            services.AddPoVoiceHudCore(settings, models, _ollamaModel);
            var container = services.BuildServiceProvider();

            // Resolving the pipeline loads Whisper and the workspace walks the repo: keep both off the UI thread.
            var pipeline = await Task.Run(() =>
            {
                _ = container.GetService<Core.Workspace.Workspace>();
                return container.GetRequiredService<HudPipeline>();
            });
            if (run != _run)
            {
                await Task.Run(container.Dispose);   // settings were saved again while this one was loading
                return;
            }
            (_container, _pipeline) = (container, pipeline);
            pipeline.IsMuted = viewModel.IsMuted;   // the user may have muted during the download

            _ = Drain(pipeline.Status, s => viewModel.SetStatus(s + note));
            _ = Drain(pipeline.Transcript, line =>
            {
                viewModel.AddTranscript(line);
                Log(new { at = DateTimeOffset.Now, source = line.Source.ToString(), heard = line.Text });
            });
            _ = Drain(pipeline.Insights, OnInsights);
            _ = Drain(pipeline.Checking, busy => viewModel.IsChecking = busy);
            pipeline.Start();
        }
        catch (Exception e)
        {
            Status($"Start failed: {e.Message}");
        }
    }

    /// <summary>Saving settings lands here: persist, then swap the running pipeline for one built from them.</summary>
    private async Task ApplyAsync(Settings settings)
    {
        var viewModel = _window!.ViewModel;
        viewModel.Apply(settings);
        viewModel.IsChecking = false;
        viewModel.MicLevel = 0;

        var old = _container;
        (_container, _pipeline) = (null, null);
        _run++;   // a start still loading must not install its pipeline after this point
        if (old is not null) await Task.Run(old.Dispose);   // stops capture and unloads Whisper; not for the UI thread
        await StartAsync(settings, firstRun: false);
    }

    private void OnInsights(Routed routed)
    {
        _window!.ViewModel.Push(routed.Insights, DateTimeOffset.Now, routed.Line.Source);
        if (routed.Insights.Any(i => i.Verdict == Verdict.Contradicted)) Earcon.Play();
        foreach (var insight in routed.Insights)
            Log(new { at = DateTimeOffset.Now, card = insight.Headline, answer = insight.Answer, verdict = insight.Verdict?.ToString(), link = insight.Link });
    }

    /// <summary>One JSON object per line in sessions\{date}.jsonl: what was heard and what the HUD answered.</summary>
    private static void Log(object entry)
    {
        try
        {
            Directory.CreateDirectory(SessionsDir);
            File.AppendAllText(Path.Combine(SessionsDir, $"{DateTime.Now:yyyy-MM-dd}.jsonl"), JsonSerializer.Serialize(entry) + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the log is a convenience: a locked or full disk must not interrupt the call
        }
    }

    private void BindHotkeys()
    {
        var window = _window!;
        var viewModel = window.ViewModel;
        _muteKey = new GlobalHotkey(window, 1, Settings.MuteHotkeys,
            () => window.DispatcherQueue.TryEnqueue(() => viewModel.ToggleMuteCommand.Execute(null)));
        _recheckKey = new GlobalHotkey(window, 2, [RecheckHotkey], () => _pipeline?.Recheck());
        viewModel.MuteHint = (_muteKey.Binding is { } mute ? $"Mute: {mute}" : "Mute: click the microphone (hotkeys taken)")
                             + (_recheckKey.Binding is { } recheck ? $" · Check the last sentence again: {recheck}" : "");
    }

    private void AttachSettings(Settings settings) => _window!.AttachSettings(new SettingsViewModel(settings,
        readSecret: CredentialStore.Read,
        writeSecret: CredentialStore.Write,
        pickFolder: PickFolderAsync,
        save: s =>
        {
            SettingsStore.Save(s, SettingsStore.DefaultPath);
            _ = ApplyAsync(s);
        },
        connect: (endpoint, model, key) => new FoundryClient(Http, endpoint, model, key),
        ollamaModel: () => _ollamaModel));

    private async Task Drain<T>(ChannelReader<T> reader, Action<T> apply)
    {
        await foreach (var item in reader.ReadAllAsync())
            _window!.DispatcherQueue.TryEnqueue(() => apply(item));
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    /// <summary>vscode://file/{path}:{line} and https links go through the shell; VS Code focuses the line itself.</summary>
    private async Task OpenAsync(Uri uri)
    {
        if (!await Windows.System.Launcher.LaunchUriAsync(uri))
            _window?.ViewModel.SetStatus($"Nothing is registered to open {uri.Scheme}:// links");
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        File.WriteAllText(WindowFile, JsonSerializer.Serialize(_placement.ToDictionary(p => p.Key, p => p.Value.ToString())));
        _meter?.Stop();
        _muteKey?.Dispose();
        _recheckKey?.Dispose();
        _docking?.Dispose();
        _container?.Dispose();
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PoVoiceHud.App.Interop;
using PoVoiceHud.Core;
using PoVoiceHud.Core.Asr;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.Diagnostics;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Providers.FactCheck;
using PoVoiceHud.Core.Sessions;
using PoVoiceHud.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.ViewManagement;
using WinUIEx;

namespace PoVoiceHud.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The application object lives as long as the process; its hotkey, tray icon and container are released in OnClosed.")]
public partial class App : Application
{
    private static readonly string WindowFile = Path.Combine(AppPaths.DataDir, "window.json");
    private static readonly HttpClient Http = new();
    private static readonly SessionLog Sessions = new(SessionLog.DefaultDirectory);

    // WinUIEx persists window placement through this dictionary (string values only);
    // unpackaged apps have no ApplicationData, so we back it with a JSON file.
    private readonly Dictionary<string, object> _placement;
    // One file log for the whole process; every rebuilt container writes to it too.
    private readonly FileLoggerProvider _fileLog = new(Path.Combine(AppPaths.DataDir, "logs"));
    private readonly ILogger _log;
    private MainWindow? _window;
    private ServiceProvider? _container;
    private HudPipeline? _pipeline;
    private Settings _settings = new();
    private int _run;   // bumped by every (re)start, so a start that was overtaken throws its pipeline away
    private GlobalHotkey? _muteKey;
    private TrayIcon? _tray;
    private DispatcherTimer? _meter;

    public App()
    {
        InitializeComponent();
        Directory.CreateDirectory(AppPaths.DataDir);
        _log = _fileLog.CreateLogger("App");
        UnhandledException += (_, e) =>
        {
            File.AppendAllText(Path.Combine(AppPaths.DataDir, "crash.log"), $"{DateTimeOffset.Now:O} {e.Exception}\n\n");
            // Only failures known to leave the app usable are survived (a file, the network, a Windows API call).
            // Anything else is a bug: it is logged above and allowed to end the process rather than run on in an unknown state.
            e.Handled = e.Exception is IOException or UnauthorizedAccessException or HttpRequestException or TimeoutException
                or OperationCanceledException or Win32Exception or COMException;
            _log.Log(e.Handled ? LogLevel.Error : LogLevel.Critical, e.Exception, "Unhandled exception (survived: {Handled})", e.Handled);
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

    private static string Version =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var (settings, warning) = SettingsStore.Load(SettingsStore.DefaultPath);
        _settings = settings;
        var viewModel = new HudViewModel(OpenAsync);
        viewModel.Apply(settings);
        _window = new MainWindow(viewModel);
        _window.Closed += OnClosed;
        _window.Activate();
        BindHotkey();
        AttachSettings(settings);
        ApplyFeedback(settings);

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(HudViewModel.IsMuted)) return;
            if (_pipeline is { } pipeline) pipeline.IsMuted = viewModel.IsMuted;
            if (_tray is { } tray) tray.Tooltip = viewModel.IsMuted ? "PoVoiceHud — muted" : "PoVoiceHud — listening";
            if (_settings.Sounds) SoundPalette.Play(viewModel.IsMuted ? Cue.Muted : Cue.Unmuted);
        };
        viewModel.Arrived += verdict =>
        {
            if (_settings.Sounds)
                SoundPalette.Play(verdict.Verdict switch { Verdict.Supported => Cue.Supported, Verdict.Contradicted => Cue.Contradicted, _ => Cue.Unverified });
        };
        _meter = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _meter.Tick += (_, _) =>
        {
            if (_pipeline is not { } pipeline) return;
            viewModel.MicLevel = pipeline.Level;
            viewModel.LevelSource = pipeline.Speaker;
        };
        _meter.Start();

#if DEBUG
        // Layout checks without a microphone: --demo seeds a sample conversation (--facts opens the Facts page,
        // --settings shows the settings in the panel instead).
        var flags = Environment.GetCommandLineArgs();
        if (flags.Contains("--demo"))
        {
            _settings = _settings with { Sounds = false };   // the demo is for looking at, not listening to
            _window.SeedDemo();
            if (flags.Contains("--facts")) viewModel.IsFactsOpen = true;
            if (flags.Contains("--settings")) _window.ShowSettings();
            return;
        }
#endif
        _log.LogInformation("PoVoiceHud {Version} starting: {Input} audio", Version, settings.AudioInput);
        _tray = new TrayIcon(1, MainWindow.IconPath, "PoVoiceHud — listening") { IsVisible = true };
        _tray.Selected += (_, _) => ToggleVisible();
        _tray.ContextMenu += (_, e) => e.Flyout = TrayMenu();
        if (settings.StartWithWindows) Autostart.Set(true);   // keeps the entry pointing at this exe if it moved
        if (warning is not null) viewModel.SetStatus(warning);
        _ = StartAsync(settings);
        _ = CheckUpdatesAsync(userAsked: false);
    }

    /// <summary>"Sounds and animations": the cues check the setting as they play; the highlight's fade also follows Windows' animation switch.</summary>
    private void ApplyFeedback(Settings settings) => _window!.Animations = settings.Sounds && new UISettings().AnimationsEnabled;

    private async Task StartAsync(Settings settings)
    {
        var run = ++_run;
        var window = _window!;
        var viewModel = window.ViewModel;
        void Status(string text) => window.DispatcherQueue.TryEnqueue(() => viewModel.SetStatus(text));
        try
        {
            Status("Checking models…");
            var models = await ModelStore.EnsureAsync(new Progress<double>(p => Status($"Downloading models… {p:P0}")), CancellationToken.None);

            Status("Loading models…");
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddDebug().AddProvider(_fileLog));
            services.AddSingleton(Http);
            services.AddPoVoiceHudCore(settings, models);
            var container = services.BuildServiceProvider();

            // Resolving the pipeline loads Whisper: keep it off the UI thread.
            var pipeline = await Task.Run(container.GetRequiredService<HudPipeline>);
            if (run != _run)
            {
                await Task.Run(container.Dispose);   // settings were saved again while this one was loading
                return;
            }
            (_container, _pipeline) = (container, pipeline);
            pipeline.IsMuted = viewModel.IsMuted;   // the user may have muted during the download

            _ = Drain(pipeline.Status, viewModel.SetStatus);
            _ = Drain(pipeline.Transcript, line =>
            {
                viewModel.AddTranscript(line);
                Sessions.Append(new SessionEntry { At = DateTimeOffset.Now, Source = line.Source.ToString(), Heard = line.Text });
            });
            _ = Drain(pipeline.Checking, c => viewModel.MarkChecking(c.Line, c.Busy));
            _ = Drain(pipeline.Verdicts, result =>
            {
                viewModel.Push(result, DateTimeOffset.Now);
                Sessions.Append(new SessionEntry
                {
                    At = DateTimeOffset.Now, Source = result.Line.Source.ToString(), Claim = result.Verdict.Claim, Answer = result.Verdict.Answer,
                    Verdict = result.Verdict.Verdict.ToString(), Link = result.Verdict.Link,
                });
            });
            pipeline.Start();
            _log.LogInformation("Listening: {Model} at {Host}, Wikipedia {LookUp}", FoundryClient.Model,
                Uri.TryCreate(settings.FoundryEndpoint, UriKind.Absolute, out var host) ? host.Host : "none", settings.LookUpClaims ? "on" : "off");
        }
        catch (Exception e)
        {
            _log.LogError(e, "Start failed");
            Status($"Start failed: {e.Message}");
        }
    }

    /// <summary>Saving settings lands here: persist, then swap the running pipeline for one built from them.</summary>
    private async Task ApplyAsync(Settings settings)
    {
        _settings = settings;
        var viewModel = _window!.ViewModel;
        viewModel.Apply(settings);
        viewModel.ResetChecking();
        viewModel.MicLevel = 0;

        var old = _container;
        (_container, _pipeline) = (null, null);
        _run++;   // a start still loading must not install its pipeline after this point
        if (old is not null) await Task.Run(old.Dispose);   // stops capture and unloads Whisper; not for the UI thread
        _log.LogInformation("Settings applied; restarting the pipeline");
        await StartAsync(settings);
    }

    private async Task CheckUpdatesAsync(bool userAsked)
    {
        var message = await Updates.CheckAsync(_settings.UpdateFeed, _log, userAsked);
        if (message is not null) _window?.ViewModel.SetStatus(message);
    }

    /// <summary>The tray menu: built each time it opens so its labels match the current state.</summary>
    private MenuFlyout TrayMenu()
    {
        static MenuFlyoutItem Item(string text, Action run)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) => run();
            return item;
        }
        var menu = new MenuFlyout();
        menu.Items.Add(Item(_window!.AppWindow.IsVisible ? "Hide the HUD" : "Show the HUD", ToggleVisible));
        menu.Items.Add(Item("Open the history folder", () => OpenFolder(Sessions.Directory)));
        menu.Items.Add(Item("Copy diagnostics", () => _ = CollectDiagnosticsAsync()));
        menu.Items.Add(Item("Check for updates", () => _ = CheckUpdatesAsync(userAsked: true)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Quit PoVoiceHud", _window.Close));
        return menu;
    }

    private void ToggleVisible()
    {
        if (_window!.AppWindow.IsVisible) _window.AppWindow.Hide();
        else _window.AppWindow.Show();
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Zips logs and settings into %TEMP%, puts the zip on the clipboard and shows it in Explorer.</summary>
    private async Task CollectDiagnosticsAsync()
    {
        var zip = Path.Combine(Path.GetTempPath(), $"PoVoiceHud-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        var settings = _settings;
        var environment = string.Join("\n",
            $"PoVoiceHud {Version} ({(IsInstalled() ? "installed" : "not installed: run from a build folder")})",
            $"Windows {Environment.OSVersion.Version} · {RuntimeInformation.FrameworkDescription} · {Environment.ProcessorCount} logical cores",
            $"Audio {settings.AudioInput} · {FoundryClient.Model} at {(Uri.TryCreate(settings.FoundryEndpoint, UriKind.Absolute, out var endpoint) ? endpoint.Host : "none")}" +
            $" · key stored: {(CredentialStore.Read(CredentialStore.FoundryKey) is null ? "no" : "yes")}",
            $"Wikipedia: {(settings.LookUpClaims ? "on" : "off")} · start with Windows: {(settings.StartWithWindows ? "on" : "off")}" +
            $" · update feed: {(settings.UpdateFeed is null ? "none" : "set")}",
            "");
        try
        {
            await Task.Run(() => DiagnosticsBundle.Create(AppPaths.DataDir, zip, environment));
            var data = new DataPackage();
            data.SetStorageItems([await Windows.Storage.StorageFile.GetFileFromPathAsync(zip)]);
            Clipboard.SetContent(data);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = true })?.Dispose();
            _log.LogInformation("Diagnostics written to {Zip}", zip);
            _window?.ViewModel.SetStatus($"{Path.GetFileName(zip)} is on the clipboard");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _window?.ViewModel.SetStatus($"Diagnostics failed: {e.Message}");
        }
    }

    private static bool IsInstalled()
    {
        try
        {
            return new Velopack.UpdateManager(AppContext.BaseDirectory).IsInstalled;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or Velopack.Exceptions.NotInstalledException)
        {
            return false;
        }
    }

    private void BindHotkey()
    {
        var window = _window!;
        var viewModel = window.ViewModel;
        _muteKey = new GlobalHotkey(window, 1, Settings.MuteHotkeys,
            () => window.DispatcherQueue.TryEnqueue(() => viewModel.ToggleMuteCommand.Execute(null)));
        viewModel.MuteHint = _muteKey.Binding is { } mute ? $"Click or press {mute} to mute" : "Click to mute (the mute hotkeys are taken by other programs)";
    }

    private void AttachSettings(Settings settings) => _window!.AttachSettings(new SettingsViewModel(settings,
        readSecret: CredentialStore.Read,
        writeSecret: CredentialStore.Write,
        save: s =>
        {
            SettingsStore.Save(s, SettingsStore.DefaultPath);
            if (s.StartWithWindows != _settings.StartWithWindows && !Autostart.Set(s.StartWithWindows))
                _window!.ViewModel.SetStatus("Windows did not allow changing the startup list.");
            ApplyFeedback(s);
            // Sounds and start-up are the app's business: changing only those needs no new pipeline.
            if (s with { Sounds = _settings.Sounds, StartWithWindows = _settings.StartWithWindows } == _settings) _settings = s;
            else _ = ApplyAsync(s);
        },
        connect: (endpoint, key) => new FoundryClient(Http, endpoint, key)), quit: () => _window!.Close());

    private async Task Drain<T>(ChannelReader<T> reader, Action<T> apply)
    {
        await foreach (var item in reader.ReadAllAsync())
            _window!.DispatcherQueue.TryEnqueue(() => apply(item));
    }

    /// <summary>A fact's source link goes through the shell.</summary>
    private async Task OpenAsync(Uri uri)
    {
        if (!await Windows.System.Launcher.LaunchUriAsync(uri))
            _window?.ViewModel.SetStatus($"Nothing is registered to open {uri.Scheme}:// links");
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        File.WriteAllText(WindowFile, JsonSerializer.Serialize(_placement.ToDictionary(p => p.Key, p => p.Value.ToString())));
        _log.LogInformation("Closing");
        _meter?.Stop();
        _muteKey?.Dispose();
        _tray?.Dispose();
        _container?.Dispose();
    }
}

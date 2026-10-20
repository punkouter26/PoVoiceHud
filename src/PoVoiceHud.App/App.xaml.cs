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
using PoVoiceHud.Core.Workspace;
using Windows.ApplicationModel.DataTransfer;
using WinUIEx;

namespace PoVoiceHud.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The application object lives as long as the process; its hotkeys, tray icon, docking and container are released in OnClosed.")]
public partial class App : Application
{
    private const string RecheckHotkey = "Ctrl+Alt+R";
    private static readonly string[] RecapHotkeys = ["Ctrl+Alt+S", "Ctrl+Shift+Alt+S"];
    // A pause this long ends a conversation: the recap covers what was said since then (or since the last recap).
    private static readonly TimeSpan ConversationGap = TimeSpan.FromMinutes(20);

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
    private string? _ollamaModel;
    private GlobalHotkey? _muteKey;
    private GlobalHotkey? _recheckKey;
    private GlobalHotkey? _recapKey;
    private TrayIcon? _tray;
    private WindowDocking? _docking;
    private DispatcherTimer? _meter;
    private bool _recapping;

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
        var firstRun = !File.Exists(SettingsStore.DefaultPath);
        var (settings, warning) = SettingsStore.Load(SettingsStore.DefaultPath);
        _settings = settings;
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
            if (e.PropertyName != nameof(HudViewModel.IsMuted)) return;
            if (_pipeline is { } pipeline) pipeline.IsMuted = viewModel.IsMuted;
            if (_tray is { } tray) tray.Tooltip = viewModel.IsMuted ? "PoVoiceHud — muted" : "PoVoiceHud — listening";
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
        // Layout checks without a microphone: --demo seeds sample cards (add --settings to show the settings instead), --pin starts expanded.
        var flags = Environment.GetCommandLineArgs();
        if (flags.Contains("--pin")) viewModel.ToggleExpandCommand.Execute(null);
        if (flags.Contains("--demo"))
        {
            _window.SeedDemo();
            // the demo is for looking at, not listening to; --effects Off|Subtle|Full overrides the saved level
            var level = Array.IndexOf(flags, "--effects") is var e and >= 0 && e + 1 < flags.Length
                        && Enum.TryParse<EffectsLevel>(flags[e + 1], ignoreCase: true, out var chosen) ? chosen : settings.Effects;
            _window.Effects.Apply(level, sounds: false);
            if (flags.Contains("--settings")) _window.ShowSettings(everything: flags.Contains("--all"));
            // --fx <moment> plays one effect and holds a frame of it, once the window has been laid out
            if (Array.IndexOf(flags, "--fx") is var at and >= 0 && at + 1 < flags.Length)
            {
                var moment = flags[at + 1];
                var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                later.Tick += (_, _) =>
                {
                    later.Stop();
                    _window.Preview(moment);
                };
                later.Start();
            }
            return;
        }
#endif
        _window.Effects.Apply(settings.Effects, settings.Sounds);
        _log.LogInformation("PoVoiceHud {Version} starting: {Mode} mode, {Input} audio", Version, settings.Mode, settings.AudioInput);
        _tray = new TrayIcon(1, MainWindow.IconPath, "PoVoiceHud — listening") { IsVisible = true };
        _tray.Selected += (_, _) => ToggleVisible();
        _tray.ContextMenu += (_, e) => e.Flyout = TrayMenu();
        viewModel.Rated += OnRated;
        viewModel.RecapRequested += () => _ = RecapAsync();
        ShowRatings();
        if (settings.StartWithWindows) Autostart.Set(true);   // keeps the entry pointing at this exe if it moved
        if (warning is not null) viewModel.SetStatus(warning);
        _ = StartAsync(settings, firstRun);
        _ = CheckUpdatesAsync(userAsked: false);
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
                _settings = settings;
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
                    _log.LogWarning(e, "Repository {Location} unavailable", location);
                    settings = settings with { RepoPath = null };
                    note = $" (repository unavailable: {e.Message.TrimEnd('.')})";
                }
                viewModel.Apply(settings);   // cards label their file links relative to the folder actually read
            }

            Status("Loading models…");
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddDebug().AddProvider(_fileLog));
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
                Sessions.Append(new SessionEntry { At = DateTimeOffset.Now, Source = line.Source.ToString(), Heard = line.Text });
            });
            _ = Drain(pipeline.Insights, OnInsights);
            _ = Drain(pipeline.Checking, busy => viewModel.IsChecking = busy);
            pipeline.Start();
            _log.LogInformation("Listening: model {Model} at {Host}, repository {Repo}, Wikipedia {LookUp}",
                settings.FoundryModel, Uri.TryCreate(settings.FoundryEndpoint, UriKind.Absolute, out var host) ? host.Host : "none",
                settings.RepoPath is null ? "none" : "open", settings.LookUpClaims ? "on" : "off");
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
        Autostart.Set(settings.StartWithWindows);
        _window!.Effects.Apply(settings.Effects, settings.Sounds);
        var viewModel = _window.ViewModel;
        viewModel.Apply(settings);
        viewModel.IsChecking = false;
        viewModel.MicLevel = 0;

        var old = _container;
        (_container, _pipeline) = (null, null);
        _run++;   // a start still loading must not install its pipeline after this point
        if (old is not null) await Task.Run(old.Dispose);   // stops capture and unloads Whisper; not for the UI thread
        _log.LogInformation("Settings applied; restarting the pipeline");
        await StartAsync(settings, firstRun: false);
    }

    private void OnInsights(Routed routed)
    {
        _window!.ViewModel.Push(routed.Insights, DateTimeOffset.Now, routed.Line.Source);   // the effects play the verdict's sound
        foreach (var insight in routed.Insights)
            Sessions.Append(new SessionEntry
            {
                At = DateTimeOffset.Now, Source = routed.Line.Source.ToString(), Card = insight.Headline, Answer = insight.Answer,
                Verdict = insight.Verdict?.ToString(), Link = insight.Link,
            });
    }

    private void OnRated(InsightCardViewModel card)
    {
        Sessions.Append(new SessionEntry { At = DateTimeOffset.Now, Card = card.Insight.Headline, Rating = card.Rating.ToString().ToLowerInvariant() });
        ShowRatings();
    }

    private void ShowRatings()
    {
        var now = DateTimeOffset.Now;
        var (up, down) = Sessions.Ratings(now.AddDays(-7), now);
        _window!.ViewModel.SetRatings(up, down);
    }

    /// <summary>
    /// Writes the recap of the conversation going on now to sessions\{date}-recap-{time}.md, copies it to the clipboard and
    /// shows it as a card that opens the file. Without a pipeline (still starting) it is written without a model summary.
    /// </summary>
    private async Task RecapAsync()
    {
        if (_recapping || _window is not { } window) return;
        _recapping = true;
        var viewModel = window.ViewModel;
        try
        {
            viewModel.SetStatus("Writing the recap…");
            var now = DateTimeOffset.Now;
            var writer = _container?.GetService<RecapWriter>() ?? new RecapWriter(model: null);
            var recap = await writer.WriteAsync(Sessions.Conversation(now, ConversationGap), CancellationToken.None);
            Directory.CreateDirectory(Sessions.Directory);
            var file = Path.Combine(Sessions.Directory, $"{now:yyyy-MM-dd}-recap-{now:HHmm}.md");
            await File.WriteAllTextAsync(file, recap.Markdown);
            Sessions.Append(new SessionEntry { At = now, Recap = file });

            var data = new DataPackage();
            data.SetText(recap.Markdown);
            Clipboard.SetContent(data);
            viewModel.Push([new Insight(Core.Routing.Intent.None, "[RECAP] This call", $"{recap.Summary}. Copied — paste it anywhere.", [], Link: new Uri(file))],
                now, SourceHint.Unknown);
            viewModel.RecapDone();
            _log.LogInformation("Recap written to {File}", file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException)
        {
            _log.LogError(e, "Recap failed");
            viewModel.SetStatus($"Recap failed: {e.Message}");
        }
        finally
        {
            _recapping = false;
        }
    }

    private async Task CheckUpdatesAsync(bool userAsked)
    {
        var message = await Updates.CheckAsync(_settings.UpdateFeed, _log, userAsked);
        if (message is not null) _window?.ViewModel.SetStatus(message);
    }

    /// <summary>The tray menu, built each time it opens so its labels and checkmark match the current state.</summary>
    private MenuFlyout TrayMenu()
    {
        var viewModel = _window!.ViewModel;
        static MenuFlyoutItem Item(string text, Action run)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) => run();
            return item;
        }
        var autostart = new ToggleMenuFlyoutItem { Text = "Start with Windows", IsChecked = _settings.StartWithWindows };
        autostart.Click += (_, _) => ToggleAutostart();

        var menu = new MenuFlyout();
        menu.Items.Add(Item(_window.AppWindow.IsVisible ? "Hide the HUD" : "Show the HUD", ToggleVisible));
        menu.Items.Add(Item(viewModel.IsMuted ? "Unmute" : "Mute", () => viewModel.ToggleMuteCommand.Execute(null)));
        menu.Items.Add(Item("Recap this call", () => _ = RecapAsync()));
        menu.Items.Add(Item("Open the sessions folder", () => OpenFolder(Sessions.Directory)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(autostart);
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

    /// <summary>From the tray: saved straight away, without rebuilding the pipeline (nothing it uses changed).</summary>
    private void ToggleAutostart()
    {
        var settings = _settings with { StartWithWindows = !_settings.StartWithWindows };
        if (!Autostart.Set(settings.StartWithWindows))
        {
            _window!.ViewModel.SetStatus("Windows did not allow changing the startup list.");
            return;
        }
        SettingsStore.Save(settings, SettingsStore.DefaultPath);
        _settings = settings;
        AttachSettings(settings);
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Zips logs and settings into %TEMP%, puts the zip on the clipboard and shows it in Explorer.</summary>
    private async Task<string> CollectDiagnosticsAsync()
    {
        var zip = Path.Combine(Path.GetTempPath(), $"PoVoiceHud-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        var settings = _settings;
        var environment = string.Join("\n",
            $"PoVoiceHud {Version} ({(IsInstalled() ? "installed" : "not installed: run from a build folder")})",
            $"Windows {Environment.OSVersion.Version} · {RuntimeInformation.FrameworkDescription} · {Environment.ProcessorCount} logical cores",
            $"Mode {settings.Mode} · audio {settings.AudioInput} · sources {settings.EnabledSources}",
            $"Model {settings.FoundryModel} at {(Uri.TryCreate(settings.FoundryEndpoint, UriKind.Absolute, out var endpoint) ? endpoint.Host : "none")}" +
            $" · key stored: {(CredentialStore.Read(CredentialStore.FoundryKey) is null ? "no" : "yes")} · local Ollama: {_ollamaModel ?? "not running"}",
            $"Repository: {(settings.RepoPath is null ? "none" : "set")} · Wikipedia: {(settings.LookUpClaims ? "on" : "off")}" +
            $" · start with Windows: {(settings.StartWithWindows ? "on" : "off")} · update feed: {(settings.UpdateFeed is null ? "none" : "set")}",
            "");
        await Task.Run(() => DiagnosticsBundle.Create(AppPaths.DataDir, zip, environment));

        var data = new DataPackage();
        data.SetStorageItems([await Windows.Storage.StorageFile.GetFileFromPathAsync(zip)]);
        Clipboard.SetContent(data);
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = true })?.Dispose();
        _log.LogInformation("Diagnostics written to {Zip}", zip);
        return $"✓ {Path.GetFileName(zip)} is on the clipboard — paste it into an email or a bug report.";
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

    private void BindHotkeys()
    {
        var window = _window!;
        var viewModel = window.ViewModel;
        _muteKey = new GlobalHotkey(window, 1, Settings.MuteHotkeys,
            () => window.DispatcherQueue.TryEnqueue(() => viewModel.ToggleMuteCommand.Execute(null)));
        _recheckKey = new GlobalHotkey(window, 2, [RecheckHotkey], () => _pipeline?.Recheck());
        _recapKey = new GlobalHotkey(window, 3, RecapHotkeys, () => window.DispatcherQueue.TryEnqueue(() => viewModel.RecapCommand.Execute(null)));
        viewModel.RecapHotkey = _recapKey.Binding;
        viewModel.MuteHint = (_muteKey.Binding is { } mute ? $"Mute: {mute}" : "Mute: click the microphone (hotkeys taken)")
                             + (_recheckKey.Binding is { } recheck ? $" · Check the last sentence again: {recheck}" : "")
                             + (_recapKey.Binding is { } recap ? $" · Recap: {recap}" : "");
    }

    private void AttachSettings(Settings settings) => _window!.AttachSettings(new SettingsViewModel(settings,
        readSecret: CredentialStore.Read,
        writeSecret: CredentialStore.Write,
        pickFolder: PickFolderAsync,
        save: s =>
        {
            SettingsStore.Save(s, SettingsStore.DefaultPath);
            // Effects and sounds are the window's business: changing only those needs no new pipeline.
            if (s with { Effects = _settings.Effects, Sounds = _settings.Sounds } == _settings)
            {
                _settings = s;
                _window!.Effects.Apply(s.Effects, s.Sounds);
            }
            else _ = ApplyAsync(s);
        },
        connect: (endpoint, model, key) => new FoundryClient(Http, endpoint, model, key),
        ollamaModel: () => _ollamaModel,
        collectDiagnostics: CollectDiagnosticsAsync));

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

    /// <summary>
    /// vscode://file/{path}:{line} and https links go through the shell; VS Code focuses the line itself.
    /// A file link (a recap) opens in whatever edits Markdown on this PC.
    /// </summary>
    private async Task OpenAsync(Uri uri)
    {
        if (uri.IsFile)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.LocalPath) { UseShellExecute = true })?.Dispose();
            }
            catch (Win32Exception e)
            {
                _window?.ViewModel.SetStatus($"Could not open {Path.GetFileName(uri.LocalPath)}: {e.Message}");
            }
            return;
        }
        if (!await Windows.System.Launcher.LaunchUriAsync(uri))
            _window?.ViewModel.SetStatus($"Nothing is registered to open {uri.Scheme}:// links");
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        File.WriteAllText(WindowFile, JsonSerializer.Serialize(_placement.ToDictionary(p => p.Key, p => p.Value.ToString())));
        _log.LogInformation("Closing");
        _meter?.Stop();
        _muteKey?.Dispose();
        _recheckKey?.Dispose();
        _recapKey?.Dispose();
        _tray?.Dispose();
        _docking?.Dispose();
        _container?.Dispose();
    }
}

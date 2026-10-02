using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using PoVoiceHud.App.Interop;
using PoVoiceHud.Core;
using PoVoiceHud.Core.Asr;
using PoVoiceHud.Core.Config;
using PoVoiceHud.Core.ViewModels;
using WinUIEx;

namespace PoVoiceHud.App;

public partial class App : Application
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoVoiceHud");

    private static readonly string WindowFile = Path.Combine(DataDir, "window.json");

    // WinUIEx persists window placement through this dictionary (string values only);
    // unpackaged apps have no ApplicationData, so we back it with a JSON file.
    private readonly Dictionary<string, object> _placement;
    private MainWindow? _window;
    private ServiceProvider? _container;
    private GlobalHotkey? _hotkey;
    private WindowDocking? _docking;

    public App()
    {
        InitializeComponent();
        Directory.CreateDirectory(DataDir);
        UnhandledException += (_, e) =>
        {
            File.AppendAllText(Path.Combine(DataDir, "crash.log"), $"{DateTimeOffset.Now:O} {e.Exception}\n\n");
            e.Handled = true;
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
        var (settings, warning) = SettingsStore.Load(SettingsStore.DefaultPath);
        var viewModel = new HudViewModel(TimeSpan.FromSeconds(settings.AutoExpandSeconds), settings.RepoPath, OpenAsync);
        _window = new MainWindow(viewModel);
        _window.Closed += OnClosed;
        _window.Activate();
        _docking = new WindowDocking(_window);
        _hotkey = new GlobalHotkey(_window, () => _window.DispatcherQueue.TryEnqueue(() => viewModel.ToggleMuteCommand.Execute(null)));
        viewModel.MuteHint = _hotkey.Binding is { } binding ? $"Mute: {binding}" : "Mute: click the dot (hotkeys taken)";

        _window.AttachSettings(new SettingsViewModel(settings,
            readSecret: CredentialStore.Read,
            writeSecret: CredentialStore.Write,
            pickFolder: PickFolderAsync,
            save: s => SettingsStore.Save(s, SettingsStore.DefaultPath)));

        var args2 = Environment.GetCommandLineArgs();
        if (args2.Contains("--pin")) viewModel.ToggleExpandCommand.Execute(null);   // start pinned open (manual testing)
        if (args2.Contains("--demo"))
        {
            _window.SeedDemo();
            _window.ShowSettings();
            return;
        }
        if (warning is not null) viewModel.SetStatus(warning);
        _ = StartAsync(settings, viewModel);
    }

    private async Task StartAsync(Settings settings, HudViewModel viewModel)
    {
        var window = _window!;
        void Status(string text) => window.DispatcherQueue.TryEnqueue(() => viewModel.SetStatus($"{ModeLabel(settings)} · {text}"));
        try
        {
            Status("Checking models…");
            var models = await ModelStore.EnsureAsync(settings.WhisperModel,
                new Progress<double>(p => Status($"Downloading models… {p:P0}")), CancellationToken.None);

            Status("Loading models…");
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddDebug());
            services.AddPoVoiceHudCore(settings, models);
            _container = services.BuildServiceProvider();

            // Resolving the pipeline loads Whisper (seconds for small.en): keep it off the UI thread.
            var pipeline = await Task.Run(() => _container.GetRequiredService<HudPipeline>());
            pipeline.IsMuted = viewModel.IsMuted;   // the user may have muted during the download
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(HudViewModel.IsMuted)) pipeline.IsMuted = viewModel.IsMuted;
            };

            _ = Drain(pipeline.Status, s => viewModel.SetStatus($"{ModeLabel(settings)} · {s}"));
            _ = Drain(pipeline.Transcript, viewModel.AddTranscript);
            _ = Drain(pipeline.Insights, insights => viewModel.Push(insights, DateTimeOffset.Now));
            pipeline.Start();
        }
        catch (Exception e)
        {
            Status($"Start failed: {e.Message}");
        }
    }

    private async Task Drain<T>(ChannelReader<T> reader, Action<T> apply)
    {
        await foreach (var item in reader.ReadAllAsync())
            _window!.DispatcherQueue.TryEnqueue(() => apply(item));
    }

    private static string ModeLabel(Settings settings) => settings.Mode == HudMode.Simple
        ? "Mic · Fact check"
        : $"{settings.AudioInput} · {settings.EnabledSources}";

    private async Task<string?> PickFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    private async Task OpenAsync(Uri uri)
    {
        if (!await VsCodeLauncher.OpenAsync(uri))
            _window?.ViewModel.SetStatus($"Nothing is registered to open {uri.Scheme}:// links");
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        File.WriteAllText(WindowFile, JsonSerializer.Serialize(_placement.ToDictionary(p => p.Key, p => p.Value.ToString())));
        _hotkey?.Dispose();
        _docking?.Dispose();
        _container?.Dispose();
    }
}

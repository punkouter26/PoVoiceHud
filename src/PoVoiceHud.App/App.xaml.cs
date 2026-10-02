using System.Text.Json;
using Microsoft.UI.Xaml;
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
    private Window? _window;

    public App()
    {
        InitializeComponent();
        Directory.CreateDirectory(DataDir);
        var saved = File.Exists(WindowFile)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(WindowFile))
            : null;
        _placement = saved?.ToDictionary(p => p.Key, p => (object)p.Value) ?? [];
        WindowManager.PersistenceStorage = _placement;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var viewModel = new HudViewModel(TimeSpan.FromSeconds(20), repoRoot: null, openUri: _ => Task.CompletedTask);
        var window = new MainWindow(viewModel);
        if (Environment.GetCommandLineArgs().Contains("--demo")) window.SeedDemo();
        window.Closed += (_, _) => File.WriteAllText(WindowFile,
            JsonSerializer.Serialize(_placement.ToDictionary(p => p.Key, p => p.Value.ToString())));
        _window = window;
        _window.Activate();
    }
}

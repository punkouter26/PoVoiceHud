using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Config;

public enum HudMode { Simple, Repo }

public enum AudioInput { Mic, Remote, Both }

public sealed record Settings
{
    /// <summary>Repo mode does everything Simple mode does (fact check) and adds the repository sources.</summary>
    public const Intent RepoSources = Intent.FactCheck | Intent.Git | Intent.Schema | Intent.Docs | Intent.Pipeline | Intent.Code;

    /// <summary>Mute hotkeys in order of preference; the first one no other program owns gets registered.</summary>
    public static readonly IReadOnlyList<string> MuteHotkeys = ["Win+Shift+M", "Ctrl+Shift+M", "Ctrl+Alt+M", "Ctrl+Shift+F12"];

    public HudMode Mode { get; init; } = HudMode.Simple;
    public AudioInput AudioInput { get; init; } = AudioInput.Mic;
    public Intent EnabledSources { get; init; } = Intent.FactCheck;
    /// <summary>A folder holding a git repository, or the https URL of one (Azure DevOps), which is cloned into the app's cache.</summary>
    public string? RepoPath { get; init; }
    /// <summary>Half the logical cores, 2–8: beyond 8 Whisper gains little, and all cores would starve the call itself.</summary>
    public int WhisperThreads { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
    public string? AzdoOrgUrl { get; init; }
    public string? AzdoProject { get; init; }
    public string? FoundryEndpoint { get; init; }
    public string FoundryModel { get; init; } = "Llama-3.3-70B-Instruct";
    public int AutoExpandSeconds { get; init; } = 20;

    /// <summary>Mode presets (SPEC §1a); individual toggles can be changed afterwards.</summary>
    public Settings ApplyMode(HudMode mode) => mode switch
    {
        HudMode.Simple => this with { Mode = mode, AudioInput = AudioInput.Mic, EnabledSources = Intent.FactCheck },
        _ => this with { Mode = mode, AudioInput = AudioInput.Both, EnabledSources = RepoSources },
    };
}

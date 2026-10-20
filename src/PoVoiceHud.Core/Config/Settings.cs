using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Config;

public enum HudMode { Simple, Repo }

public enum AudioInput { Mic, Remote, Both }

/// <summary>How much the HUD moves and glows. Windows' "Animation effects" switch and battery saver can lower it further.</summary>
public enum EffectsLevel { Off, Subtle, Full }

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
    /// <summary>Looks each claim up on Wikipedia before the model judges it, so the verdict can cite a real article.</summary>
    public bool LookUpClaims { get; init; } = true;
    public bool StartWithWindows { get; init; }
    /// <summary>Shaders, springs and particles; see <see cref="EffectsLevel"/>.</summary>
    public EffectsLevel Effects { get; init; } = EffectsLevel.Full;
    /// <summary>Short synthesized cues for verdicts, mute and ratings, each under the 300 ms the segmenter ignores.</summary>
    public bool Sounds { get; init; } = true;
    /// <summary>
    /// Comma-separated words Whisper should expect (names, product terms), on top of the ones read from the repository.
    /// A string rather than a list so that settings compare by value.
    /// </summary>
    public string? Vocabulary { get; init; }
    /// <summary>Where new versions are published: an https URL or a folder that holds Velopack releases. Empty: no update check.</summary>
    public string? UpdateFeed { get; init; }

    /// <summary>Mode presets (SPEC §1a); individual toggles can be changed afterwards.</summary>
    public Settings ApplyMode(HudMode mode) => mode switch
    {
        HudMode.Simple => this with { Mode = mode, AudioInput = AudioInput.Mic, EnabledSources = Intent.FactCheck },
        _ => this with { Mode = mode, AudioInput = AudioInput.Both, EnabledSources = RepoSources },
    };
}

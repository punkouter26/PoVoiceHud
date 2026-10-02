using PoVoiceHud.Core.Routing;

namespace PoVoiceHud.Core.Config;

public enum HudMode { Simple, Repo }

public enum AudioInput { Mic, Remote, Both }

public sealed record Settings
{
    public const Intent RepoSources = Intent.Git | Intent.Schema | Intent.Docs | Intent.Pipeline;

    public HudMode Mode { get; init; } = HudMode.Simple;
    public AudioInput AudioInput { get; init; } = AudioInput.Mic;
    public Intent EnabledSources { get; init; } = Intent.FactCheck;
    public string? RepoPath { get; init; }
    public string WhisperModel { get; init; } = "base.en";
    public int WhisperThreads { get; init; } = 8;
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

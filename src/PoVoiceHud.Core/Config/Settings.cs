namespace PoVoiceHud.Core.Config;

public sealed record Settings
{
    public string? RepoPath { get; init; }
    public string WhisperModel { get; init; } = "base.en";
    public int WhisperThreads { get; init; } = 8;
    public string? AzdoOrgUrl { get; init; }
    public string? AzdoProject { get; init; }
    public int AutoExpandSeconds { get; init; } = 20;
}

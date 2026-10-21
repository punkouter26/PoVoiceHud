namespace PoVoiceHud.Core.Config;

/// <summary>Which devices <see cref="Audio.WasapiDualCapture"/> opens. The app always opens both: you and the call.</summary>
public enum AudioInput { Mic, Remote, Both }

/// <summary>
/// Everything in settings.json. The Settings panel shows all of it except <see cref="WhisperThreads"/>,
/// <see cref="Vocabulary"/> and <see cref="UpdateFeed"/>, which are for the file only.
/// </summary>
public sealed record Settings
{
    /// <summary>The Azure AI Foundry resource root, https://&lt;resource&gt;.services.ai.azure.com. The key is in Credential Manager.</summary>
    public string? FoundryEndpoint { get; init; }
    /// <summary>Looks each claim up on Wikipedia before the model judges it, so the verdict can cite a real article.</summary>
    public bool LookUpClaims { get; init; } = true;
    /// <summary>
    /// The code repository claims about the code are checked against, optional: an https git URL (GitHub or Azure DevOps,
    /// cloned into the app's cache) or a folder holding a git repository. Its access token is in Credential Manager.
    /// </summary>
    public string? Repository { get; init; }
    public bool StartWithWindows { get; init; }
    /// <summary>Short cues for verdicts and mute (each under the 300 ms the segmenter ignores) and the highlight's fade.</summary>
    public bool Sounds { get; init; } = true;
    /// <summary>Half the logical cores, 2–8: beyond 8 Whisper gains little, and all cores would starve the call itself.</summary>
    public int WhisperThreads { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
    /// <summary>Comma-separated words Whisper should expect (names, product terms). A string so that settings compare by value.</summary>
    public string? Vocabulary { get; init; }
    /// <summary>Where new versions are published: an https URL or a folder that holds Velopack releases. Empty: no update check.</summary>
    public string? UpdateFeed { get; init; }
}

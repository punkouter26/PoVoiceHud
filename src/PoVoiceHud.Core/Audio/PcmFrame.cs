namespace PoVoiceHud.Core.Audio;

public enum SourceHint { Unknown, Teams, Mic }

public static class SourceHintExtensions
{
    /// <summary>Who said it, as the HUD labels it: the microphone is you, the call audio is everyone else.</summary>
    public static string Speaker(this SourceHint source) => source switch
    {
        SourceHint.Mic => "You",
        SourceHint.Teams => "Them",
        _ => "?",
    };
}

/// <summary>100 ms of 16 kHz mono PCM16 with a guess at who was speaking.</summary>
public readonly record struct PcmFrame(ReadOnlyMemory<short> Samples, SourceHint Source, TimeSpan Timestamp)
{
    public const int SampleRate = 16_000;
    public const int SamplesPerFrame = SampleRate / 10;
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(100);
}

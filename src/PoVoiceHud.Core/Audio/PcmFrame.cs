namespace PoVoiceHud.Core.Audio;

public enum SourceHint { Unknown, Teams, Mic }

/// <summary>100 ms of 16 kHz mono PCM16 with a guess at who was speaking.</summary>
public readonly record struct PcmFrame(ReadOnlyMemory<short> Samples, SourceHint Source, TimeSpan Timestamp)
{
    public const int SampleRate = 16_000;
    public const int SamplesPerFrame = SampleRate / 10;
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(100);
}

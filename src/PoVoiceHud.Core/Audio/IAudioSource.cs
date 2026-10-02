namespace PoVoiceHud.Core.Audio;

public interface IAudioSource : IDisposable
{
    event Action<PcmFrame>? FrameAvailable;
    event Action<string>? Status;

    /// <summary>Muting stops capture and discards everything buffered so far.</summary>
    bool IsMuted { get; set; }

    void Start();
    void Stop();
}

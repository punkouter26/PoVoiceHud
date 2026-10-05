using System.Buffers.Binary;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PoVoiceHud.Core.Audio;

/// <summary>
/// A 60 ms tone on the default output: the cue for a contradicted claim. It is far shorter than the 300 ms
/// blip the segmenter drops, so it never comes back as a transcript when loopback capture is on.
/// </summary>
public static class Earcon
{
    private const int Rate = 48_000;
    private static readonly byte[] Tone = Render(hz: 660, milliseconds: 60, gain: 0.25);

    public static void Play()
    {
        try
        {
            var output = new WasapiOut(AudioClientShareMode.Shared, 50);
            output.PlaybackStopped += (_, _) => output.Dispose();
            output.Init(new RawSourceWaveStream(new MemoryStream(Tone), new WaveFormat(Rate, 16, 1)));
            output.Play();
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // no output device, or it is held exclusively: a missing beep is not worth surfacing
        }
    }

    private static byte[] Render(double hz, int milliseconds, double gain)
    {
        var samples = Rate * milliseconds / 1000;
        var bytes = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var envelope = Math.Sin(Math.PI * i / samples);   // fade in and out, so there is no click
            var value = Math.Sin(2 * Math.PI * hz * i / Rate) * envelope * gain * short.MaxValue;
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), (short)value);
        }
        return bytes;
    }
}

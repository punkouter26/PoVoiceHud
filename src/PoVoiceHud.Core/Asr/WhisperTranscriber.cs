using System.Text;
using PoVoiceHud.Core.Audio;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace PoVoiceHud.Core.Asr;

/// <summary>
/// Whisper.net on the CPU runtime. The encoder cost is set by audio_ctx (50 per second, 1500 = 30 s), so
/// utterances up to 10 s use a 512-context processor (~3x faster, same text on the fixture) and longer ones the full window.
/// </summary>
public sealed class WhisperTranscriber : ITranscriber
{
    private const int ShortContext = 512;
    private const int ShortMaxSamples = ShortContext / 50 * Audio.PcmFrame.SampleRate;

    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _short;
    private readonly WhisperProcessor _long;
    private readonly SemaphoreSlim _one = new(1, 1);

    static WhisperTranscriber() => RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];

    public WhisperTranscriber(string modelPath, int threads)
    {
        _factory = WhisperFactory.FromPath(modelPath);
        _short = _factory.CreateBuilder().WithLanguage("en").WithThreads(threads).WithAudioContextSize(ShortContext).Build();
        _long = _factory.CreateBuilder().WithLanguage("en").WithThreads(threads).Build();
    }

    public async Task<string> TranscribeAsync(short[] pcm, CancellationToken ct)
    {
        await _one.WaitAsync(ct);
        try
        {
            var processor = pcm.Length <= ShortMaxSamples ? _short : _long;
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(SampleConvert.ToFloat(pcm), ct))
                text.Append(segment.Text);
            var result = text.ToString().Trim();
            return result.StartsWith('[') && result.EndsWith(']') ? "" : result; // "[BLANK_AUDIO]" etc.
        }
        finally
        {
            _one.Release();
        }
    }

    public void Dispose()
    {
        // A transcription cancelled by the pipeline may still be unwinding inside the native processor.
        _one.Wait(TimeSpan.FromSeconds(5));
        _short.Dispose();
        _long.Dispose();
        _factory.Dispose();
        _one.Dispose();
    }
}

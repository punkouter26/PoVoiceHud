using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace PoVoiceHud.Core.Audio;

/// <summary>
/// Pull graph: loopback and/or mic byte streams → per-input RMS tap → mono → 16 kHz → mix → PCM16 frames.
/// A null format means that input is not captured. Starved inputs read as silence, so frames keep flowing.
/// </summary>
public sealed class MixGraph
{
    private const float AudibleRms = 0.001f;

    private readonly BufferedWaveProvider? _loop;
    private readonly BufferedWaveProvider? _mic;
    private readonly RmsTap? _loopTap;
    private readonly RmsTap? _micTap;
    private readonly MixingSampleProvider _mixer;
    private readonly float[] _floats = new float[PcmFrame.SamplesPerFrame];
    private long _frames;

    public MixGraph(WaveFormat? loopFormat, WaveFormat? micFormat)
    {
        if (loopFormat is null && micFormat is null) throw new ArgumentException("At least one input is required.");
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(PcmFrame.SampleRate, 1)) { ReadFully = true };
        if (loopFormat is not null)
        {
            _loop = Buffer(loopFormat);
            _loopTap = new RmsTap(_loop.ToSampleProvider());
            _mixer.AddMixerInput(To16kMono(_loopTap));
        }
        if (micFormat is not null)
        {
            _mic = Buffer(micFormat);
            _micTap = new RmsTap(_mic.ToSampleProvider());
            _mixer.AddMixerInput(To16kMono(_micTap));
        }
    }

    public TimeSpan Buffered
    {
        get
        {
            var loop = _loop?.BufferedDuration ?? TimeSpan.Zero;
            var mic = _mic?.BufferedDuration ?? TimeSpan.Zero;
            return loop > mic ? loop : mic;
        }
    }

    public void AddLoopback(byte[] buffer, int? count = null) => _loop?.AddSamples(buffer, 0, count ?? buffer.Length);

    public void AddMic(byte[] buffer, int? count = null) => _mic?.AddSamples(buffer, 0, count ?? buffer.Length);

    public PcmFrame ReadFrame()
    {
        _mixer.Read(_floats, 0, _floats.Length);
        var pcm = new short[PcmFrame.SamplesPerFrame];
        SampleConvert.ToPcm16(_floats, pcm);

        var loop = _loopTap?.TakeRms() ?? 0f;
        var mic = _micTap?.TakeRms() ?? 0f;
        var source = loop > AudibleRms && loop > mic * 1.5f ? SourceHint.Teams
                   : mic > AudibleRms && mic > loop * 1.5f ? SourceHint.Mic
                   : SourceHint.Unknown;
        return new PcmFrame(pcm, source, PcmFrame.Duration * _frames++);
    }

    public void Clear()
    {
        _loop?.ClearBuffer();
        _mic?.ClearBuffer();
    }

    private static BufferedWaveProvider Buffer(WaveFormat format) => new(format)
    {
        BufferDuration = TimeSpan.FromSeconds(5),
        DiscardOnBufferOverflow = true,
        ReadFully = true,
    };

    private static ISampleProvider To16kMono(ISampleProvider s)
    {
        if (s.WaveFormat.Channels == 2) s = s.ToMono();
        else if (s.WaveFormat.Channels > 2) s = new MultiplexingSampleProvider([s], 1);
        return new WdlResamplingSampleProvider(s, PcmFrame.SampleRate);
    }

    private sealed class RmsTap(ISampleProvider source) : ISampleProvider
    {
        private double _sumSquares;
        private long _count;

        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, count);
            for (var i = 0; i < read; i++) _sumSquares += buffer[offset + i] * buffer[offset + i];
            _count += read;
            return read;
        }

        public float TakeRms()
        {
            var rms = _count == 0 ? 0f : (float)Math.Sqrt(_sumSquares / _count);
            (_sumSquares, _count) = (0, 0);
            return rms;
        }
    }
}

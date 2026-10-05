using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PoVoiceHud.Core.Asr;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Vad;

namespace PoVoiceHud.Core;

public sealed record TranscriptLine(SourceHint Source, TimeSpan At, string Text);

/// <summary>The cards one transcript line produced.</summary>
public sealed record Routed(TranscriptLine Line, IReadOnlyList<Insight> Insights);

/// <summary>
/// Audio frames → VAD → utterances → Whisper → router, one utterance at a time. Muting bumps a generation
/// counter so anything already in flight is discarded when it completes.
/// </summary>
public sealed class HudPipeline : IDisposable
{
    private const int ContextLines = 2;

    private readonly IAudioSource _audio;
    private readonly SileroVad _vad;
    private readonly ITranscriber _transcriber;
    private readonly Router _router;
    private readonly ILogger<HudPipeline> _log;
    private readonly UtteranceSegmenter _segmenter = new();
    // Not SingleReader: the mute setter drains it from the UI thread while the worker is the normal consumer.
    private readonly Channel<Utterance> _utterances = Channel.CreateUnbounded<Utterance>();
    // Two stages so a slow provider (LLM, AzDO retries) never delays the next transcription.
    private readonly Channel<(TranscriptLine Line, int Generation, bool Fresh)> _routing = Channel.CreateUnbounded<(TranscriptLine, int, bool)>();
    private readonly Channel<TranscriptLine> _transcript = Channel.CreateUnbounded<TranscriptLine>();
    private readonly Channel<Routed> _insights = Channel.CreateUnbounded<Routed>();
    private readonly Channel<string> _status = Channel.CreateUnbounded<string>();
    private readonly Channel<bool> _checking = Channel.CreateUnbounded<bool>();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _vadGate = new();
    private int _generation;
    private TranscriptLine? _last;

    public HudPipeline(IAudioSource audio, SileroVad vad, ITranscriber transcriber, Router router, ILogger<HudPipeline> log)
    {
        (_audio, _vad, _transcriber, _router, _log) = (audio, vad, transcriber, router, log);
        _segmenter.UtteranceReady += u => _utterances.Writer.TryWrite(u);
        _audio.FrameAvailable += OnFrame;
        _audio.Status += s => _status.Writer.TryWrite(s);
    }

    public ChannelReader<TranscriptLine> Transcript => _transcript.Reader;
    public ChannelReader<Routed> Insights => _insights.Reader;
    public ChannelReader<string> Status => _status.Reader;

    /// <summary>True while a sentence is with the providers (an LLM call can take a second), false when they are done.</summary>
    public ChannelReader<bool> Checking => _checking.Reader;

    public bool IsMuted
    {
        get => _audio.IsMuted;
        set
        {
            _audio.IsMuted = value;
            if (value)
            {
                Interlocked.Increment(ref _generation);
                lock (_vadGate)
                {
                    _segmenter.Flush();
                    _vad.Reset();
                }
                while (_utterances.Reader.TryRead(out _)) { }
                while (_routing.Reader.TryRead(out _)) { }
            }
            _status.Writer.TryWrite(value ? "Muted" : "Listening");
        }
    }

    public void Start()
    {
        _ = Task.Run(WorkAsync);
        _ = Task.Run(RouteAsync);
        _audio.Start();
    }

    /// <summary>Sends the last transcribed sentence to the providers again, bypassing their caches.</summary>
    public void Recheck()
    {
        if (Volatile.Read(ref _last) is { } line) _routing.Writer.TryWrite((line, Volatile.Read(ref _generation), true));
    }

    /// <summary>Latest frame loudness on a 0–1 scale (−60 dBFS → 0, 0 dBFS → 1); 0 while muted. For the mic meter.</summary>
    public float Level { get; private set; }

    public static float LevelOf(ReadOnlySpan<short> samples)
    {
        double sum = 0;
        foreach (var s in samples) sum += (double)s * s;
        var rms = Math.Sqrt(sum / Math.Max(1, samples.Length)) / short.MaxValue;
        return rms <= 0 ? 0 : (float)Math.Clamp((20 * Math.Log10(rms) + 60) / 60, 0, 1);
    }

    private void OnFrame(PcmFrame frame)
    {
        lock (_vadGate)
        {
            if (_audio.IsMuted)
            {
                Level = 0;
                return;
            }
            Level = LevelOf(frame.Samples.Span);
            _segmenter.Push(frame, _vad.Process(frame.Samples.Span));
        }
    }

    private async Task WorkAsync()
    {
        try
        {
            await foreach (var utterance in _utterances.Reader.ReadAllAsync(_cts.Token))
            {
                var generation = Volatile.Read(ref _generation);
                try
                {
                    var text = await _transcriber.TranscribeAsync(utterance.Samples, _cts.Token);
                    if (generation != Volatile.Read(ref _generation) || string.IsNullOrWhiteSpace(text)) continue;
                    var line = new TranscriptLine(utterance.Source, utterance.Start, text);
                    Volatile.Write(ref _last, line);
                    _transcript.Writer.TryWrite(line);
                    _routing.Writer.TryWrite((line, generation, false));
                }
                catch (Exception e) when (!_cts.IsCancellationRequested)
                {
                    // Includes HttpClient timeouts (TaskCanceledException): the worker must outlive one bad call.
                    _log.LogError(e, "Utterance at {At} failed", utterance.Start);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // disposed
        }
    }

    private async Task RouteAsync()
    {
        var recent = new List<string>();   // the sentences just before this one, handed to providers as context
        try
        {
            await foreach (var (line, generation, fresh) in _routing.Reader.ReadAllAsync(_cts.Token))
            {
                var busy = _router.Plan(line.Text) != Intent.None;
                if (busy) _checking.Writer.TryWrite(true);
                try
                {
                    var context = recent.Where(t => t != line.Text).ToArray();
                    var insights = await _router.RouteAsync(line.Text, context, fresh, _cts.Token);
                    if (generation == Volatile.Read(ref _generation) && insights.Count > 0) _insights.Writer.TryWrite(new Routed(line, insights));
                }
                catch (Exception e) when (!_cts.IsCancellationRequested)
                {
                    _log.LogError(e, "Routing \"{Text}\" failed", line.Text);
                }
                finally
                {
                    if (busy) _checking.Writer.TryWrite(false);
                }

                if (fresh) continue;
                recent.Add(line.Text);
                if (recent.Count > ContextLines) recent.RemoveAt(0);
            }
        }
        catch (OperationCanceledException)
        {
            // disposed
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _audio.FrameAvailable -= OnFrame;
        _audio.Stop();
        // Readers (the UI's drain loops) end instead of waiting forever on a pipeline that is gone.
        _transcript.Writer.TryComplete();
        _insights.Writer.TryComplete();
        _status.Writer.TryComplete();
        _checking.Writer.TryComplete();
        _cts.Dispose();
    }
}

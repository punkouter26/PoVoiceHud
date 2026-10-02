using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PoVoiceHud.Core.Asr;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Routing;
using PoVoiceHud.Core.Vad;

namespace PoVoiceHud.Core;

public sealed record TranscriptLine(SourceHint Source, TimeSpan At, string Text);

/// <summary>
/// Audio frames → VAD → utterances → Whisper → router, one utterance at a time. Muting bumps a generation
/// counter so anything already in flight is discarded when it completes.
/// </summary>
public sealed class HudPipeline : IDisposable
{
    private readonly IAudioSource _audio;
    private readonly SileroVad _vad;
    private readonly ITranscriber _transcriber;
    private readonly Router _router;
    private readonly ILogger<HudPipeline> _log;
    private readonly UtteranceSegmenter _segmenter = new();
    // Not SingleReader: the mute setter drains it from the UI thread while the worker is the normal consumer.
    private readonly Channel<Utterance> _utterances = Channel.CreateUnbounded<Utterance>();
    private readonly Channel<TranscriptLine> _transcript = Channel.CreateUnbounded<TranscriptLine>();
    private readonly Channel<IReadOnlyList<Insight>> _insights = Channel.CreateUnbounded<IReadOnlyList<Insight>>();
    private readonly Channel<string> _status = Channel.CreateUnbounded<string>();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _vadGate = new();
    private int _generation;

    public HudPipeline(IAudioSource audio, SileroVad vad, ITranscriber transcriber, Router router, ILogger<HudPipeline> log)
    {
        (_audio, _vad, _transcriber, _router, _log) = (audio, vad, transcriber, router, log);
        _segmenter.UtteranceReady += u => _utterances.Writer.TryWrite(u);
        _audio.FrameAvailable += OnFrame;
        _audio.Status += s => _status.Writer.TryWrite(s);
    }

    public ChannelReader<TranscriptLine> Transcript => _transcript.Reader;
    public ChannelReader<IReadOnlyList<Insight>> Insights => _insights.Reader;
    public ChannelReader<string> Status => _status.Reader;

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
            }
            _status.Writer.TryWrite(value ? "Muted" : "Listening");
        }
    }

    public void Start()
    {
        _ = Task.Run(WorkAsync);
        _audio.Start();
    }

    private void OnFrame(PcmFrame frame)
    {
        lock (_vadGate)
        {
            if (_audio.IsMuted) return;
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
                    _transcript.Writer.TryWrite(new TranscriptLine(utterance.Source, utterance.Start, text));

                    var insights = await _router.RouteAsync(text, _cts.Token);
                    if (generation == Volatile.Read(ref _generation) && insights.Count > 0) _insights.Writer.TryWrite(insights);
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

    public void Dispose()
    {
        _cts.Cancel();
        _audio.FrameAvailable -= OnFrame;
        _audio.Stop();
        _cts.Dispose();
    }
}

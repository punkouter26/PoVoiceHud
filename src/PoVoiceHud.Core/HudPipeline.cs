using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PoVoiceHud.Core.Asr;
using PoVoiceHud.Core.Audio;
using PoVoiceHud.Core.Providers;
using PoVoiceHud.Core.Providers.FactCheck;
using PoVoiceHud.Core.Vad;

namespace PoVoiceHud.Core;

public sealed record TranscriptLine(SourceHint Source, TimeSpan At, string Text);

/// <summary>The verdict on one transcript line.</summary>
public sealed record Checked(TranscriptLine Line, Insight Verdict);

/// <summary>
/// Audio frames → VAD → utterances → Whisper → fact checker, one utterance at a time. Muting bumps a generation
/// counter so anything already in flight is discarded when it completes.
/// </summary>
public sealed partial class HudPipeline : IDisposable
{
    private const int ContextLines = 2;

    private readonly IAudioSource _audio;
    private readonly SileroVad _vad;
    private readonly ITranscriber _transcriber;
    private readonly IFactChecker _checker;
    private readonly ILogger<HudPipeline> _log;
    private readonly UtteranceSegmenter _segmenter = new();
    // Not SingleReader: the mute setter drains it from the UI thread while the worker is the normal consumer.
    private readonly Channel<Utterance> _utterances = Channel.CreateUnbounded<Utterance>();
    // Two stages so a slow model call never delays the next transcription.
    private readonly Channel<(TranscriptLine Line, int Generation)> _checks = Channel.CreateUnbounded<(TranscriptLine, int)>();
    private readonly Channel<TranscriptLine> _transcript = Channel.CreateUnbounded<TranscriptLine>();
    private readonly Channel<Checked> _verdicts = Channel.CreateUnbounded<Checked>();
    private readonly Channel<string> _status = Channel.CreateUnbounded<string>();
    private readonly Channel<(TranscriptLine Line, bool Busy)> _checking = Channel.CreateUnbounded<(TranscriptLine, bool)>();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _vadGate = new();
    private int _generation;

    public HudPipeline(IAudioSource audio, SileroVad vad, ITranscriber transcriber, IFactChecker checker, ILogger<HudPipeline> log)
    {
        (_audio, _vad, _transcriber, _checker, _log) = (audio, vad, transcriber, checker, log);
        _segmenter.UtteranceReady += u => _utterances.Writer.TryWrite(u);
        _audio.FrameAvailable += OnFrame;
        _audio.Status += s => _status.Writer.TryWrite(s);
    }

    public ChannelReader<TranscriptLine> Transcript => _transcript.Reader;
    public ChannelReader<Checked> Verdicts => _verdicts.Reader;
    public ChannelReader<string> Status => _status.Reader;

    /// <summary>A line is queued for the fact checker (Busy) and done with it, with or without a verdict, or dropped by mute.</summary>
    public ChannelReader<(TranscriptLine Line, bool Busy)> Checking => _checking.Reader;

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
                while (_checks.Reader.TryRead(out var dropped)) _checking.Writer.TryWrite((dropped.Line, false));
            }
            _status.Writer.TryWrite(value ? "Muted" : "Listening");
        }
    }

    public void Start()
    {
        _ = Task.Run(WorkAsync);
        _ = Task.Run(CheckAsync);
        _audio.Start();
    }

    /// <summary>Latest frame loudness on a 0–1 scale (−60 dBFS → 0, 0 dBFS → 1); 0 while muted. For the mic meter.</summary>
    public float Level { get; private set; }

    /// <summary>Who the latest frame most likely came from, for colouring the meter.</summary>
    public SourceHint Speaker { get; private set; }

    public static float LevelOf(ReadOnlySpan<short> samples)
    {
        double sum = 0;
        foreach (var s in samples) sum += (double)s * s;
        var rms = Math.Sqrt(sum / Math.Max(1, samples.Length)) / short.MaxValue;
        return rms <= 0 ? 0 : (float)Math.Clamp((20 * Math.Log10(rms) + 60) / 60, 0, 1);
    }

    /// <summary>
    /// <paramref name="text"/> cut after each . ! or ? that is followed by a new sentence; initials ("U.S.") and titles
    /// ("Dr.") do not end one.
    /// </summary>
    public static IEnumerable<string> Sentences(string text) =>
        SentenceBreak().Split(text.Trim()).Select(s => s.Trim()).Where(s => s.Length > 0);

    [GeneratedRegex(@"(?<![A-Z]\.)(?<!\b(?:Mr|Mrs|Ms|Dr|St|Jr|Sr|vs|Prof)\.)(?<=[.!?…])\s+(?=[A-Z0-9""'“(])")]
    private static partial Regex SentenceBreak();

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
            Speaker = frame.Source;
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
                    // Whisper hands over everything said between two pauses; each sentence is its own line with its own verdict.
                    foreach (var sentence in Sentences(text))
                    {
                        var line = new TranscriptLine(utterance.Source, utterance.Start, sentence);
                        _transcript.Writer.TryWrite(line);
                        if (!FactChecker.IsCandidate(sentence)) continue;
                        _checking.Writer.TryWrite((line, true));   // shown as "checking…" from the moment it is queued
                        _checks.Writer.TryWrite((line, generation));
                    }
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

    private async Task CheckAsync()
    {
        var recent = new List<string>();   // the sentences just before this one, handed to the checker as context
        try
        {
            await foreach (var (line, generation) in _checks.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    var verdict = await _checker.CheckAsync(line.Text, recent.ToArray(), _cts.Token);
                    if (verdict is not null && generation == Volatile.Read(ref _generation)) _verdicts.Writer.TryWrite(new Checked(line, verdict));
                }
                catch (Exception e) when (!_cts.IsCancellationRequested)
                {
                    _log.LogError(e, "Checking \"{Text}\" failed", line.Text);
                }
                finally
                {
                    _checking.Writer.TryWrite((line, false));
                }

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
        _verdicts.Writer.TryComplete();
        _status.Writer.TryComplete();
        _checking.Writer.TryComplete();
        _cts.Dispose();
    }
}

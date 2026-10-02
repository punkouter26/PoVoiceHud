using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using PoVoiceHud.Core.Config;

namespace PoVoiceHud.Core.Audio;

/// <summary>
/// Default communications render (loopback) and/or capture (mic) devices per <see cref="AudioInput"/>,
/// re-bound when Windows changes them.
/// </summary>
public sealed class WasapiDualCapture(AudioInput input = AudioInput.Both) : IAudioSource, IMMNotificationClient
{
    private readonly object _gate = new();
    private readonly MMDeviceEnumerator _devices = new();
    private WasapiLoopbackCapture? _loop;
    private WasapiCapture? _mic;
    private MixGraph? _graph;
    private Timer? _timer;
    private bool _running;
    private bool _muted;

    public event Action<PcmFrame>? FrameAvailable;
    public event Action<string>? Status;

    /// <summary>Which endpoints an input selection opens: (loopback, mic).</summary>
    public static (bool Loopback, bool Mic) DevicesFor(AudioInput input) => input switch
    {
        AudioInput.Mic => (false, true),
        AudioInput.Remote => (true, false),
        _ => (true, true),
    };

    private bool _registered;

    private void EnsureRegistered()
    {
        if (_registered) return;
        _devices.RegisterEndpointNotificationCallback(this);
        _registered = true;
    }

    public bool IsMuted
    {
        get => _muted;
        set
        {
            lock (_gate)
            {
                _muted = value;
                if (value) Close();
                else if (_running) Open();
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            EnsureRegistered();
            _running = true;
            if (!_muted) Open();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            Close();
        }
    }

    private void Open()
    {
        Close();
        try
        {
            var (useLoopback, useMic) = DevicesFor(input);
            if (useLoopback) _loop = new WasapiLoopbackCapture(_devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications));
            if (useMic) _mic = new WasapiCapture(_devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications));
            var graph = new MixGraph(_loop?.WaveFormat, _mic?.WaveFormat);
            if (_loop is not null) _loop.DataAvailable += (_, e) => graph.AddLoopback(e.Buffer, e.BytesRecorded);
            if (_mic is not null) _mic.DataAvailable += (_, e) => graph.AddMic(e.Buffer, e.BytesRecorded);
            _graph = graph;
            _loop?.StartRecording();
            _mic?.StartRecording();
            _timer = new Timer(_ => Tick(), null, PcmFrame.Duration, PcmFrame.Duration);
            Status?.Invoke(input switch { AudioInput.Mic => "Listening (mic)", AudioInput.Remote => "Listening (remote)", _ => "Listening" });
        }
        catch (Exception e)
        {
            Close();
            Status?.Invoke($"No audio device: {e.Message}");
        }
    }

    private void Close()
    {
        _timer?.Dispose();
        _loop?.Dispose();
        _mic?.Dispose();
        (_timer, _loop, _mic, _graph) = (null, null, null, null);
    }

    private int _ticking;

    private void Tick()
    {
        // Timer callbacks can overlap when a tick (VAD + segmenter downstream) takes > 100 ms; MixGraph is single-threaded.
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
        try
        {
            MixGraph? graph;
            lock (_gate) graph = _graph;
            if (graph is null) return;
            // Drain extra frames when capture runs ahead of the timer so latency stays bounded.
            do FrameAvailable?.Invoke(graph.ReadFrame());
            while (graph.Buffered > PcmFrame.Duration * 2);
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (role != Role.Communications) return;
        Task.Delay(500).ContinueWith(_ =>
        {
            lock (_gate)
            {
                if (_running && !_muted) Open();
            }
            Status?.Invoke("Audio device changed");
        });
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { }
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        Stop();
        if (_registered) _devices.UnregisterEndpointNotificationCallback(this);
        _devices.Dispose();
    }
}

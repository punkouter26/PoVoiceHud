namespace PoVoiceHud.Core.Audio;

/// <summary>Fixed-size PCM16 ring addressed by absolute sample position.</summary>
public sealed class RingBuffer(int capacity)
{
    private readonly short[] _data = new short[capacity];

    public RingBuffer(TimeSpan length) : this((int)(length.TotalSeconds * PcmFrame.SampleRate)) { }

    public int Capacity => _data.Length;
    public long TotalWritten { get; private set; }

    public void Write(ReadOnlySpan<short> samples)
    {
        // Only the tail can survive; skipped samples still advance the absolute position.
        var skipped = Math.Max(0, samples.Length - Capacity);
        samples = samples[skipped..];
        var head = (int)((TotalWritten + skipped) % Capacity);
        var first = Math.Min(samples.Length, Capacity - head);
        samples[..first].CopyTo(_data.AsSpan(head));
        samples[first..].CopyTo(_data);
        TotalWritten += skipped + samples.Length;
    }

    public short[] CopyRange(long from, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(from, Math.Max(0, TotalWritten - Capacity));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(from + count, TotalWritten);
        var result = new short[count];
        var start = (int)(from % Capacity);
        var first = Math.Min(count, Capacity - start);
        _data.AsSpan(start, first).CopyTo(result);
        _data.AsSpan(0, count - first).CopyTo(result.AsSpan(first));
        return result;
    }

    public void Clear() => TotalWritten = 0;
}

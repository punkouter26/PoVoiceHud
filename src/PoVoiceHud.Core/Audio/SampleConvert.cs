namespace PoVoiceHud.Core.Audio;

public static class SampleConvert
{
    public static void ToPcm16(ReadOnlySpan<float> src, Span<short> dst)
    {
        for (var i = 0; i < src.Length; i++)
            dst[i] = (short)Math.Clamp(src[i] * short.MaxValue, short.MinValue, short.MaxValue);
    }

    public static float[] ToFloat(ReadOnlySpan<short> src)
    {
        var dst = new float[src.Length];
        for (var i = 0; i < src.Length; i++) dst[i] = src[i] / 32768f;
        return dst;
    }
}

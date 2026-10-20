using ComputeSharp;
using ComputeSharp.D2D1;

namespace PoVoiceHud.App.Fx;

// HLSL pixel shaders written in C#: ComputeSharp compiles them at build time and Win2D runs them on the GPU.
// They take no input image, work in physical pixels (scene position) and return premultiplied colour.
// A colour brighter than its alpha is deliberate: it adds light rather than covering what is underneath.

/// <summary>Noise shared by the shaders (ComputeSharp inlines it into each one).</summary>
internal static class Noise
{
    /// <summary>
    /// 0–1 from the integer part of <paramref name="p"/>. An integer hash (a PCG-style mix), not the usual
    /// frac(sin(x)·43758): the compiler may reorder the float maths, and that multiplier turns the rounding difference
    /// into a different value on each side of a cell edge, which shows as straight seams across the noise.
    /// </summary>
    public static float Hash(float2 p)
    {
        uint h = (uint)(int)p.X * 1597334677u ^ (uint)(int)p.Y * 3812015801u;
        h = h * 747796405u + 2891336453u;
        h = ((h >> (int)((h >> 28) + 4u)) ^ h) * 277803737u;
        h = (h >> 22) ^ h;
        return h * (1.0f / 4294967296.0f);
    }

    public static float2 Hash2(float2 p) => new(Hash(p), Hash(p + new float2(19.19f, 7.31f)));

    /// <summary>Smooth value noise, 0–1.</summary>
    public static float Value(float2 p)
    {
        float2 i = Hlsl.Floor(p);
        float2 f = Hlsl.Frac(p);
        float2 u = f * f * f * (f * (f * 6 - 15) + 10);   // quintic: no visible lattice where cells meet
        float a = Hash(i);
        float b = Hash(i + new float2(1, 0));
        float c = Hash(i + new float2(0, 1));
        float d = Hash(i + new float2(1, 1));
        return a + (b - a) * u.X + (c - a) * u.Y + (a - b - c + d) * u.X * u.Y;
    }

    /// <summary>Four octaves of value noise, 0–1.</summary>
    public static float Fbm(float2 p)
    {
        float sum = 0;
        float amplitude = 0.5f;
        for (int i = 0; i < 4; i++)
        {
            sum += amplitude * Value(p);
            // rotate each octave by about 37° so the lattices of successive octaves never line up
            p = new float2(0.8f * p.X - 0.6f * p.Y, 0.6f * p.X + 0.8f * p.Y) * 2.03f + new float2(1.7f, 9.2f);
            amplitude *= 0.5f;
        }
        return sum;
    }

    /// <summary>Signed distance to a rounded rectangle centred on the origin: negative inside.</summary>
    public static float RoundedBox(float2 p, float2 half, float radius)
    {
        float2 d = Hlsl.Abs(p) - (half - radius);
        return Hlsl.Length(Hlsl.Max(d, float2.Zero)) + Hlsl.Min(Hlsl.Max(d.X, d.Y), 0) - radius;
    }

    /// <summary>
    /// How far round the rounded rectangle's outline the nearest point to <paramref name="p"/> is, 0–1, clockwise from
    /// the top centre, measured by arc length so that something travelling along it keeps an even speed on a long, thin pill.
    /// </summary>
    public static float Around(float2 p, float2 half, float radius)
    {
        float a = half.X - radius;
        float b = half.Y - radius;
        float quarter = 1.5707963f * radius;
        float total = 4 * a + 4 * b + 4 * quarter;
        float s;
        if (Hlsl.Abs(p.X) <= a)
        {
            s = p.Y < 0 ? (p.X >= 0 ? p.X : total + p.X) : 3 * a + 2 * b + 2 * quarter - p.X;
        }
        else if (Hlsl.Abs(p.Y) <= b)
        {
            s = p.X > 0 ? a + quarter + p.Y + b : 3 * a + 2 * b + 3 * quarter + b - p.Y;
        }
        else
        {
            float cx = p.X > 0 ? a : -a;
            float cy = p.Y > 0 ? b : -b;
            float angle = Hlsl.Atan2(p.Y - cy, p.X - cx);
            if (p.X > 0 && p.Y < 0) s = a + (angle + 1.5707963f) * radius;
            else if (p.X > 0) s = a + quarter + 2 * b + angle * radius;
            else if (p.Y > 0) s = 3 * a + 2 * b + 2 * quarter + (angle - 1.5707963f) * radius;
            else s = 3 * a + 4 * b + 3 * quarter + (angle + 3.1415926f) * radius;
        }
        return s / total;
    }
}

/// <summary>
/// G1 + G2, behind the pill's content: a slow domain-warped aurora under the frosted glass that speeds up with
/// your voice and leans towards the latest verdict's colour, bent by the arrival ripple; and the voice ribbon
/// (three glowing strands whose height follows the level) in the slot where the meter bars used to be.
/// </summary>
[D2DInputCount(0)]
[D2DRequiresScenePosition]
[D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
[D2DGeneratedPixelShaderDescriptor]
internal readonly partial struct GlassShader(
    float time, float2 size, float scale, float intensity, float energy, float3 tint, float tintMix,
    float rippleAge, float2 rippleOrigin, float4 ribbon, float3 ribbonColor) : ID2D1PixelShader
{
    public float4 Execute()
    {
        float2 px = D2D.GetScenePosition().XY;
        float2 uv = px / size.Y;

        // the arrival ripple refracts the aurora: sample it displaced along the ring
        if (rippleAge >= 0)
        {
            float2 d = px - rippleOrigin;
            float dist = Hlsl.Max(Hlsl.Length(d), 1);
            float radius = rippleAge * 700 * scale;
            float band = Hlsl.Exp(-((dist - radius) * (dist - radius)) / (300 * scale * scale));
            uv += d / dist * band * Hlsl.Saturate(1 - rippleAge / 0.7f) * 0.35f;
        }

        float t = time * (0.06f + 0.3f * energy);
        float2 q = new(Noise.Fbm(uv * 1.3f + new float2(t, 0)), Noise.Fbm(uv * 1.3f + new float2(5.2f, 1.3f - t)));
        float n = Noise.Fbm(uv * 1.1f + q * 1.8f + new float2(t * 0.7f, -t * 0.4f));
        float3 azure = new(0.20f, 0.48f, 1.0f);
        float3 violet = new(0.64f, 0.32f, 0.98f);
        float3 teal = new(0.10f, 0.86f, 0.76f);
        float3 colour = azure + (violet - azure) * Hlsl.Saturate(n * 1.5f - 0.2f);
        colour += (teal - colour) * Hlsl.Saturate(q.X * q.Y * 2.2f - 0.2f);
        colour += (tint - colour) * tintMix;
        float alpha = (0.07f + 0.17f * n + 0.10f * energy) * intensity;
        float3 light = colour * alpha;

        // the ribbon: three strands, tapered at both ends, height following the voice
        float2 r = px - ribbon.XY;
        if (r.X >= 0 && r.X <= ribbon.Z && r.Y >= -4 * scale && r.Y <= ribbon.W + 4 * scale)
        {
            float x = r.X / ribbon.Z;
            float middle = ribbon.W * 0.5f;
            float taper = Hlsl.Sin(x * 3.1415926f);
            float glow = 0;
            for (int i = 0; i < 3; i++)
            {
                float k = i;
                float wobble = 0.6f + 0.4f * Hlsl.Sin(time * 1.3f + k * 2.0f);
                float y = middle + (0.06f + energy * 0.94f) * middle * taper * wobble
                          * Hlsl.Sin(x * (8 + k * 3.1f) + time * (5 + k * 1.7f) + k * 2.1f);
                float d = Hlsl.Abs(r.Y - y);
                glow += (Hlsl.Exp(-d * d / (1.2f * scale * scale)) + 0.3f * Hlsl.Exp(-d * d / (12 * scale * scale))) * (1 - k * 0.22f);
            }
            float strength = Hlsl.Saturate(glow) * (0.45f + 0.55f * Hlsl.Saturate(energy * 2));
            float3 hot = ribbonColor + (new float3(1, 1, 1) - ribbonColor) * Hlsl.Saturate(glow - 1) * 0.5f;
            light = light * (1 - strength) + hot * strength;
            alpha = alpha * (1 - strength) + strength;
        }
        return new float4(light, alpha);
    }
}

/// <summary>
/// G3 + G4, over the pill: the verdict shockwave (kind 0 supported: soft ring and a diagonal light sweep;
/// 1 contradicted: a ring split into red, green and blue with a scanline glitch; 2 unverified: an amber shimmer)
/// and the "thinking" comet, two heads with fading tails running round the pill's outline.
/// </summary>
[D2DInputCount(0)]
[D2DRequiresScenePosition]
[D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
[D2DGeneratedPixelShaderDescriptor]
internal readonly partial struct OverlayShader(
    float time, float2 size, float scale, float radius,
    float cometAlpha, float cometHead, float3 cometColour,
    float shockAge, float2 shockOrigin, float3 shockColour, float shockKind) : ID2D1PixelShader
{
    public float4 Execute()
    {
        float2 px = D2D.GetScenePosition().XY;
        float2 p = px - size * 0.5f;
        float2 half = size * 0.5f - 1.5f * scale;
        float edge = Noise.RoundedBox(p, half, radius);
        float3 light = float3.Zero;

        if (cometAlpha > 0)
        {
            float along = Noise.Around(p, half, radius);
            float core = Hlsl.Exp(-edge * edge / (1.1f * scale * scale));
            float halo = Hlsl.Exp(-edge * edge / (28 * scale * scale)) * 0.45f;
            for (int i = 0; i < 2; i++)
            {
                float behind = Hlsl.Frac(cometHead + i * 0.5f - along);
                float tail = Hlsl.Exp(-behind * 7) * (behind < 0.4f ? 1 : 0) * (1 - i * 0.45f);
                float head = Hlsl.Exp(-Hlsl.Min(behind, 1 - behind) * 140);
                light += (cometColour * (tail + head) + new float3(1, 1, 1) * head * 0.8f) * (core + halo);
            }
            light *= cometAlpha;
        }

        if (shockAge >= 0)
        {
            float2 d = px - shockOrigin;
            float dist = Hlsl.Length(d);
            float ring = shockAge * 900 * scale;
            float fade = Hlsl.Saturate(1 - shockAge / 0.6f);
            float width = 260 * scale * scale;
            if (shockKind < 0.5f)
            {
                float band = Hlsl.Exp(-(dist - ring) * (dist - ring) / width);
                float sweepX = (shockAge / 0.6f) * size.X * 1.4f - 0.2f * size.X;
                float sweepD = px.X - sweepX - (px.Y - size.Y * 0.5f) * 0.8f;
                float sweep = Hlsl.Exp(-sweepD * sweepD / (900 * scale * scale));
                light += (shockColour * band * 0.9f + (shockColour * 0.5f + new float3(0.5f, 0.5f, 0.5f)) * sweep * 0.55f) * fade;
            }
            else if (shockKind < 1.5f)
            {
                float split = 6 * scale;
                float red = Hlsl.Exp(-(dist - ring - split) * (dist - ring - split) / width);
                float green = Hlsl.Exp(-(dist - ring) * (dist - ring) / width);
                float blue = Hlsl.Exp(-(dist - ring + split) * (dist - ring + split) / width);
                light += new float3(red, green * 0.35f, blue) * fade * 1.1f;
                if (shockAge < 0.22f)
                {
                    // a few torn scanlines, re-rolled every frame
                    float row = Hlsl.Floor(px.Y / (3 * scale));
                    float roll = Noise.Hash(new float2(row, Hlsl.Floor(time * 60)));
                    float tear = roll > 0.82f ? 1 : 0;
                    float side = Noise.Hash(new float2(row * 3.1f, Hlsl.Floor(time * 60))) > 0.5f ? 1 : 0;
                    light += (side > 0.5f ? new float3(1, 0.15f, 0.2f) : new float3(0.1f, 0.9f, 1)) * tear * 0.45f * (1 - shockAge / 0.22f);
                }
            }
            else
            {
                float band = Hlsl.Exp(-(dist - ring) * (dist - ring) / width) * 0.6f;
                float lines = Hlsl.Pow(Hlsl.Saturate(Hlsl.Sin((px.X + px.Y * 1.5f) / (7 * scale) - shockAge * 18)), 12);
                light += shockColour * (band + lines * 0.35f) * fade;
            }
        }

        light *= edge < 0 ? 1 : Hlsl.Exp(-edge / scale);
        float alpha = Hlsl.Saturate(Hlsl.Max(light.X, Hlsl.Max(light.Y, light.Z))) * 0.85f;
        return new float4(light, alpha);
    }
}

/// <summary>
/// G9: frost growing in from the pill's rim, made of Voronoi crystals with bright boundaries and the odd glinting
/// cell. <c>progress</c> 0 → 1 moves the frost front inwards; the centre stays thin enough to read through.
/// </summary>
[D2DInputCount(0)]
[D2DRequiresScenePosition]
[D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
[D2DGeneratedPixelShaderDescriptor]
internal readonly partial struct FrostShader(float time, float2 size, float scale, float radius, float progress) : ID2D1PixelShader
{
    public float4 Execute()
    {
        float2 px = D2D.GetScenePosition().XY;
        float2 p = px - size * 0.5f;
        float edge = Noise.RoundedBox(p, size * 0.5f, radius);
        if (edge > 0) return float4.Zero;
        float depth = -edge;
        float deepest = size.Y * 0.5f;

        float2 g = px / (9 * scale);
        float2 cell = Hlsl.Floor(g);
        float2 f = Hlsl.Frac(g);
        float nearest = 8;
        float second = 8;
        float2 owner = float2.Zero;
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                float2 o = new(x, y);
                float d = Hlsl.Length(o + Noise.Hash2(cell + o) - f);
                if (d < nearest)
                {
                    second = nearest;
                    nearest = d;
                    owner = cell + o;
                }
                else if (d < second)
                {
                    second = d;
                }
            }
        }
        float crack = 1 - Hlsl.SmoothStep(0, 0.09f, second - nearest);

        float front = progress * deepest * 1.35f + (Noise.Value(px / (14 * scale)) - 0.5f) * 12 * scale;
        float cover = Hlsl.SmoothStep(front, front - 6 * scale, depth);
        float density = cover * (0.22f + 0.5f * Hlsl.Saturate(1 - depth / deepest));
        float glint = Noise.Hash(owner) > 0.94f ? Hlsl.Pow(Hlsl.Saturate(Hlsl.Sin(time * 3 + Noise.Hash(owner + 3) * 40)), 8) : 0;
        float3 ice = new float3(0.80f, 0.91f, 1.0f) + new float3(0.2f, 0.09f, 0) * crack;
        float alpha = Hlsl.Saturate(density * (0.6f + 0.6f * crack) + glint * cover * 0.6f);
        return new float4(ice * alpha, alpha);
    }
}

/// <summary>G10's sky: a deep gradient, a faint nebula in the session's dominant verdict colour and twinkling background stars.</summary>
[D2DInputCount(0)]
[D2DRequiresScenePosition]
[D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
[D2DGeneratedPixelShaderDescriptor]
internal readonly partial struct StarfieldShader(float time, float2 size, float scale, float radius, float3 tint) : ID2D1PixelShader
{
    public float4 Execute()
    {
        float2 px = D2D.GetScenePosition().XY;
        float edge = Noise.RoundedBox(px - size * 0.5f, size * 0.5f, radius);
        if (edge > 0) return float4.Zero;
        float2 uv = px / size.Y;
        float3 top = new(0.02f, 0.03f, 0.09f);
        float3 bottom = new(0.09f, 0.04f, 0.16f);
        float3 sky = top + (bottom - top) * (px.Y / size.Y);
        float nebula = Noise.Fbm(uv * 2.2f + new float2(time * 0.01f, 0));
        sky += tint * Hlsl.Pow(Hlsl.Saturate(nebula), 2.5f) * 0.55f;

        float2 grid = px / (11 * scale);
        float2 cell = Hlsl.Floor(grid);
        float2 star = Noise.Hash2(cell);
        float d = Hlsl.Length(Hlsl.Frac(grid) - star) * 11;
        float bright = Noise.Hash(cell + 5.3f);
        float twinkle = 0.55f + 0.45f * Hlsl.Sin(time * (1 + bright * 3) + bright * 50);
        sky += new float3(0.9f, 0.95f, 1) * (bright > 0.86f ? 1 : 0) * Hlsl.Exp(-d * d / 0.5f) * twinkle * bright;
        return new float4(sky * 0.94f, 0.94f);
    }
}

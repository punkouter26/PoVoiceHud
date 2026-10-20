using System.Numerics;

namespace PoVoiceHud.Core.Fx;

/// <summary>One spark. Colour is RGBA 0–1; the renderer fades it out over its life.</summary>
public record struct Particle(Vector2 Position, Vector2 Velocity, float Age, float Life, float Size, Vector4 Color);

/// <summary>
/// Sparks in window coordinates (DIPs, y down): launched in bursts, pulled down by gravity and slowed by drag,
/// or — once a target is set — drawn into it like dust settling on a link.
/// </summary>
public sealed class ParticleSystem(int seed = 1)
{
    private const float Gravity = 520;
    private const float Drag = 2.2f;
    private const float Pull = 60;      // spring stiffness towards the target, per second²
    private const float PullDamping = 11;

    private readonly List<Particle> _particles = [];
    private readonly Random _random = new(seed);

    /// <summary>Where sparks gather after the first third of their life; null lets them fall.</summary>
    public Vector2? Target { get; set; }

    public IReadOnlyList<Particle> Particles => _particles;
    public bool IsAlive => _particles.Count > 0;

    /// <param name="direction">Mean launch angle in radians (0 = right, −π/2 = up).</param>
    /// <param name="spread">Launch angles fall within ± this of <paramref name="direction"/>.</param>
    public void Burst(Vector2 at, int count, float speed, float direction, float spread, float life, IReadOnlyList<Vector4> palette)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = direction + (float)(_random.NextDouble() * 2 - 1) * spread;
            var velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed * (0.35f + (float)_random.NextDouble() * 0.65f);
            var span = life * (0.6f + (float)_random.NextDouble() * 0.4f);
            _particles.Add(new Particle(at, velocity, 0, span, 1.2f + (float)_random.NextDouble() * 2.3f, palette[_random.Next(palette.Count)]));
        }
    }

    public void Step(float dt)
    {
        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _particles.RemoveAt(i);
                continue;
            }
            var acceleration = Target is { } target && p.Age > p.Life / 3
                ? (target - p.Position) * Pull - p.Velocity * PullDamping
                : new Vector2(0, Gravity) - p.Velocity * Drag;
            p.Velocity += acceleration * dt;
            p.Position += p.Velocity * dt;
            _particles[i] = p;
        }
    }

    public void Clear() => _particles.Clear();
}

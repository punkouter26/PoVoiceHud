using System.Numerics;

namespace PoVoiceHud.Core.Fx;

/// <summary>A triangle of the broken pane: its corners at rest, and where the throw has taken it since.</summary>
public record struct Shard(Vector2 A, Vector2 B, Vector2 C, Vector2 Centre, Vector2 Offset, Vector2 Velocity, float Angle, float Spin);

/// <summary>
/// Breaks a width × height pane into triangles over a jittered grid and throws them away from an impact point,
/// spinning, under gravity. Edge points stay on the edge, so at rest the triangles tile the pane exactly.
/// </summary>
public sealed class ShardField
{
    public const float Life = 0.9f;
    private const float Gravity = 1400;

    private readonly Shard[] _shards;

    public ShardField(float width, float height, Vector2 impact, int columns = 16, int rows = 2, int seed = 7)
    {
        var random = new Random(seed);
        float Jitter(float span) => (float)(random.NextDouble() * 2 - 1) * span * 0.35f;
        var (cell, row) = (width / columns, height / rows);
        var points = new Vector2[columns + 1, rows + 1];
        for (var x = 0; x <= columns; x++)
            for (var y = 0; y <= rows; y++)
                points[x, y] = new Vector2(
                    x * cell + (x is 0 || x == columns ? 0 : Jitter(cell)),
                    y * row + (y is 0 || y == rows ? 0 : Jitter(row)));

        var reach = MathF.Max(width, height);
        var shards = new List<Shard>(columns * rows * 2);
        for (var x = 0; x < columns; x++)
            for (var y = 0; y < rows; y++)
            {
                var (p00, p10, p01, p11) = (points[x, y], points[x + 1, y], points[x, y + 1], points[x + 1, y + 1]);
                // alternate the diagonal, so the cracks do not all lean one way
                Vector2[][] halves = (x + y) % 2 == 0 ? [[p00, p10, p11], [p00, p11, p01]] : [[p00, p10, p01], [p10, p11, p01]];
                foreach (var t in halves)
                {
                    var centre = (t[0] + t[1] + t[2]) / 3;
                    var away = centre - impact;
                    var push = away.LengthSquared() < 1e-3f ? new Vector2(0, -1) : Vector2.Normalize(away);
                    var speed = (420 - 220 * MathF.Min(1, away.Length() / reach)) * (0.7f + (float)random.NextDouble() * 0.6f);
                    var velocity = push * speed + new Vector2(0, -160);   // a little lift, so the pieces arc before they fall
                    var spin = (float)(random.NextDouble() * 2 - 1) * 9;
                    shards.Add(new Shard(t[0], t[1], t[2], centre, Vector2.Zero, velocity, 0, spin));
                }
            }
        _shards = [.. shards];
    }

    public ReadOnlySpan<Shard> Shards => _shards;
    public float Age { get; private set; }
    public bool IsDone => Age >= Life;

    public void Step(float dt)
    {
        Age += dt;
        for (var i = 0; i < _shards.Length; i++)
        {
            ref var s = ref _shards[i];
            s.Velocity += new Vector2(0, Gravity) * dt;
            s.Offset += s.Velocity * dt;
            s.Angle += s.Spin * dt;
        }
    }
}

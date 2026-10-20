using System.Numerics;

namespace PoVoiceHud.Core.Fx;

/// <summary>
/// A force-directed layout of the session's cards in a −1…1 square: time runs left to right, every star pushes
/// the others away, and cards about the same thing (sharing key words) are tied together by springs, so related
/// claims cluster. Stepping it animates the stars settling; <see cref="Settle"/> jumps to the end.
/// </summary>
public sealed class Constellation
{
    private const float Repulsion = 0.012f;
    private const float LinkStiffness = 6;
    private const float LinkLength = 0.22f;
    private const float TimeStiffness = 1.6f;
    private const float Damping = 4.5f;

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "again", "being", "could", "every", "first", "their", "there", "these", "those", "under", "until", "where", "which", "while", "would",
    };

    private readonly Vector2[] _positions;
    private readonly Vector2[] _velocities;
    private readonly float[] _timeX;

    /// <param name="titles">Each card's title, oldest first.</param>
    /// <param name="start">Where the first stars already are (an earlier layout), so a new card does not rearrange the sky.</param>
    public Constellation(IReadOnlyList<string> titles, ReadOnlySpan<Vector2> start = default, int seed = 3)
    {
        var random = new Random(seed);
        var n = titles.Count;
        _positions = new Vector2[n];
        _velocities = new Vector2[n];
        _timeX = new float[n];
        for (var i = 0; i < n; i++)
        {
            _timeX[i] = n == 1 ? 0 : -0.85f + 1.7f * i / (n - 1);
            _positions[i] = i < start.Length ? start[i] : new Vector2(_timeX[i], (float)(random.NextDouble() * 2 - 1) * 0.5f);
        }

        var words = titles.Select(Words).ToArray();
        var links = new List<(int, int)>();
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
                if (Similarity(words[i], words[j]) >= 0.2) links.Add((i, j));
        Links = links;
    }

    public ReadOnlySpan<Vector2> Positions => _positions;
    public IReadOnlyList<(int A, int B)> Links { get; }

    /// <summary>Kinetic energy left; below about 1e-4 nothing visibly moves.</summary>
    public float Energy { get; private set; } = float.MaxValue;

    public void Step(float dt)
    {
        dt = MathF.Min(dt, 1 / 30f);
        var n = _positions.Length;
        var forces = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var d = _positions[i] - _positions[j];
                var push = d / MathF.Pow(MathF.Max(d.LengthSquared(), 0.0025f), 1.5f) * Repulsion;
                forces[i] += push;
                forces[j] -= push;
            }
            forces[i] += new Vector2((_timeX[i] - _positions[i].X) * TimeStiffness, -_positions[i].Y * 0.8f);
        }
        foreach (var (a, b) in Links)
        {
            var d = _positions[b] - _positions[a];
            var length = MathF.Max(d.Length(), 1e-4f);
            var pull = d / length * (length - LinkLength) * LinkStiffness;
            forces[a] += pull;
            forces[b] -= pull;
        }
        var energy = 0f;
        for (var i = 0; i < n; i++)
        {
            _velocities[i] += (forces[i] - _velocities[i] * Damping) * dt;
            _positions[i] = Vector2.Clamp(_positions[i] + _velocities[i] * dt, new Vector2(-1), new Vector2(1));
            energy += _velocities[i].LengthSquared();
        }
        Energy = energy;
    }

    /// <summary>Runs the layout to rest at once, for when nothing should move on screen.</summary>
    public void Settle()
    {
        for (var i = 0; i < 600 && Energy > 1e-5f; i++) Step(1 / 60f);
    }

    private static HashSet<string> Words(string title) => title
        .Split([' ', ',', '.', ';', ':', '(', ')', '"', '\'', '-', '?', '!'], StringSplitOptions.RemoveEmptyEntries)
        .Where(w => w.Length >= 4 && !Common.Contains(w))
        .Select(w => w.ToLowerInvariant())
        .ToHashSet();

    private static double Similarity(HashSet<string> a, HashSet<string> b) =>
        a.Count == 0 || b.Count == 0 ? 0 : (double)a.Count(b.Contains) / a.Union(b).Count();
}

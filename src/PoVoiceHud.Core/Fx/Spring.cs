namespace PoVoiceHud.Core.Fx;

/// <summary>
/// A damped spring stepped by hand (semi-implicit Euler), for motion the compositor cannot run itself: the
/// window's height. A damping ratio of 1 arrives without overshoot; below 1 it overshoots and settles back.
/// </summary>
public sealed class Spring(double stiffness = 420, double dampingRatio = 1)
{
    public double Value { get; set; }
    public double Velocity { get; set; }
    public double Target { get; set; }
    public double DampingRatio { get; set; } = dampingRatio;

    public bool IsSettled => Math.Abs(Target - Value) < 0.5 && Math.Abs(Velocity) < 4;

    /// <summary>Advances by <paramref name="seconds"/> (split into small steps, so a late tick cannot blow up); true once settled.</summary>
    public bool Step(double seconds)
    {
        for (var left = Math.Min(seconds, 0.1); left > 0; left -= 1 / 240d)
        {
            var dt = Math.Min(left, 1 / 240d);
            var damping = 2 * DampingRatio * Math.Sqrt(stiffness);
            Velocity += (stiffness * (Target - Value) - damping * Velocity) * dt;
            Value += Velocity * dt;
        }
        if (!IsSettled) return false;
        (Value, Velocity) = (Target, 0);
        return true;
    }
}

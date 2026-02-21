namespace CelestialMechanics.Simulation.Systems;

/// <summary>
/// Manages global cosmological scale factor for Big Bang expansion simulation.
///
/// The scale factor a(t) grows via a simple Hubble-like law:
///   da/dt = H0 * a
///
/// Positions are scaled relative to the origin:
///   r_physical = a(t) * r_comoving
///
/// Expansion is optional and controlled by <see cref="ExpansionEnabled"/>.
/// When disabled, ScaleFactor remains 1.0 and no position modification occurs.
/// </summary>
public sealed class SpaceMetricManager
{
    /// <summary>Scale factor a(t). Starts at 1.0.</summary>
    public double ScaleFactor { get; private set; } = 1.0;

    /// <summary>Previous scale factor (for computing velocity corrections).</summary>
    public double PreviousScaleFactor { get; private set; } = 1.0;

    /// <summary>Hubble parameter H0 in simulation units (1/time).</summary>
    public double HubbleParameter { get; set; } = 0.001;

    /// <summary>Whether cosmological expansion is active.</summary>
    public bool ExpansionEnabled { get; set; }

    /// <summary>
    /// Advance the scale factor by one timestep.
    /// Uses exact exponential solution: a(t+dt) = a(t) * exp(H0 * dt).
    /// </summary>
    public void Update(double dt)
    {
        if (!ExpansionEnabled) return;

        PreviousScaleFactor = ScaleFactor;
        ScaleFactor *= System.Math.Exp(HubbleParameter * dt);
    }

    /// <summary>
    /// Compute the ratio of scale factors for position rescaling.
    /// Returns 1.0 when expansion is disabled.
    /// </summary>
    public double GetScaleRatio()
    {
        if (!ExpansionEnabled || PreviousScaleFactor <= 0.0)
            return 1.0;

        return ScaleFactor / PreviousScaleFactor;
    }

    public void Reset()
    {
        ScaleFactor = 1.0;
        PreviousScaleFactor = 1.0;
    }
}

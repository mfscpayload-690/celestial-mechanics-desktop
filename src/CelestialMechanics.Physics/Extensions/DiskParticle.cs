namespace CelestialMechanics.Physics.Extensions;

/// <summary>
/// Lightweight value type representing a single particle in an accretion disk.
///
/// Accretion disk particles orbit a compact object, drifting inward due to
/// viscous dissipation. Their temperature increases as they fall deeper into
/// the gravitational well, following a radial gradient from cool (outer)
/// to hot (inner), which determines their emission color.
///
/// No allocations — stored in contiguous arrays for cache efficiency.
/// </summary>
public struct DiskParticle
{
    /// <summary>Position X in simulation units (AU).</summary>
    public double PosX;
    /// <summary>Position Y in simulation units (AU).</summary>
    public double PosY;
    /// <summary>Position Z in simulation units (AU).</summary>
    public double PosZ;

    /// <summary>Velocity X in simulation units.</summary>
    public double VelX;
    /// <summary>Velocity Y in simulation units.</summary>
    public double VelY;
    /// <summary>Velocity Z in simulation units.</summary>
    public double VelZ;

    /// <summary>
    /// Temperature in Kelvin. Determines emission colour via blackbody mapping.
    /// Inner disk: 10⁶–10⁷ K (X-ray), outer disk: 10³–10⁴ K (infrared/visible).
    /// </summary>
    public double Temperature;

    /// <summary>Age of the particle in simulation time units. Used for fade-out.</summary>
    public double Age;

    /// <summary>Maximum lifetime before the particle is recycled.</summary>
    public double MaxAge;

    /// <summary>Index of the compact body this particle orbits.</summary>
    public int ParentBodyIndex;

    /// <summary>Whether this particle slot is in use.</summary>
    public bool IsActive;

    /// <summary>
    /// Current orbital radius from parent body. Precomputed each update
    /// to avoid redundant sqrt calls.
    /// </summary>
    public double OrbitalRadius;
}

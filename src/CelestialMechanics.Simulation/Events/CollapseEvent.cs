using CelestialMechanics.Math;

namespace CelestialMechanics.Simulation.Events;

/// <summary>
/// Data record capturing gravitational collapse event details.
/// Created when a stellar remnant or massive core collapses to form
/// a compact object (neutron star or black hole).
/// </summary>
public sealed class CollapseEvent
{
    /// <summary>Entity ID of the collapsing object.</summary>
    public Guid EntityId { get; init; }

    /// <summary>Position at time of collapse.</summary>
    public Vec3d Position { get; init; }

    /// <summary>Mass of the collapsing core (M☉).</summary>
    public double CoreMass { get; init; }

    /// <summary>Remnant type: "NeutronStar" or "BlackHole".</summary>
    public string RemnantType { get; init; } = string.Empty;

    /// <summary>Schwarzschild radius if black hole formed (sim units).</summary>
    public double SchwarzschildRadius { get; init; }

    /// <summary>Simulation time when the collapse occurred.</summary>
    public double Time { get; init; }
}

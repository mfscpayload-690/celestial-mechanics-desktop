using CelestialMechanics.Math;

namespace CelestialMechanics.Simulation.Events;

/// <summary>
/// Data record capturing compact object merger event details.
/// Created by MergerResolutionSystem when two compact objects coalesce.
/// </summary>
public sealed class MergerEvent
{
    /// <summary>Entity ID of the first merging object.</summary>
    public Guid EntityAId { get; init; }

    /// <summary>Entity ID of the second merging object.</summary>
    public Guid EntityBId { get; init; }

    /// <summary>Mass of entity A (M☉).</summary>
    public double MassA { get; init; }

    /// <summary>Mass of entity B (M☉).</summary>
    public double MassB { get; init; }

    /// <summary>Combined mass of the remnant (M☉).</summary>
    public double RemnantMass { get; init; }

    /// <summary>Velocity of the merged remnant.</summary>
    public Vec3d RemnantVelocity { get; init; }

    /// <summary>Position where the merger occurred.</summary>
    public Vec3d Position { get; init; }

    /// <summary>Gravitational wave burst amplitude.</summary>
    public double GravitationalWaveAmplitude { get; init; }

    /// <summary>Separation at time of merger (sim units).</summary>
    public double Separation { get; init; }

    /// <summary>Simulation time when the merger occurred.</summary>
    public double Time { get; init; }

    /// <summary>Whether the remnant is a black hole.</summary>
    public bool FormedBlackHole { get; init; }
}

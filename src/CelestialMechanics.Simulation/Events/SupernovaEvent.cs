using CelestialMechanics.Math;

namespace CelestialMechanics.Simulation.Events;

/// <summary>
/// Data record capturing supernova event details.
/// Created by CatastrophicEventSystem when a star undergoes core collapse.
/// </summary>
public sealed class SupernovaEvent
{
    /// <summary>Entity ID of the progenitor star.</summary>
    public Guid ProgenitorId { get; init; }

    /// <summary>Position at time of explosion.</summary>
    public Vec3d Position { get; init; }

    /// <summary>Original mass of the progenitor star (M☉).</summary>
    public double ProgenitorMass { get; init; }

    /// <summary>Core mass at time of collapse (M☉).</summary>
    public double CoreMass { get; init; }

    /// <summary>Mass ejected as ejecta shell (M☉).</summary>
    public double EjectaMass { get; init; }

    /// <summary>Remnant mass after collapse (M☉).</summary>
    public double RemnantMass { get; init; }

    /// <summary>Total kinetic energy of ejecta in sim units.</summary>
    public double ExplosionEnergy { get; init; }

    /// <summary>Number of ejecta particles spawned.</summary>
    public int EjectaCount { get; init; }

    /// <summary>Simulation time when the supernova occurred.</summary>
    public double Time { get; init; }

    /// <summary>Whether the remnant is a black hole (vs neutron star).</summary>
    public bool FormedBlackHole { get; init; }
}

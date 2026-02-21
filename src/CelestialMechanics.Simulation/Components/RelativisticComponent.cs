using CelestialMechanics.Math;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.Components;

/// <summary>
/// Component for entities requiring relativistic treatment.
/// Tracks Schwarzschild radius and flags for post-Newtonian corrections,
/// gravitational lensing, and accretion disk effects.
/// </summary>
public sealed class RelativisticComponent : IComponent
{
    /// <summary>Schwarzschild radius: rs = 2GM/c² in simulation units.</summary>
    public double SchwarzschildRadius { get; set; }

    /// <summary>Whether this entity should receive post-Newtonian corrections.</summary>
    public bool EnablePostNewtonian { get; set; } = true;

    /// <summary>Whether gravitational lensing applies to this entity.</summary>
    public bool EnableLensing { get; set; }

    /// <summary>Whether accretion disk effects are active for this entity.</summary>
    public bool EnableAccretion { get; set; }

    /// <summary>
    /// Recompute Schwarzschild radius from current mass.
    /// rs = 2 * G_Sim * M / c²_Sim
    /// </summary>
    public void ComputeSchwarzschildRadius(double mass)
    {
        SchwarzschildRadius = PhysicalConstants.SchwarzschildFactorSim * mass;
    }

    public void Update(double dt)
    {
        // Relativistic corrections are applied by the physics backend (PostNewtonianBackend).
        // This component is state-only.
    }
}

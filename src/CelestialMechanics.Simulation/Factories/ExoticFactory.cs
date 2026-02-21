using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.Factories;

/// <summary>
/// Factory for creating exotic objects: black holes, wormholes, singularities.
/// Uses physical constants from PhysicalConstants for Schwarzschild radii.
/// </summary>
public static class ExoticFactory
{
    /// <summary>
    /// Create a black hole entity.
    /// Assigns PhysicsComponent, RelativisticComponent, and computes Schwarzschild radius:
    ///   rs = 2GM/c² (in simulation units: G_Sim=1, so rs = 2M/c²_Sim)
    /// </summary>
    public static Entity CreateBlackHole(double mass, Vec3d position, Vec3d velocity,
        bool enableAccretion = false)
    {
        var entity = new Entity();
        entity.Tag = "BlackHole";

        // Schwarzschild radius in simulation units
        double rs = PhysicalConstants.SchwarzschildFactorSim * mass;
        // Use a visual minimum so it appears at rendering scale
        double visualRadius = System.Math.Max(rs, 0.01 * System.Math.Cbrt(mass));

        var pc = new PhysicsComponent
        {
            Mass = mass,
            Position = position,
            Velocity = velocity,
            Density = 1.0, // BH density is nominal
            Radius = visualRadius,
            IsCollidable = true
        };
        entity.AddComponent(pc);

        var rel = new RelativisticComponent
        {
            SchwarzschildRadius = rs,
            EnablePostNewtonian = true,
            EnableLensing = true,
            EnableAccretion = enableAccretion
        };
        entity.AddComponent(rel);

        return entity;
    }

    /// <summary>
    /// Create a cosmological singularity (Big Bang seed).
    /// Has ExpansionComponent with IsSingularity=true.
    /// </summary>
    public static Entity CreateSingularity(Vec3d position, double hubbleParameter = 0.001)
    {
        var entity = new Entity();
        entity.Tag = "Singularity";

        var pc = new PhysicsComponent
        {
            Mass = 1e6, // Very high mass density
            Position = position,
            Velocity = Vec3d.Zero,
            Density = 1e15,
            Radius = 0.001,
            IsCollidable = false
        };
        entity.AddComponent(pc);

        var expansion = new ExpansionComponent
        {
            IsSingularity = true,
            HasExpanded = false,
            HubbleParameter = hubbleParameter
        };
        entity.AddComponent(expansion);

        return entity;
    }

    /// <summary>
    /// Create a supermassive black hole (typically at galaxy centres).
    /// </summary>
    public static Entity CreateSuperMassiveBlackHole(double mass, Vec3d position,
        bool enableAccretion = true)
    {
        var entity = CreateBlackHole(mass, position, Vec3d.Zero, enableAccretion);
        entity.Tag = "SMBH";
        return entity;
    }
}

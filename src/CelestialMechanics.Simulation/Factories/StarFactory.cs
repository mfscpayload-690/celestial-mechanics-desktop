using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.Factories;

/// <summary>
/// Factory for creating star entities with PhysicsComponent and StellarEvolutionComponent.
/// All masses in solar masses, distances in AU.
/// </summary>
public static class StarFactory
{
    /// <summary>Create a Sun-like star (1 M☉).</summary>
    public static Entity CreateSunLike(Vec3d position, Vec3d velocity)
    {
        return CreateStar(1.0, position, velocity, burnRate: 0.0001, tag: "Star_SunLike");
    }

    /// <summary>
    /// Create a massive star (8–50 M☉) that will eventually undergo supernova.
    /// Pre-configured with high burn rate to reach Chandrasekhar limit.
    /// </summary>
    public static Entity CreateMassiveStar(Vec3d position, Vec3d velocity, double mass = 20.0)
    {
        // Massive stars burn fuel much faster: L ∝ M^3.5 → lifetime ∝ M^(-2.5)
        double burnRate = 0.01 * System.Math.Pow(mass, 1.5);
        return CreateStar(mass, position, velocity, burnRate, tag: "Star_Massive");
    }

    /// <summary>Convenience overload placing the star at origin with zero velocity.</summary>
    public static Entity CreateMassiveStar(double mass = 20.0)
    {
        return CreateMassiveStar(Vec3d.Zero, Vec3d.Zero, mass);
    }

    /// <summary>Create a neutron star with relativistic component.</summary>
    public static Entity CreateNeutronStar(Vec3d position, Vec3d velocity, double mass = 1.4)
    {
        var entity = new Entity();
        entity.Tag = "Star_Neutron";

        var pc = new PhysicsComponent
        {
            Mass = mass,
            Position = position,
            Velocity = velocity,
            Density = DensityModel.NeutronStarDensity,
            Radius = DensityModel.ComputeRadius(mass, DensityModel.NeutronStarDensity),
            IsCollidable = true
        };
        entity.AddComponent(pc);

        var rel = new RelativisticComponent();
        rel.ComputeSchwarzschildRadius(mass);
        rel.EnablePostNewtonian = true;
        entity.AddComponent(rel);

        return entity;
    }

    /// <summary>Create a generic star with full stellar evolution tracking.</summary>
    public static Entity CreateStar(double mass, Vec3d position, Vec3d velocity,
        double burnRate = 0.001, string tag = "Star")
    {
        var entity = new Entity();
        entity.Tag = tag;

        double density = DensityModel.StarDensity;
        var pc = new PhysicsComponent
        {
            Mass = mass,
            Position = position,
            Velocity = velocity,
            Density = density,
            Radius = DensityModel.ComputeRadius(mass, density),
            IsCollidable = true
        };
        entity.AddComponent(pc);

        // Stellar evolution: initial core is 10% of total mass, rest is fuel
        var stellar = new StellarEvolutionComponent
        {
            CoreMass = mass * 0.1,
            FuelMass = mass * 0.9,
            BurnRate = burnRate
        };
        entity.AddComponent(stellar);

        // Explosion component for supernova readiness
        entity.AddComponent(new ExplosionComponent());

        return entity;
    }
}

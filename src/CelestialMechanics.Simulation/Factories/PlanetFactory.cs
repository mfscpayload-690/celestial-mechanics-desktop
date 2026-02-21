using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.Factories;

/// <summary>
/// Factory for creating planet entities with pre-configured PhysicsComponents.
/// All masses in solar masses, distances in AU, velocities in AU/TimeUnit.
/// </summary>
public static class PlanetFactory
{
    /// <summary>Create an Earth-like rocky planet.</summary>
    public static Entity CreateEarthLike(Vec3d position, Vec3d velocity)
    {
        const double earthMass = 3.003e-6; // M☉
        const double earthDensity = DensityModel.RockyPlanetDensity;

        var entity = new Entity();
        entity.Tag = "Planet_EarthLike";
        var pc = new PhysicsComponent
        {
            Mass = earthMass,
            Position = position,
            Velocity = velocity,
            Density = earthDensity,
            Radius = DensityModel.ComputeRadius(earthMass, earthDensity),
            IsCollidable = true
        };
        entity.AddComponent(pc);
        return entity;
    }

    /// <summary>Create a gas giant (Jupiter-like).</summary>
    public static Entity CreateGasGiant(Vec3d position, Vec3d velocity)
    {
        const double jupiterMass = 9.546e-4; // M☉
        const double density = DensityModel.GasGiantDensity;

        var entity = new Entity();
        entity.Tag = "Planet_GasGiant";
        var pc = new PhysicsComponent
        {
            Mass = jupiterMass,
            Position = position,
            Velocity = velocity,
            Density = density,
            Radius = DensityModel.ComputeRadius(jupiterMass, density),
            IsCollidable = true
        };
        entity.AddComponent(pc);
        return entity;
    }

    /// <summary>Create a planet with custom parameters.</summary>
    public static Entity CreateCustomPlanet(double mass, Vec3d position, Vec3d velocity, double density = DensityModel.RockyPlanetDensity)
    {
        var entity = new Entity();
        entity.Tag = "Planet_Custom";
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
        return entity;
    }
}

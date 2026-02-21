using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;
using CelestialMechanics.Simulation.Components;
using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.Factories;

/// <summary>
/// Factory for composite structures: binary systems, solar systems, star clusters.
/// Builds structures from child entities with correct orbital mechanics.
/// </summary>
public static class StructureFactory
{
    /// <summary>
    /// Create a binary star system with two stars in mutual circular orbit.
    /// Uses vis-viva equation: v = sqrt(G*M_total / (4*a)) for equal mass binaries.
    /// </summary>
    public static Entity[] CreateBinarySystem(
        double mass1, double mass2,
        double separation,
        Vec3d centrePosition)
    {
        double totalMass = mass1 + mass2;

        // Place bodies at ±(separation * m_other/m_total) from centre
        double r1 = separation * mass2 / totalMass;
        double r2 = separation * mass1 / totalMass;

        // Circular orbital velocity: v = sqrt(G * M_total / separation) * (r/separation)
        double vOrbital = System.Math.Sqrt(PhysicalConstants.G_Sim * totalMass / separation);

        Vec3d pos1 = centrePosition + new Vec3d(r1, 0, 0);
        Vec3d pos2 = centrePosition + new Vec3d(-r2, 0, 0);

        // Perpendicular velocities for circular orbit
        double v1 = vOrbital * r1 / separation;
        double v2 = vOrbital * r2 / separation;

        Vec3d vel1 = new Vec3d(0, v1, 0);
        Vec3d vel2 = new Vec3d(0, -v2, 0);

        var star1 = StarFactory.CreateStar(mass1, pos1, vel1, tag: "Binary_A");
        var star2 = StarFactory.CreateStar(mass2, pos2, vel2, tag: "Binary_B");

        return new[] { star1, star2 };
    }

    /// <summary>
    /// Create a simple solar system: one star + N planets in circular orbits.
    /// </summary>
    public static Entity[] CreateSolarSystem(
        double starMass,
        Vec3d centrePosition,
        int planetCount = 4,
        double innerRadius = 0.5,
        double outerRadius = 5.0)
    {
        var entities = new List<Entity>();

        // Central star
        var star = StarFactory.CreateStar(starMass, centrePosition, Vec3d.Zero, tag: "PrimaryStar");
        entities.Add(star);

        // Planets in circular orbits
        for (int i = 0; i < planetCount; i++)
        {
            double t = (double)i / System.Math.Max(1, planetCount - 1);
            double radius = innerRadius + t * (outerRadius - innerRadius);
            double angle = 2.0 * System.Math.PI * i / planetCount;

            Vec3d pos = centrePosition + new Vec3d(
                radius * System.Math.Cos(angle),
                radius * System.Math.Sin(angle),
                0);

            // Circular orbital velocity: v = sqrt(G*M/r)
            double vMag = System.Math.Sqrt(PhysicalConstants.G_Sim * starMass / radius);
            Vec3d vel = new Vec3d(
                -vMag * System.Math.Sin(angle),
                vMag * System.Math.Cos(angle),
                0);

            double planetMass = 1e-5 * (0.5 + t); // Increasing mass outward

            var planet = PlanetFactory.CreateCustomPlanet(planetMass, pos, vel);
            entities.Add(planet);
        }

        return entities.ToArray();
    }

    /// <summary>
    /// Create a star cluster (Plummer sphere initial conditions).
    /// </summary>
    public static Entity[] CreateStarCluster(
        int count,
        Vec3d centrePosition,
        double totalMass = 100.0,
        double plummerRadius = 1.0)
    {
        var entities = new Entity[count];
        double massPerStar = totalMass / count;
        var rng = new Random(42);

        for (int i = 0; i < count; i++)
        {
            // Plummer sphere: r = a / sqrt(u^{-2/3} - 1) where u is uniform [0,1)
            double u = rng.NextDouble() * 0.999 + 0.001; // avoid 0 and 1
            double r = plummerRadius / System.Math.Sqrt(System.Math.Pow(u, -2.0 / 3.0) - 1.0);
            r = System.Math.Min(r, plummerRadius * 10.0); // Clamp outliers

            // Uniform direction on sphere
            double cosTheta = 2.0 * rng.NextDouble() - 1.0;
            double sinTheta = System.Math.Sqrt(1.0 - cosTheta * cosTheta);
            double phi = 2.0 * System.Math.PI * rng.NextDouble();

            Vec3d pos = centrePosition + new Vec3d(
                r * sinTheta * System.Math.Cos(phi),
                r * sinTheta * System.Math.Sin(phi),
                r * cosTheta);

            // Velocity dispersion from Plummer model: σ² = G*M / (6*a)
            double sigma = System.Math.Sqrt(PhysicalConstants.G_Sim * totalMass / (6.0 * plummerRadius));
            Vec3d vel = new Vec3d(
                sigma * (rng.NextDouble() * 2.0 - 1.0),
                sigma * (rng.NextDouble() * 2.0 - 1.0),
                sigma * (rng.NextDouble() * 2.0 - 1.0));

            entities[i] = StarFactory.CreateStar(massPerStar, pos, vel, tag: "ClusterStar");
        }

        return entities;
    }
}

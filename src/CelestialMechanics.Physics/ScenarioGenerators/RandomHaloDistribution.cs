using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.ScenarioGenerators;

/// <summary>
/// Generates a uniform random spherical distribution of bodies.
///
/// USAGE
/// -----
/// Intended for stress testing and benchmarking. Bodies are distributed
/// uniformly within a sphere of given radius with random (bounded) velocities.
/// This distribution is NOT dynamically stable — it will collapse and virialise
/// over time, which makes it useful for testing energy conservation under
/// violent relaxation.
///
/// SAMPLING
/// --------
/// Uses rejection sampling: generate uniform random points in a cube,
/// reject those outside the sphere. Expected acceptance rate = π/6 ≈ 52%.
///
/// No per-frame allocations. Output is PhysicsBody[] ready for SoA conversion.
/// </summary>
public static class RandomHaloDistribution
{
    /// <summary>
    /// Generate a uniform spherical distribution with random velocities.
    /// </summary>
    /// <param name="count">Number of bodies.</param>
    /// <param name="totalMass">Total mass distributed equally among bodies.</param>
    /// <param name="radius">Radius of the bounding sphere.</param>
    /// <param name="maxSpeed">Maximum initial speed for random velocities.</param>
    /// <param name="seed">Random seed for reproducibility.</param>
    public static PhysicsBody[] Generate(int count, double totalMass = 100.0,
                                          double radius = 10.0, double maxSpeed = 0.3,
                                          int seed = 42)
    {
        var rng = seed >= 0 ? new Random(seed) : new Random();
        var bodies = new PhysicsBody[count];
        double bodyMass = totalMass / count;

        for (int i = 0; i < count; i++)
        {
            // ── Position: uniform in sphere via rejection sampling ────────────
            double px, py, pz;
            while (true)
            {
                px = (rng.NextDouble() * 2.0 - 1.0) * radius;
                py = (rng.NextDouble() * 2.0 - 1.0) * radius;
                pz = (rng.NextDouble() * 2.0 - 1.0) * radius;
                if (px * px + py * py + pz * pz <= radius * radius)
                    break;
            }

            // ── Velocity: uniform random in sphere of radius maxSpeed ────────
            double vx, vy, vz;
            while (true)
            {
                vx = (rng.NextDouble() * 2.0 - 1.0) * maxSpeed;
                vy = (rng.NextDouble() * 2.0 - 1.0) * maxSpeed;
                vz = (rng.NextDouble() * 2.0 - 1.0) * maxSpeed;
                if (vx * vx + vy * vy + vz * vz <= maxSpeed * maxSpeed)
                    break;
            }

            bodies[i] = new PhysicsBody(i, bodyMass,
                new Vec3d(px, py, pz),
                new Vec3d(vx, vy, vz),
                BodyType.Star)
            {
                IsActive = true,
                Radius = 0.02,
                GravityStrength = 60,
                GravityRange = 0
            };
        }

        return bodies;
    }
}

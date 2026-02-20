using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.ScenarioGenerators;

/// <summary>
/// Generates an N-body system following the Plummer density profile.
///
/// PHYSICS BACKGROUND
/// ------------------
/// The Plummer model describes a spherically symmetric, self-gravitating
/// system commonly used to model globular clusters. The density profile:
///
///   ρ(r) = (3M / 4πa³) · (1 + r²/a²)^(-5/2)
///
/// where M is total mass and 'a' is the Plummer scale length (core radius).
///
/// The cumulative mass function has an analytic inverse, allowing exact
/// sampling via inverse transform:
///
///   r = a / sqrt(U^(-2/3) - 1)    where U ~ Uniform(0,1)
///
/// Velocities are sampled from the distribution function f(E) using the
/// rejection sampling method described in Aarseth, Hénon & Wielen (1974).
///
/// USAGE
/// -----
/// var bodies = PlummerSphere.Generate(count: 10000, totalMass: 100.0, scaleLength: 2.0);
///
/// No per-frame allocations. Output is a PhysicsBody[] ready for SoA conversion.
/// </summary>
public static class PlummerSphere
{
    /// <summary>
    /// Generate a Plummer sphere with the given parameters.
    /// </summary>
    /// <param name="count">Number of bodies to generate.</param>
    /// <param name="totalMass">Total mass of the system.</param>
    /// <param name="scaleLength">Plummer scale length 'a' (core radius).</param>
    /// <param name="seed">Random seed for reproducibility. Use -1 for non-deterministic.</param>
    public static PhysicsBody[] Generate(int count, double totalMass = 100.0,
                                          double scaleLength = 2.0, int seed = 42)
    {
        var rng = seed >= 0 ? new Random(seed) : new Random();
        var bodies = new PhysicsBody[count];
        double bodyMass = totalMass / count;

        // Gravitational constant in simulation units (G = 1)
        // Escape velocity from center: v_esc = sqrt(2 * G * M / a)
        double escapeVelocity = System.Math.Sqrt(2.0 * PhysicalConstants.G_Sim * totalMass / scaleLength);

        for (int i = 0; i < count; i++)
        {
            // ── Position sampling via inverse CDF ─────────────────────────────
            // M(r) / M_total = r³ / (r² + a²)^(3/2)
            // Inverting: r = a / sqrt(U^(-2/3) - 1)
            double u = rng.NextDouble();
            // Clamp to avoid division by zero or infinity
            u = System.Math.Max(u, 1e-10);
            u = System.Math.Min(u, 1.0 - 1e-10);

            double r = scaleLength / System.Math.Sqrt(System.Math.Pow(u, -2.0 / 3.0) - 1.0);

            // Random direction on sphere (uniform)
            double cosTheta = 2.0 * rng.NextDouble() - 1.0;
            double sinTheta = System.Math.Sqrt(1.0 - cosTheta * cosTheta);
            double phi = 2.0 * System.Math.PI * rng.NextDouble();

            double px = r * sinTheta * System.Math.Cos(phi);
            double py = r * sinTheta * System.Math.Sin(phi);
            double pz = r * cosTheta;

            // ── Velocity sampling via rejection method ────────────────────────
            // Local escape velocity at radius r:
            //   v_esc(r) = sqrt(2 * G * M / sqrt(r² + a²))
            double localEscapeVel = System.Math.Sqrt(
                2.0 * PhysicalConstants.G_Sim * totalMass /
                System.Math.Sqrt(r * r + scaleLength * scaleLength));

            // Rejection sampling: sample speed from g(q) ∝ q² * (1-q²)^(7/2)
            // where q = v / v_esc(r), 0 ≤ q ≤ 1
            double speed;
            while (true)
            {
                double q = rng.NextDouble();
                double g = q * q * System.Math.Pow(1.0 - q * q, 3.5);
                // Maximum of g(q) occurs at q = 1/3, g_max ≈ 0.092
                if (rng.NextDouble() * 0.1 < g)
                {
                    speed = q * localEscapeVel;
                    break;
                }
            }

            // Random velocity direction
            double vCosTheta = 2.0 * rng.NextDouble() - 1.0;
            double vSinTheta = System.Math.Sqrt(1.0 - vCosTheta * vCosTheta);
            double vPhi = 2.0 * System.Math.PI * rng.NextDouble();

            double vx = speed * vSinTheta * System.Math.Cos(vPhi);
            double vy = speed * vSinTheta * System.Math.Sin(vPhi);
            double vz = speed * vCosTheta;

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

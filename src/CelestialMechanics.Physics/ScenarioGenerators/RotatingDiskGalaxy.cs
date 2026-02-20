using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.ScenarioGenerators;

/// <summary>
/// Generates a rotating disk galaxy initial condition.
///
/// PHYSICS MODEL
/// -------------
/// The galaxy consists of three components:
///
/// 1. CENTRAL CORE
///    A single massive body (or small number of bodies) at the origin,
///    representing a supermassive black hole or dense bulge.
///
/// 2. EXPONENTIAL DISK
///    Bodies distributed in a thin disk (XZ plane) with surface density:
///      Σ(R) ∝ exp(-R / R_d)
///    where R is cylindrical radius and R_d is the disk scale length.
///    Sampled via inverse CDF: R = -R_d * ln(1 - U).
///
/// 3. TANGENTIAL VELOCITY
///    Each disk body is initialized with circular orbital velocity:
///      v_circ(R) = sqrt(G * M(R) / R)
///    where M(R) is the enclosed mass (core + disk interior + optional halo).
///    The velocity vector is tangent to the circular orbit in the XZ plane.
///
/// 4. OPTIONAL DARK MATTER HALO (analytical approximation)
///    Adds extra enclosed mass M_halo(R) = M_halo * R / (R + R_halo) to
///    flatten the rotation curve at large radii. This does NOT add explicit
///    halo particles — it modifies the circular velocity calculation only.
///
/// USAGE
/// -----
/// var bodies = RotatingDiskGalaxy.Generate(count: 10000, coreMass: 50.0);
/// </summary>
public static class RotatingDiskGalaxy
{
    /// <summary>
    /// Generate a rotating disk galaxy.
    /// </summary>
    /// <param name="count">Total number of bodies (including core).</param>
    /// <param name="coreMass">Mass of the central core body.</param>
    /// <param name="diskMass">Total mass of all disk bodies.</param>
    /// <param name="diskScaleLength">Exponential disk scale length R_d.</param>
    /// <param name="diskThickness">Half-thickness of the disk in Y direction.</param>
    /// <param name="haloMass">Dark matter halo mass (0 = no halo). Affects velocity only.</param>
    /// <param name="haloScaleLength">Halo scale length for NFW-like profile.</param>
    /// <param name="seed">Random seed for reproducibility.</param>
    public static PhysicsBody[] Generate(
        int count,
        double coreMass = 50.0,
        double diskMass = 50.0,
        double diskScaleLength = 3.0,
        double diskThickness = 0.1,
        double haloMass = 0.0,
        double haloScaleLength = 10.0,
        int seed = 42)
    {
        var rng = seed >= 0 ? new Random(seed) : new Random();
        var bodies = new PhysicsBody[count];

        // ── Central core (body 0) ─────────────────────────────────────────────
        bodies[0] = new PhysicsBody(0, coreMass,
            Vec3d.Zero, Vec3d.Zero, BodyType.Star)
        {
            IsActive = true,
            Radius = 0.1,
            GravityStrength = 60,
            GravityRange = 0
        };

        // ── Disk bodies ───────────────────────────────────────────────────────
        int diskCount = count - 1;
        double bodyMass = diskMass / diskCount;

        // Precompute cumulative disk mass for enclosed mass calculation.
        // For an exponential disk: M(R) = M_disk * [1 - (1 + R/R_d) * exp(-R/R_d)]
        for (int i = 0; i < diskCount; i++)
        {
            int idx = i + 1;

            // ── Radial position: inverse CDF of exponential disk ──────────────
            // Σ(R) ∝ exp(-R/R_d), CDF: P(R) = 1 - (1 + R/R_d) * exp(-R/R_d)
            // Use rejection sampling for simplicity with this CDF
            double u = rng.NextDouble();
            u = System.Math.Max(u, 1e-10);
            u = System.Math.Min(u, 1.0 - 1e-10);

            // Approximate inverse CDF using Newton's method or simple sampling:
            // For simplicity, use the simpler exponential R = -R_d * ln(1 - U)
            // which gives a reasonable exponential-like radial distribution
            double R = -diskScaleLength * System.Math.Log(1.0 - u);

            // Random azimuthal angle in the disk plane (XZ)
            double angle = 2.0 * System.Math.PI * rng.NextDouble();
            double px = R * System.Math.Cos(angle);
            double pz = R * System.Math.Sin(angle);

            // Small random Y offset for disk thickness
            double py = (rng.NextDouble() - 0.5) * 2.0 * diskThickness;

            // ── Enclosed mass at radius R ─────────────────────────────────────
            // Core mass + disk interior + optional dark matter halo
            double enclosedDiskMass = diskMass *
                (1.0 - (1.0 + R / diskScaleLength) * System.Math.Exp(-R / diskScaleLength));
            double enclosedHaloMass = haloMass > 0.0
                ? haloMass * R / (R + haloScaleLength)
                : 0.0;
            double totalEnclosedMass = coreMass + enclosedDiskMass + enclosedHaloMass;

            // ── Circular orbital velocity ─────────────────────────────────────
            // v_circ = sqrt(G * M_enclosed / R)
            double vCirc = R > 1e-10
                ? System.Math.Sqrt(PhysicalConstants.G_Sim * totalEnclosedMass / R)
                : 0.0;

            // Velocity is tangent to the circle in the XZ plane
            // For counterclockwise rotation: v = vCirc * (-sin(angle), 0, cos(angle))
            double vx = -vCirc * System.Math.Sin(angle);
            double vz = vCirc * System.Math.Cos(angle);

            bodies[idx] = new PhysicsBody(idx, bodyMass,
                new Vec3d(px, py, pz),
                new Vec3d(vx, 0, vz),
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

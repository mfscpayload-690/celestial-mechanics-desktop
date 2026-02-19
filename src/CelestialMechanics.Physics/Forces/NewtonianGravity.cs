using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.Forces;

/// <summary>
/// Newtonian gravitational force: F = G * m1 * m2 / (r^2 + epsilon^2).
/// Uses softening parameter to prevent singularity as r approaches 0.
/// Applies smooth Hermite falloff at gravity range boundary.
/// </summary>
public class NewtonianGravity : IForceCalculator
{
    public string Name => "Newtonian Gravity";
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Softening parameter to prevent r->0 singularity.
    /// </summary>
    public double SofteningEpsilon { get; set; } = 1e-4;

    /// <summary>
    /// Multiplier applied to GravityRange to determine falloff distance in simulation units.
    /// Default 1000.0.
    /// </summary>
    public double RangeScale { get; set; } = 1000.0;

    public Vec3d ComputeForce(in PhysicsBody a, in PhysicsBody b)
    {
        if (!Enabled) return Vec3d.Zero;

        Vec3d r = b.Position - a.Position;
        double distSq = r.LengthSquared + SofteningEpsilon * SofteningEpsilon;
        double dist = System.Math.Sqrt(distSq);

        // Gravity range cutoff with smooth Hermite falloff
        double maxGravityRange = System.Math.Max(a.GravityRange, b.GravityRange);
        if (maxGravityRange > 0.0)
        {
            double maxRange = maxGravityRange * RangeScale;
            if (dist > maxRange) return Vec3d.Zero;

            double forceMag = PhysicalConstants.G_Sim * a.Mass * b.Mass / distSq;

            // Smooth Hermite falloff in last 20% of range
            double t = dist / maxRange;
            if (t > 0.8)
            {
                double s = (t - 0.8) / 0.2; // 0 -> 1 in last 20% of range
                forceMag *= 1.0 - s * s * (3.0 - 2.0 * s); // Hermite smoothstep
            }

            return r * (forceMag / dist);
        }

        // No range limit (infinite range) when both GravityRange values are 0
        double fMag = PhysicalConstants.G_Sim * a.Mass * b.Mass / distSq;
        return r * (fMag / dist);
    }

    public double ComputePotentialEnergy(in PhysicsBody a, in PhysicsBody b)
    {
        if (!Enabled) return 0.0;

        Vec3d r = b.Position - a.Position;
        double distSq = r.LengthSquared + SofteningEpsilon * SofteningEpsilon;
        double dist = System.Math.Sqrt(distSq);

        return -PhysicalConstants.G_Sim * a.Mass * b.Mass / dist;
    }
}

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
    /// When true, applies shell-theorem interior gravity when the target point
    /// is inside the source body's radius.
    /// </summary>
    public bool EnableShellTheorem { get; set; } = false;

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
        double rawDistSq = r.LengthSquared;
        double eps2 = SofteningEpsilon * SofteningEpsilon;
        double distSq = rawDistSq + eps2;
        double dist = System.Math.Sqrt(distSq);

        // Gravity range cutoff with smooth Hermite falloff
        double maxGravityRange = System.Math.Max(a.GravityRange, b.GravityRange);
        if (maxGravityRange > 0.0)
        {
            double maxRange = maxGravityRange * RangeScale;
            if (dist > maxRange) return Vec3d.Zero;

            double forceOverDistance = ComputeForceOverDistanceCoefficient(a.Mass, b.Mass, rawDistSq, b.Radius, eps2);

            // Smooth Hermite falloff in last 20% of range
            double t = dist / maxRange;
            if (t > 0.8)
            {
                double s = (t - 0.8) / 0.2; // 0 -> 1 in last 20% of range
                forceOverDistance *= 1.0 - s * s * (3.0 - 2.0 * s); // Hermite smoothstep
            }

            return r * forceOverDistance;
        }

        // No range limit (infinite range) when both GravityRange values are 0
        double fOverDistance = ComputeForceOverDistanceCoefficient(a.Mass, b.Mass, rawDistSq, b.Radius, eps2);
        return r * fOverDistance;
    }

    public double ComputePotentialEnergy(in PhysicsBody a, in PhysicsBody b)
    {
        if (!Enabled) return 0.0;

        Vec3d r = b.Position - a.Position;
        double distSq = r.LengthSquared + SofteningEpsilon * SofteningEpsilon;
        double dist = System.Math.Sqrt(distSq);

        return -PhysicalConstants.G_Sim * a.Mass * b.Mass / dist;
    }

    private double ComputeForceOverDistanceCoefficient(
        double targetMass,
        double sourceMass,
        double rawDistSq,
        double sourceRadius,
        double eps2)
    {
        double softenedDist = System.Math.Sqrt(rawDistSq + eps2);
        double outsideCoeff = PhysicalConstants.G_Sim * targetMass * sourceMass /
                              (softenedDist * softenedDist * softenedDist);

        if (!EnableShellTheorem || sourceRadius <= 0.0)
            return outsideCoeff;

        double rawDist = System.Math.Sqrt(rawDistSq);
        if (rawDist < sourceRadius)
        {
            // Interior linear field: F_vec = coeff * r_vec
            // coeff is matched to softened exterior value at r = R.
            double denom = sourceRadius * sourceRadius + eps2;
            double invDenomSqrt = 1.0 / System.Math.Sqrt(denom);
            return PhysicalConstants.G_Sim * targetMass * sourceMass * invDenomSqrt / denom;
        }

        return outsideCoeff;
    }
}

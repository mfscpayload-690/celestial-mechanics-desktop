namespace CelestialMechanics.Physics.Solvers;

internal static class GravityKernel
{
    /// <summary>
    /// Returns acceleration coefficient a = coeff * r_vec where r_vec points
    /// from target to source body.
    /// </summary>
    public static double AccelerationCoeffFromSource(
        double distSq,
        double sourceMass,
        double sourceRadius,
        double eps2,
        bool enableShellTheorem)
    {
        if (!enableShellTheorem || sourceRadius <= 0.0)
        {
            double invDist = 1.0 / System.Math.Sqrt(distSq + eps2);
            return sourceMass * invDist * invDist * invDist;
        }

        double dist = System.Math.Sqrt(distSq);
        if (dist < sourceRadius)
        {
            // Linear interior field matched to the softened exterior value at r = R.
            double denom = sourceRadius * sourceRadius + eps2;
            double invDenomSqrt = 1.0 / System.Math.Sqrt(denom);
            return sourceMass * invDenomSqrt / denom;
        }

        double invDistOuter = 1.0 / System.Math.Sqrt(distSq + eps2);
        return sourceMass * invDistOuter * invDistOuter * invDistOuter;
    }
}

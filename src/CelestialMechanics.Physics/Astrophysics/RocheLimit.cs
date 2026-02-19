namespace CelestialMechanics.Physics.Astrophysics;

/// <summary>
/// Computes the Roche limit -- the distance within which a secondary body,
/// held together only by its own gravity, will be torn apart by tidal forces
/// from the primary body.
/// d = R * (2 * M_primary / m_secondary)^(1/3)
/// </summary>
public static class RocheLimit
{
    /// <summary>
    /// Compute the Roche limit distance.
    /// </summary>
    /// <param name="primaryMass">Mass of the primary (larger) body in simulation units.</param>
    /// <param name="secondaryMass">Mass of the secondary (smaller) body in simulation units.</param>
    /// <param name="secondaryRadius">Radius of the secondary body in simulation units.</param>
    /// <returns>Roche limit distance in simulation units.</returns>
    public static double Compute(double primaryMass, double secondaryMass, double secondaryRadius)
    {
        double massRatio = 2.0 * primaryMass / secondaryMass;
        return secondaryRadius * System.Math.Cbrt(massRatio);
    }
}

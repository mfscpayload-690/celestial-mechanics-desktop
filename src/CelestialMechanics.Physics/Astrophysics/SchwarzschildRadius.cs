using CelestialMechanics.Math;

namespace CelestialMechanics.Physics.Astrophysics;

/// <summary>
/// Computes the Schwarzschild radius for a given mass.
/// r_s = 2GM/c^2 -- the radius of the event horizon for a non-rotating black hole.
/// </summary>
public static class SchwarzschildRadius
{
    /// <summary>
    /// Compute Schwarzschild radius in meters.
    /// Input mass is in solar masses (simulation units); converts to kg internally.
    /// </summary>
    /// <param name="mass">Mass in solar masses.</param>
    /// <returns>Schwarzschild radius in meters.</returns>
    public static double Compute(double mass)
    {
        double massKg = mass * PhysicalConstants.SolarMass;
        return 2.0 * PhysicalConstants.G_SI * massKg / (PhysicalConstants.C * PhysicalConstants.C);
    }

    /// <summary>
    /// Compute Schwarzschild radius in simulation units (AU).
    /// </summary>
    /// <param name="simMass">Mass in simulation units (solar masses).</param>
    /// <returns>Schwarzschild radius in AU.</returns>
    public static double ComputeSimUnits(double simMass)
    {
        double radiusMeters = Compute(simMass);
        return radiusMeters / PhysicalConstants.AU;
    }
}

namespace CelestialMechanics.Math;

/// <summary>
/// Conversion utilities between SI units and simulation (normalized) units.
/// Simulation units: mass in solar masses, distance in AU, time in <see cref="PhysicalConstants.TimeUnit"/> seconds.
/// </summary>
public static class UnitConversion
{
    /// <summary>Converts mass from kilograms to simulation units (solar masses).</summary>
    public static double MassToSim(double kg) => kg / PhysicalConstants.SolarMass;

    /// <summary>Converts mass from simulation units (solar masses) to kilograms.</summary>
    public static double MassToSI(double sim) => sim * PhysicalConstants.SolarMass;

    /// <summary>Converts distance from metres to simulation units (AU).</summary>
    public static double DistToSim(double m) => m / PhysicalConstants.AU;

    /// <summary>Converts distance from simulation units (AU) to metres.</summary>
    public static double DistToSI(double sim) => sim * PhysicalConstants.AU;

    /// <summary>Converts time from seconds to simulation time units.</summary>
    public static double TimeToSim(double s) => s / PhysicalConstants.TimeUnit;

    /// <summary>Converts time from simulation time units to seconds.</summary>
    public static double TimeToSI(double sim) => sim * PhysicalConstants.TimeUnit;

    /// <summary>Converts velocity from m/s to simulation units (AU / TimeUnit).</summary>
    public static double VelocityToSim(double mps) => (mps * PhysicalConstants.TimeUnit) / PhysicalConstants.AU;

    /// <summary>Converts velocity from simulation units (AU / TimeUnit) to m/s.</summary>
    public static double VelocityToSI(double sim) => (sim * PhysicalConstants.AU) / PhysicalConstants.TimeUnit;
}

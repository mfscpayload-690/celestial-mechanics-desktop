namespace CelestialMechanics.Math;

/// <summary>
/// Physical constants used throughout the simulation, in SI and normalized units.
/// </summary>
public static class PhysicalConstants
{
    /// <summary>Gravitational constant in m^3/(kg*s^2).</summary>
    public const double G_SI = 6.67430e-11;

    /// <summary>Speed of light in m/s.</summary>
    public const double C = 2.998e8;

    /// <summary>Solar mass in kg.</summary>
    public const double SolarMass = 1.989e30;

    /// <summary>Solar radius in m.</summary>
    public const double SolarRadius = 6.957e8;

    /// <summary>Astronomical unit in m.</summary>
    public const double AU = 1.496e11;

    /// <summary>Time unit in seconds: sqrt(AU^3 / (G_SI * SolarMass)).</summary>
    public const double TimeUnit = 5.0226e6;

    /// <summary>Gravitational constant in simulation (normalized) units.</summary>
    public const double G_Sim = 1.0;

    /// <summary>Schwarzschild factor: 2 * G_SI / c^2.</summary>
    public const double SchwarzschildFactor = 2.0 * G_SI / (C * C);
}

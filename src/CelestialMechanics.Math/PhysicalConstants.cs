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

    // ── Phase 6: Relativistic constants in simulation units ───────────────────

    /// <summary>
    /// Speed of light in simulation units (AU / TimeUnit).
    /// C_Sim = C_SI × TimeUnit / AU ≈ 10065.3
    /// </summary>
    public const double C_Sim = C * TimeUnit / AU;

    /// <summary>c² in simulation units.</summary>
    public const double C_Sim2 = C_Sim * C_Sim;

    /// <summary>c⁴ in simulation units (used in gravitational wave strain formula).</summary>
    public const double C_Sim4 = C_Sim2 * C_Sim2;

    /// <summary>c⁵ in simulation units (used in GW energy loss rate).</summary>
    public const double C_Sim5 = C_Sim4 * C_Sim;

    /// <summary>
    /// Schwarzschild radius in simulation units: Rs = 2·G·M / c² = 2·M / c²_sim
    /// (since G_Sim = 1). For M = 1 M☉, Rs ≈ 1.97e-8 AU.
    /// </summary>
    public const double SchwarzschildFactorSim = 2.0 * G_Sim / C_Sim2;

    /// <summary>
    /// Radiative accretion efficiency η. Fraction of rest-mass energy
    /// converted to luminosity. Typical value for Schwarzschild BH: 0.057.
    /// </summary>
    public const double AccretionEfficiency = 0.057;
}

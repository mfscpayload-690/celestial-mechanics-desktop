namespace CelestialMechanics.Data;

/// <summary>
/// Maps user-facing slider values to physical quantities for gravity configuration.
/// All mass values are in solar masses; distances are in AU.
/// </summary>
public static class GravityStrengthMap
{
    private const double LogMin = -15.0; // log10(asteroid mass / solar mass)
    private const double LogMax = 6.0;   // log10(SMBH mass / solar mass)

    private const double SolarRadiusInAU = 0.00465047; // ~6.957e8 m / 1.496e11 m

    /// <summary>
    /// Maps a strength slider value (0-100) to mass in solar masses via an exponential curve.
    /// </summary>
    public static double StrengthToMass(double strength)
    {
        strength = System.Math.Clamp(strength, 0.0, 100.0);
        double logMass = LogMin + (strength / 100.0) * (LogMax - LogMin);
        return System.Math.Pow(10.0, logMass);
    }

    /// <summary>
    /// Inverse mapping: mass (in solar masses) to strength slider value (0-100).
    /// </summary>
    public static double MassToStrength(double massSolar)
    {
        if (massSolar <= 0) return 0;
        double logMass = System.Math.Log10(massSolar);
        return System.Math.Clamp((logMass - LogMin) / (LogMax - LogMin) * 100.0, 0.0, 100.0);
    }

    /// <summary>
    /// Maps a range slider value (0-10) to a gravitational cutoff distance in AU.
    /// Range 0 => 0.1 AU, range 10 => ~31.6 AU.
    /// </summary>
    public static double RangeToDistance(double range)
    {
        range = System.Math.Clamp(range, 0.0, 10.0);
        return 0.1 * System.Math.Pow(10.0, range * 0.5);
    }

    /// <summary>
    /// Rough radius estimate (in AU) based on mass (in solar masses) and body type string.
    /// Body type should match the Physics.Types.BodyType enum names:
    /// "Star", "Planet", "GasGiant", "RockyPlanet", "Moon", "Asteroid",
    /// "NeutronStar", "BlackHole", "Comet", "Custom".
    /// </summary>
    public static double EstimateRadius(double massSolar, string bodyType)
    {
        return bodyType switch
        {
            // Main-sequence approximation: R ~ M^0.8 in solar radii, converted to AU
            "Star" => System.Math.Pow(massSolar, 0.8) * SolarRadiusInAU,

            // Rocky planets: scale from Earth (3e-6 M_sun, R ~ 4.26e-5 AU)
            "RockyPlanet" or "Planet" => 4.26e-5 * System.Math.Pow(massSolar / 3.0e-6, 0.27),

            // Gas giants: scale from Jupiter (9.5e-4 M_sun, R ~ 4.67e-4 AU)
            // Radius varies weakly with mass for gas giants
            "GasGiant" => 4.67e-4 * System.Math.Pow(massSolar / 9.5e-4, 0.1),

            // Schwarzschild radius: Rs = 2GM/c^2, in AU ~ massSolar * 1.974e-8
            "BlackHole" => massSolar * 1.974e-8,

            // Neutron stars: ~10 km regardless of mass ~ 6.68e-8 AU
            "NeutronStar" => 6.68e-8,

            // Moons: scale from Earth's Moon (3.7e-8 M_sun, R ~ 1.16e-5 AU)
            "Moon" => 1.16e-5 * System.Math.Pow(massSolar / 3.7e-8, 0.27),

            // Asteroids / Comets: very small, ~1e-8 AU baseline
            "Asteroid" or "Comet" => 1.0e-8 * System.Math.Pow(massSolar / 1.0e-12, 0.33),

            // Custom / unknown: use star-like scaling as a fallback
            _ => System.Math.Pow(System.Math.Max(massSolar, 1e-15), 0.8) * SolarRadiusInAU,
        };
    }
}

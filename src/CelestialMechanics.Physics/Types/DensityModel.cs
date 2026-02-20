using CelestialMechanics.Math;

namespace CelestialMechanics.Physics.Types;

/// <summary>
/// Physical density model for computing body radii from mass and density.
///
/// Assumes uniform-density spheres:
///   ρ = m / V,  V = 4/3 π r³
///   ∴ r = (3m / 4πρ)^(1/3)
///
/// For black holes, the "radius" is the Schwarzschild radius:
///   r_s = 2GM / c²
/// </summary>
public static class DensityModel
{
    // ── Default densities (kg/m³, but used as relative values in sim units) ────
    // These are approximate physical densities normalised so that the simulation
    // produces visually reasonable radii. Actual SI densities would make bodies
    // invisible at AU-scale rendering.
    //
    // We use simulation-scale densities: mass in solar masses, radius in AU.
    // ρ_sim = m / (4/3 π r³) where m is in M☉ and r is in AU.

    /// <summary>Default density for stars (Sun-like: ρ ≈ 1410 kg/m³ → sim scaled).</summary>
    public const double StarDensity = 1000.0;

    /// <summary>Default density for rocky planets.</summary>
    public const double RockyPlanetDensity = 5000.0;

    /// <summary>Default density for gas giants.</summary>
    public const double GasGiantDensity = 1300.0;

    /// <summary>Default density for neutron stars (extremely high).</summary>
    public const double NeutronStarDensity = 1e12;

    /// <summary>Default density for asteroids.</summary>
    public const double AsteroidDensity = 3000.0;

    /// <summary>Default density for moons.</summary>
    public const double MoonDensity = 3300.0;

    /// <summary>Default density for comets.</summary>
    public const double CometDensity = 500.0;

    /// <summary>
    /// Compute the radius of a uniform-density sphere.
    /// r = (3m / 4πρ)^(1/3)
    /// </summary>
    /// <param name="mass">Body mass (must be > 0).</param>
    /// <param name="density">Body density (must be > 0).</param>
    /// <returns>Radius in the same length unit as the mass/density ratio implies.</returns>
    public static double ComputeRadius(double mass, double density)
    {
        if (mass <= 0.0 || density <= 0.0)
            return 0.0;

        double volume = mass / density;
        return System.Math.Cbrt(3.0 * volume / (4.0 * System.Math.PI));
    }

    /// <summary>
    /// Compute the Schwarzschild radius for a black hole.
    /// r_s = 2GM/c² (in simulation units: G=1, mass in M☉, result in sim length).
    /// </summary>
    public static double ComputeSchwarzschildRadius(double mass)
    {
        if (mass <= 0.0)
            return 0.0;

        // In simulation units where G_sim = 1:
        // r_s = 2 * G_sim * mass / c_sim²
        // For visual purposes we use a scaled version that is visible at AU scale.
        // Physical r_s for 1 M☉ ≈ 3 km ≈ 2e-11 AU — invisible at rendering scale.
        // We use a minimum visual radius so black holes are visible.
        double physicalRs = 2.0 * PhysicalConstants.G_Sim * mass;
        // Scale up for visibility: at least 0.01 sim units
        return System.Math.Max(physicalRs, 0.01 * System.Math.Cbrt(mass));
    }

    /// <summary>
    /// Get the default density for a given body type.
    /// </summary>
    public static double GetDefaultDensity(BodyType type) => type switch
    {
        BodyType.Star => StarDensity,
        BodyType.Planet or BodyType.RockyPlanet => RockyPlanetDensity,
        BodyType.GasGiant => GasGiantDensity,
        BodyType.Moon => MoonDensity,
        BodyType.Asteroid => AsteroidDensity,
        BodyType.NeutronStar => NeutronStarDensity,
        BodyType.Comet => CometDensity,
        BodyType.BlackHole => 1.0, // Placeholder — BH uses Schwarzschild radius
        _ => StarDensity
    };

    /// <summary>
    /// Compute the appropriate radius for a body, handling black holes specially.
    /// </summary>
    public static double ComputeBodyRadius(double mass, double density, BodyType type)
    {
        if (type == BodyType.BlackHole)
            return ComputeSchwarzschildRadius(mass);

        return ComputeRadius(mass, density);
    }
}

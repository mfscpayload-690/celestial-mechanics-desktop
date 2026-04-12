using System.Numerics;

namespace CelestialMechanics.Data;

/// <summary>
/// Immutable template describing a type of celestial body for easy placement in the sandbox.
/// The <see cref="BodyType"/> field stores the type as a string matching the
/// Physics.Types.BodyType enum names. The App layer is responsible for converting
/// templates into PhysicsBody instances.
/// </summary>
public record BodyTemplate(
    string Name,
    double Mass,            // Solar masses
    double Radius,          // AU
    double GravityStrength, // Slider value 0-100
    double GravityRange,    // Slider value 0-10
    string BodyType,        // Matches Physics.Types.BodyType enum name
    Vector4 Color           // RGBA for rendering
);

/// <summary>
/// Predefined body templates for common celestial objects.
/// </summary>
public static class ObjectTemplates
{
    public static readonly BodyTemplate Sun = new(
        "Sun", 1.0, 0.00465, 60.0, 8.0,
        "Star", new Vector4(1.0f, 0.9f, 0.3f, 1.0f));

    public static readonly BodyTemplate Earth = new(
        "Earth", 3.0e-6, 4.26e-5, 20.0, 3.0,
        "RockyPlanet", new Vector4(0.2f, 0.4f, 0.8f, 1.0f));

    public static readonly BodyTemplate Jupiter = new(
        "Jupiter", 9.5e-4, 4.67e-4, 35.0, 5.0,
        "GasGiant", new Vector4(0.8f, 0.7f, 0.5f, 1.0f));

    public static readonly BodyTemplate SmallBlackHole = new(
        "Black Hole (Stellar)", 21.2, 5.8e-7, 87.0, 7.0,
        "BlackHole", new Vector4(0.1f, 0.0f, 0.3f, 1.0f));

    public static readonly BodyTemplate NeutronStarTemplate = new(
        "Neutron Star", 1.4, 6.7e-8, 72.0, 6.0,
        "NeutronStar", new Vector4(0.5f, 0.8f, 1.0f, 1.0f));

    public static readonly BodyTemplate Asteroid = new(
        "Asteroid", 1e-12, 1e-8, 5.0, 1.0,
        "Asteroid", new Vector4(0.5f, 0.5f, 0.4f, 1.0f));

    public static readonly BodyTemplate TerrestrialPlanet = new(
        "Terrestrial Planet", 4.0e-6, 4.8e-5, 20.0, 3.0,
        "RockyPlanet", new Vector4(0.35f, 0.55f, 0.9f, 1.0f));

    public static readonly BodyTemplate GasGiant = new(
        "Gas Giant", 1.1e-3, 5.1e-4, 36.0, 5.0,
        "GasGiant", new Vector4(0.85f, 0.72f, 0.48f, 1.0f));

    public static readonly BodyTemplate DwarfPlanet = new(
        "Dwarf Planet", 5.0e-10, 1.2e-5, 9.0, 1.4,
        "Planet", new Vector4(0.75f, 0.68f, 0.62f, 1.0f));

    public static readonly BodyTemplate Moon = new(
        "Moon", 3.7e-8, 1.1e-5, 7.0, 1.4,
        "Moon", new Vector4(0.76f, 0.76f, 0.8f, 1.0f));

    public static readonly BodyTemplate Comet = new(
        "Comet", 5.0e-13, 7.0e-6, 4.0, 2.0,
        "Comet", new Vector4(0.7f, 0.85f, 0.95f, 1.0f));

    public static readonly BodyTemplate Meteoroid = new(
        "Meteoroid", 1.0e-15, 2.0e-6, 2.0, 0.8,
        "Asteroid", new Vector4(0.55f, 0.5f, 0.45f, 1.0f));

    public static readonly BodyTemplate OTypeStar = new(
        "O-Type Star", 40.0, 0.018, 82.0, 8.5,
        "Star", new Vector4(0.55f, 0.7f, 1.0f, 1.0f));

    public static readonly BodyTemplate BTypeStar = new(
        "B-Type Star", 12.0, 0.011, 78.0, 8.2,
        "Star", new Vector4(0.62f, 0.76f, 1.0f, 1.0f));

    public static readonly BodyTemplate ATypeStar = new(
        "A-Type Star", 2.2, 0.0062, 70.0, 7.6,
        "Star", new Vector4(0.78f, 0.84f, 1.0f, 1.0f));

    public static readonly BodyTemplate FTypeStar = new(
        "F-Type Star", 1.4, 0.0054, 65.0, 7.2,
        "Star", new Vector4(0.95f, 0.96f, 0.86f, 1.0f));

    public static readonly BodyTemplate GTypeStar = new(
        "G-Type Star", 1.0, 0.0047, 60.0, 6.8,
        "Star", new Vector4(1.0f, 0.9f, 0.35f, 1.0f));

    public static readonly BodyTemplate KTypeStar = new(
        "K-Type Star", 0.7, 0.0041, 54.0, 6.4,
        "Star", new Vector4(1.0f, 0.78f, 0.45f, 1.0f));

    public static readonly BodyTemplate MTypeStar = new(
        "M-Type Star", 0.2, 0.0028, 46.0, 6.0,
        "Star", new Vector4(0.95f, 0.5f, 0.4f, 1.0f));

    public static readonly BodyTemplate Hypergiant = new(
        "Hypergiant", 80.0, 0.032, 90.0, 9.0,
        "Star", new Vector4(1.0f, 0.78f, 0.72f, 1.0f));

    public static readonly BodyTemplate Supergiant = new(
        "Supergiant", 20.0, 0.016, 82.0, 8.7,
        "Star", new Vector4(1.0f, 0.7f, 0.62f, 1.0f));

    public static readonly BodyTemplate MainSequenceStar = new(
        "Main Sequence Star", 1.0, 0.0048, 60.0, 6.8,
        "Star", new Vector4(1.0f, 0.9f, 0.35f, 1.0f));

    public static readonly BodyTemplate WhiteDwarf = new(
        "White Dwarf", 0.8, 9.0e-5, 58.0, 5.0,
        "Custom", new Vector4(0.88f, 0.95f, 1.0f, 1.0f));

    public static readonly BodyTemplate Pulsar = new(
        "Pulsar", 1.5, 7.2e-8, 74.0, 6.2,
        "NeutronStar", new Vector4(0.45f, 0.85f, 1.0f, 1.0f));

    public static readonly BodyTemplate Magnetar = new(
        "Magnetar", 1.8, 7.6e-8, 76.0, 6.3,
        "NeutronStar", new Vector4(0.55f, 0.95f, 0.95f, 1.0f));

    public static readonly BodyTemplate EmissionNebula = new(
        "Emission Nebula", 0.06, 0.12, 15.0, 3.0,
        "Custom", new Vector4(0.55f, 0.36f, 0.82f, 1.0f));

    public static readonly BodyTemplate SpiralGalaxy = new(
        "Spiral Galaxy", 2500.0, 0.35, 92.0, 9.5,
        "Custom", new Vector4(0.62f, 0.76f, 1.0f, 1.0f));

    public static readonly BodyTemplate EllipticalGalaxy = new(
        "Elliptical Galaxy", 3000.0, 0.4, 94.0, 9.8,
        "Custom", new Vector4(1.0f, 0.88f, 0.62f, 1.0f));

    public static readonly BodyTemplate LenticularGalaxy = new(
        "Lenticular Galaxy", 2200.0, 0.33, 90.0, 9.2,
        "Custom", new Vector4(0.82f, 0.82f, 0.9f, 1.0f));

    public static readonly BodyTemplate IrregularGalaxy = new(
        "Irregular Galaxy", 1700.0, 0.29, 88.0, 9.0,
        "Custom", new Vector4(0.78f, 0.56f, 1.0f, 1.0f));

    public static readonly BodyTemplate Quasar = new(
        "Quasar", 950.0, 0.11, 94.0, 9.2,
        "Custom", new Vector4(0.68f, 0.86f, 1.0f, 1.0f));

    public static readonly BodyTemplate Blazar = new(
        "Blazar", 980.0, 0.11, 95.0, 9.2,
        "Custom", new Vector4(0.58f, 0.86f, 1.0f, 1.0f));

    public static readonly BodyTemplate GalaxyGroup = new(
        "Galaxy Group", 4200.0, 0.45, 96.0, 10.0,
        "Custom", new Vector4(0.75f, 0.9f, 1.0f, 1.0f));

    public static readonly BodyTemplate GalaxyCluster = new(
        "Galaxy Cluster", 12000.0, 0.7, 98.0, 10.0,
        "Custom", new Vector4(0.9f, 0.92f, 1.0f, 1.0f));

    public static readonly BodyTemplate Supercluster = new(
        "Supercluster", 45000.0, 1.2, 99.0, 10.0,
        "Custom", new Vector4(0.95f, 0.96f, 1.0f, 1.0f));

    /// <summary>
    /// All predefined templates for enumeration by the UI.
    /// </summary>
    public static readonly BodyTemplate[] All =
    {
        Sun,
        Earth,
        Jupiter,
        SmallBlackHole,
        NeutronStarTemplate,
        Asteroid,
        TerrestrialPlanet,
        GasGiant,
        DwarfPlanet,
        Moon,
        Comet,
        Meteoroid,
        OTypeStar,
        BTypeStar,
        ATypeStar,
        FTypeStar,
        GTypeStar,
        KTypeStar,
        MTypeStar,
        Hypergiant,
        Supergiant,
        MainSequenceStar,
        WhiteDwarf,
        Pulsar,
        Magnetar,
        EmissionNebula,
        SpiralGalaxy,
        EllipticalGalaxy,
        LenticularGalaxy,
        IrregularGalaxy,
        Quasar,
        Blazar,
        GalaxyGroup,
        GalaxyCluster,
        Supercluster
    };
}

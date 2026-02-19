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
        "RockyPlanet", new Vector4(0.2f, 0.4f, 0.8f, 0.5f));

    public static readonly BodyTemplate Jupiter = new(
        "Jupiter", 9.5e-4, 4.67e-4, 35.0, 5.0,
        "GasGiant", new Vector4(0.8f, 0.7f, 0.5f, 0.5f));

    public static readonly BodyTemplate SmallBlackHole = new(
        "Black Hole (Stellar)", 21.2, 5.8e-7, 87.0, 7.0,
        "BlackHole", new Vector4(0.1f, 0.0f, 0.3f, 0.5f));

    public static readonly BodyTemplate NeutronStarTemplate = new(
        "Neutron Star", 1.4, 6.7e-8, 72.0, 6.0,
        "NeutronStar", new Vector4(0.5f, 0.8f, 1.0f, 1.0f));

    public static readonly BodyTemplate Asteroid = new(
        "Asteroid", 1e-12, 1e-8, 5.0, 1.0,
        "Asteroid", new Vector4(0.5f, 0.5f, 0.4f, 0.5f));

    /// <summary>
    /// All predefined templates for enumeration by the UI.
    /// </summary>
    public static readonly BodyTemplate[] All =
    {
        Sun, Earth, Jupiter, SmallBlackHole, NeutronStarTemplate, Asteroid
    };
}

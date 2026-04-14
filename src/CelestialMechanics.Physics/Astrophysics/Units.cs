using CelestialMechanics.Math;

namespace CelestialMechanics.Physics.Astrophysics;

/// <summary>
/// Canonical SI unit helpers used by high-energy astrophysics systems.
/// Physics calculations should use SI through these helpers.
/// Render-only code may use <see cref="RenderScale"/>.
/// </summary>
public static class Units
{
    /// <summary>Gravitational constant in m^3 / (kg * s^2).</summary>
    public const double G = 6.67430e-11;

    /// <summary>Speed of light in m/s.</summary>
    public const double C = 299792458.0;

    public static double MassToKg(double solarMassUnits) => UnitConversion.MassToSI(solarMassUnits);

    public static double DistanceToMeters(double auUnits) => UnitConversion.DistToSI(auUnits);

    public static double TimeToSeconds(double simTimeUnits) => UnitConversion.TimeToSI(simTimeUnits);

    public static double VelocityToMetersPerSecond(double simVelocityUnits) => UnitConversion.VelocityToSI(simVelocityUnits);

    public static CelestialMechanics.Math.Vec3d VelocityToMetersPerSecond(CelestialMechanics.Math.Vec3d simVelocity)
    {
        return new CelestialMechanics.Math.Vec3d(
            VelocityToMetersPerSecond(simVelocity.X),
            VelocityToMetersPerSecond(simVelocity.Y),
            VelocityToMetersPerSecond(simVelocity.Z));
    }

    public static double VelocityToSim(double metersPerSecond) => UnitConversion.VelocityToSim(metersPerSecond);

    /// <summary>
    /// Rendering-only logarithmic scaling helper.
    /// Never use this in physics state updates.
    /// </summary>
    public static double RenderScale(double value)
    {
        return System.Math.Log10(System.Math.Max(0.0, value) + 1.0);
    }
}
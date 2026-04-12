namespace CelestialMechanics.Desktop.Infrastructure.Security;

/// <summary>
/// Validates and clamps user-provided numeric inputs for physics parameters (SEC-06).
/// Prevents NaN/Infinity from poisoning the physics engine.
/// </summary>
public static class PhysicsInputValidator
{
    public static readonly (double Min, double Max) MassRange     = (1e-10, 1e10);
    public static readonly (double Min, double Max) RadiusRange   = (1e-6, 1e4);
    public static readonly (double Min, double Max) VelocityRange = (-1e6, 1e6);
    public static readonly (double Min, double Max) PositionRange = (-1e6, 1e6);
    public static readonly (double Min, double Max) TimestepRange = (1e-6, 1.0);
    public static readonly (double Min, double Max) SofteningRange = (1e-8, 1.0);

    public static double ClampMass(double v)     => Clamp(v, MassRange);
    public static double ClampRadius(double v)   => Clamp(v, RadiusRange);
    public static double ClampVelocity(double v) => Clamp(v, VelocityRange);
    public static double ClampPosition(double v) => Clamp(v, PositionRange);
    public static double ClampTimestep(double v) => Clamp(v, TimestepRange);
    public static double ClampSoftening(double v) => Clamp(v, SofteningRange);

    /// <summary>
    /// Returns true if the value is a valid finite number.
    /// </summary>
    public static bool IsFinite(double v)
        => !double.IsNaN(v) && !double.IsInfinity(v);

    private static double Clamp(double v, (double Min, double Max) r)
        => System.Math.Clamp(double.IsNaN(v) || double.IsInfinity(v) ? r.Min : v, r.Min, r.Max);
}

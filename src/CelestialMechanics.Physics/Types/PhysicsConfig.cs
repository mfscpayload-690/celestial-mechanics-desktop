namespace CelestialMechanics.Physics.Types;

/// <summary>
/// Configuration parameters for the physics simulation.
/// </summary>
public class PhysicsConfig
{
    public double TimeStep { get; set; } = 0.001;
    public double SofteningEpsilon { get; set; } = 1e-4;
    public string IntegratorName { get; set; } = "Verlet";
    public double GravityRangeScale { get; set; } = 1000.0;
}

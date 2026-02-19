using CelestialMechanics.Math;

namespace CelestialMechanics.Physics.Types;

/// <summary>
/// Immutable snapshot of simulation state at a given point in time.
/// Used for diagnostics, energy monitoring, and render interpolation.
/// </summary>
public class SimulationState
{
    public double Time { get; init; }
    public int BodyCount { get; init; }
    public double KineticEnergy { get; init; }
    public double PotentialEnergy { get; init; }
    public double TotalEnergy => KineticEnergy + PotentialEnergy;
    public Vec3d TotalMomentum { get; init; }
    public double EnergyDrift { get; init; }
}

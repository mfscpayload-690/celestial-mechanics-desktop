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
    public int ActiveBodyCount { get; init; }
    public double KineticEnergy { get; init; }
    public double PotentialEnergy { get; init; }
    public double TotalEnergy => KineticEnergy + PotentialEnergy;
    public Vec3d TotalMomentum { get; init; }
    public double EnergyDrift { get; init; }
    public int CollisionCount { get; init; }
    public IReadOnlyList<CollisionBurstEvent> CollisionBursts { get; init; } = Array.Empty<CollisionBurstEvent>();
    public double CurrentDt { get; init; }
    public double StepEnergyDelta { get; init; }
    public double StepMomentumDelta { get; init; }
    public double StepAngularMomentumDelta { get; init; }
}

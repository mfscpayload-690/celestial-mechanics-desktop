using CelestialMechanics.Math;

namespace CelestialMechanics.Physics.Types;

public readonly struct CollisionBurstEvent
{
    public Vec3d Position { get; init; }
    public double ReleasedEnergy { get; init; }
    public double CombinedMass { get; init; }
    public double EjectedMass { get; init; }
    public CollisionOutcome Outcome { get; init; }
    public int PrimaryBodyIndex { get; init; }
    public int SecondaryBodyIndex { get; init; }
}
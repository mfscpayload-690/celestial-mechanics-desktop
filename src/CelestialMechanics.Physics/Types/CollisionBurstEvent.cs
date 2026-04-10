using CelestialMechanics.Math;

namespace CelestialMechanics.Physics.Types;

public readonly struct CollisionBurstEvent
{
    public Vec3d Position { get; init; }
    public double ReleasedEnergy { get; init; }
    public double CombinedMass { get; init; }
}
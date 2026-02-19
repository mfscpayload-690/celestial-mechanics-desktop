using CelestialMechanics.Math;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.Forces;

/// <summary>
/// Interface for pluggable force calculators.
/// All forces implement this interface. The solver iterates registered calculators.
/// Adding new physics = implementing one interface. No solver code changes required.
/// </summary>
public interface IForceCalculator
{
    string Name { get; }
    bool Enabled { get; set; }
    Vec3d ComputeForce(in PhysicsBody a, in PhysicsBody b);
    double ComputePotentialEnergy(in PhysicsBody a, in PhysicsBody b);
}

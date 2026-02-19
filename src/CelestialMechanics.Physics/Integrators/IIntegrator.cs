using CelestialMechanics.Physics.Forces;
using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Physics.Integrators;

/// <summary>
/// Interface for numerical integration schemes.
/// Implementations advance the simulation by one timestep dt.
/// </summary>
public interface IIntegrator
{
    string Name { get; }
    void Step(PhysicsBody[] bodies, double dt, IForceCalculator[] forces);
}

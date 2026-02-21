using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.Events;

/// <summary>
/// Action executed when a trigger condition is met for an entity.
/// Has access to the SimulationManager for spawning/removing entities.
/// </summary>
public interface IEventAction
{
    void Execute(Entity entity);
}

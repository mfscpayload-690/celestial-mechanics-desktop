using CelestialMechanics.Simulation.Core;

namespace CelestialMechanics.Simulation.Events;

/// <summary>
/// Evaluates whether a specific condition is met for a given entity.
/// Used by SimulationManager to drive the event system.
/// </summary>
public interface ITriggerCondition
{
    bool Evaluate(Entity entity);
}
